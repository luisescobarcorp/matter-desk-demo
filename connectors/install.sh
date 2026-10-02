#!/usr/bin/env bash
# Download the matterdesk CLI for this machine, unzip it, and print the MCP client snippet.
#   MATTERDESK_RELEASE_URL   base URL holding matterdesk-cli-<rid>.zip (default: GitHub release v0.1.0 assets)
#   MATTERDESK_INSTALL_DIR   where to unzip (default: ~/.matterdesk)
#   MATTERDESK_OPERATOR      operator code for the snippet (default: LES)
set -euo pipefail

base="${MATTERDESK_RELEASE_URL:-https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/}"
dir="${MATTERDESK_INSTALL_DIR:-$HOME/.matterdesk}"
op="${MATTERDESK_OPERATOR:-LES}"

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64)  rid=linux-x64 ;;
  Darwin-arm64)  rid=osx-arm64 ;;
  *) echo "No prebuilt CLI for $(uname -s)-$(uname -m); run: dotnet publish src/MatterDesk.Cli -c Release -r <rid>" >&2; exit 1 ;;
esac

zip="matterdesk-cli-$rid.zip"
mkdir -p "$dir"
echo "Downloading ${base%/}/$zip"
curl -fsSL -o "$dir/$zip" "${base%/}/$zip"
unzip -qo "$dir/$zip" -d "$dir" && rm -f "$dir/$zip"
chmod +x "$dir/matterdesk"
exe="$dir/matterdesk"

echo
echo "Installed $exe"
echo "Try it:  $exe demo --operator $op"
echo
echo "Claude Desktop (claude_desktop_config.json) / Cursor (~/.cursor/mcp.json):"
cat <<EOF
{
  "mcpServers": {
    "matterdesk": {
      "command": "$exe",
      "args": ["mcp", "--operator", "$op"]
    }
  }
}
EOF
