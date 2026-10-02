#!/usr/bin/env bash
# Builds the React app into the API's wwwroot, publishes a single container image to ECR without Docker
# (dotnet publish /t:PublishContainer), and creates or updates an AWS App Runner service that runs it.
#
# Requires: AWS CLI v2 with credentials (AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY / AWS_DEFAULT_REGION),
#           .NET 8 SDK, Node 20+. The IAM principal needs ECR, App Runner, IAM (role create/pass) and
#           CloudWatch Logs permissions.
#
# Usage: deploy/aws-apprunner.sh [tag]          # tag defaults to the short git SHA
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SERVICE="${SERVICE_NAME:-matterdesk}"
REPO="${ECR_REPOSITORY:-matterdesk}"
TAG="${1:-$(git -C "$ROOT" rev-parse --short HEAD)}"
REGION="${AWS_DEFAULT_REGION:-${AWS_REGION:-us-east-1}}"
ACCOUNT="$(aws sts get-caller-identity --query Account --output text)"
REGISTRY="${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"
IMAGE="${REGISTRY}/${REPO}:${TAG}"
ROLE_NAME="${APPRUNNER_ECR_ROLE:-AppRunnerECRAccessRole-${SERVICE}}"

export DOTNET_CLI_TELEMETRY_OPTOUT=1

echo "==> Building the React app into wwwroot"
(cd "$ROOT/src/MatterDesk.Web" && npm ci --silent && npm run build --silent)
rm -rf "$ROOT/src/MatterDesk.Api/wwwroot"/*
cp -r "$ROOT/src/MatterDesk.Web/dist/." "$ROOT/src/MatterDesk.Api/wwwroot/"

echo "==> Ensuring ECR repository ${REPO}"
aws ecr describe-repositories --repository-names "$REPO" --region "$REGION" >/dev/null 2>&1 \
  || aws ecr create-repository --repository-name "$REPO" --region "$REGION" --image-scanning-configuration scanOnPush=true >/dev/null

echo "==> Writing registry credentials for the .NET container publish (no Docker daemon needed)"
mkdir -p "$HOME/.docker"
PASSWORD="$(aws ecr get-login-password --region "$REGION")"
python3 - "$REGISTRY" "$PASSWORD" <<'PY'
import base64, json, os, sys
registry, password = sys.argv[1], sys.argv[2]
path = os.path.expanduser("~/.docker/config.json")
cfg = {}
if os.path.exists(path):
    with open(path) as f:
        try: cfg = json.load(f)
        except Exception: cfg = {}
cfg.setdefault("auths", {})[registry] = {"auth": base64.b64encode(f"AWS:{password}".encode()).decode()}
with open(path, "w") as f: json.dump(cfg, f)
os.chmod(path, 0o600)
PY

echo "==> Publishing ${IMAGE}"
dotnet publish "$ROOT/src/MatterDesk.Api" -c Release --os linux --arch x64 /t:PublishContainer \
  -p:ContainerRegistry="$REGISTRY" -p:ContainerRepository="$REPO" -p:ContainerImageTag="$TAG" -nologo -v minimal

echo "==> Ensuring App Runner can pull from ECR (${ROLE_NAME})"
if ! aws iam get-role --role-name "$ROLE_NAME" >/dev/null 2>&1; then
  aws iam create-role --role-name "$ROLE_NAME" --assume-role-policy-document '{
    "Version": "2012-10-17",
    "Statement": [{"Effect": "Allow", "Principal": {"Service": "build.apprunner.amazonaws.com"}, "Action": "sts:AssumeRole"}]
  }' >/dev/null
  aws iam attach-role-policy --role-name "$ROLE_NAME" --policy-arn arn:aws:iam::aws:policy/service-role/AWSAppRunnerServicePolicyForECRAccess
  sleep 10   # IAM propagation
fi
ROLE_ARN="$(aws iam get-role --role-name "$ROLE_NAME" --query Role.Arn --output text)"

SOURCE_CONFIG="$(cat <<JSON
{
  "ImageRepository": {
    "ImageIdentifier": "${IMAGE}",
    "ImageRepositoryType": "ECR",
    "ImageConfiguration": {
      "Port": "8080",
      "RuntimeEnvironmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Production",
        "Auth__AllowDevHeader": "true",
        "ConnectionStrings__Sqlite": "Data Source=/tmp/matterdesk.db"
      }
    }
  },
  "AutoDeploymentsEnabled": false,
  "AuthenticationConfiguration": { "AccessRoleArn": "${ROLE_ARN}" }
}
JSON
)"

SERVICE_ARN="$(aws apprunner list-services --region "$REGION" --query "ServiceSummaryList[?ServiceName=='${SERVICE}'].ServiceArn | [0]" --output text)"
if [[ -z "$SERVICE_ARN" || "$SERVICE_ARN" == "None" ]]; then
  echo "==> Creating App Runner service ${SERVICE}"
  SERVICE_ARN="$(aws apprunner create-service --region "$REGION" --service-name "$SERVICE" \
    --source-configuration "$SOURCE_CONFIG" \
    --instance-configuration Cpu="0.25 vCPU",Memory="0.5 GB" \
    --health-check-configuration Protocol=HTTP,Path=/healthz,Interval=10,Timeout=5,HealthyThreshold=1,UnhealthyThreshold=5 \
    --query Service.ServiceArn --output text)"
else
  echo "==> Updating App Runner service ${SERVICE}"
  aws apprunner update-service --region "$REGION" --service-arn "$SERVICE_ARN" --source-configuration "$SOURCE_CONFIG" >/dev/null
fi

echo "==> Waiting for the service to be RUNNING"
for _ in $(seq 1 90); do
  STATUS="$(aws apprunner describe-service --region "$REGION" --service-arn "$SERVICE_ARN" --query Service.Status --output text)"
  [[ "$STATUS" == "RUNNING" ]] && break
  [[ "$STATUS" == *FAILED* ]] && { echo "Service status: $STATUS"; exit 1; }
  sleep 10
done
URL="https://$(aws apprunner describe-service --region "$REGION" --service-arn "$SERVICE_ARN" --query Service.ServiceUrl --output text)"
echo "==> ${STATUS}: ${URL}"
curl -fsS "${URL}/healthz" && echo
curl -fsS -H "X-Operator-Code: LES" "${URL}/api/matters?pageSize=1" | head -c 200 && echo
