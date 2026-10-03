# Publishing MatterDesk to GitHub

The repository is public at **https://github.com/luisescobarcorp/matter-desk-demo**, with the CLI binaries
attached to release **[v0.1.0](https://github.com/luisescobarcorp/matter-desk-demo/releases/tag/v0.1.0)**.
These are the steps that were followed, kept here so the process can be repeated for a new release or a
fork under another account.

## 1. Create the repository

On github.com: **New repository** → **Public**, no README / .gitignore / licence (the repo already has
them — `LICENSE` is MIT). Or with the CLI: `gh repo create <owner>/<name> --public --source=. --remote=origin --push`
(then skip to step 4).

## 2. Add the remote

```bash
cd matterdesk
git remote add origin https://github.com/luisescobarcorp/matter-desk-demo.git
```

## 3. Fix the commit author

The commits were originally authored as a placeholder (`luis@example.com`). They were rewritten to the
identity GitHub knows — `Luis Escobar <luisescobarcorp@users.noreply.github.com>` — before the first push,
so every commit counts on the account's contribution graph:

```bash
git -c user.name="Luis Escobar" -c user.email="luisescobarcorp@users.noreply.github.com" \
  rebase -r --root --exec 'git commit --amend --no-edit --reset-author'
```

Equivalent with `git filter-branch` (rewrites author and committer in one pass):

```bash
git filter-branch -f --env-filter '
  export GIT_AUTHOR_NAME="Luis Escobar" GIT_AUTHOR_EMAIL="luisescobarcorp@users.noreply.github.com"
  export GIT_COMMITTER_NAME="Luis Escobar" GIT_COMMITTER_EMAIL="luisescobarcorp@users.noreply.github.com"
' -- --all
```

Check with `git log --format='%h %an <%ae> %s'` before pushing. Also set `git config user.email` locally so
later commits do not need rewriting.

## 4. Scan for secrets, then push

Before the first push, grep the working tree and the whole history (`git log -p --all | grep -nE ...`) for
cloud access keys, tokens, account ids, `.env` files and database files. `.gitignore` excludes `bin/`,
`obj/`, `node_modules/`, `dist/`, `test-results/`, `playwright-report/`, `*.db`, `*.sqlite`, `.env*`,
`*.zip` and the built SPA under `src/MatterDesk.Api/wwwroot/`. `deploy/aws-apprunner.sh` reads the AWS
account id from `aws sts get-caller-identity` rather than hard-coding it.

```bash
git push -u origin main
```

## 5. Create the release and attach the CLI zips

Build the three single-file executables and zip each one (the zips are not committed; `*.zip` is ignored):

```bash
for rid in win-x64 linux-x64 osx-arm64; do
  dotnet publish src/MatterDesk.Cli -c Release -r $rid --self-contained -p:PublishSingleFile=true -o /tmp/cli/$rid
done
(cd /tmp/cli/win-x64   && zip -q ../matterdesk-cli-win-x64.zip   matterdesk.exe)
(cd /tmp/cli/linux-x64 && zip -q ../matterdesk-cli-linux-x64.zip matterdesk)
(cd /tmp/cli/osx-arm64 && zip -q ../matterdesk-cli-osx-arm64.zip matterdesk)
```

Then on github.com: **Releases → Draft a new release**, tag `v0.1.0` (create it on `main`), title
`MatterDesk 0.1.0`, attach the three zips, publish. Or:

```bash
gh release create v0.1.0 /tmp/cli/matterdesk-cli-*.zip --repo luisescobarcorp/matter-desk-demo \
  --target main --title "MatterDesk 0.1.0" \
  --notes "Local client: matterdesk demo | matters | search | documents | tools | call | mcp (stdio MCP bridge)."
```

The assets resolve to

```
https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/matterdesk-cli-win-x64.zip
https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/matterdesk-cli-linux-x64.zip
https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/matterdesk-cli-osx-arm64.zip
```

## 6. Point the installers at the release

`connectors/install.sh` and `connectors/install.ps1` default to
`https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/`. For a new release, bump
the tag in both scripts (and in `connectors/README.md` / `README.md`), commit, and push. Anyone can then run:

```bash
curl -fsSL https://raw.githubusercontent.com/luisescobarcorp/matter-desk-demo/main/connectors/install.sh | bash
```

```powershell
irm https://raw.githubusercontent.com/luisescobarcorp/matter-desk-demo/main/connectors/install.ps1 | iex
```

Users who already have a zip can bypass the release entirely with
`MATTERDESK_RELEASE_URL=https://<any-host>/<folder>/` pointing at a directory that holds the zips.

## 7. Repository metadata

Description, homepage (the App Runner URL) and topics (`dotnet`, `aspnetcore`, `efcore`, `microsoft-graph`,
`react`, `mcp`, `model-context-protocol`, `legal-tech`) were set through the REST API
(`PATCH /repos/{owner}/{repo}` and `PUT /repos/{owner}/{repo}/topics`).
