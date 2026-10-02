# Download the matterdesk CLI for Windows, unzip it, and print the MCP client snippet.
#   MATTERDESK_RELEASE_URL   base URL holding matterdesk-cli-win-x64.zip (default: GitHub release v0.1.0 assets)
#   MATTERDESK_INSTALL_DIR   where to unzip (default: %LOCALAPPDATA%\MatterDesk)
#   MATTERDESK_OPERATOR      operator code for the snippet (default: LES)
$ErrorActionPreference = 'Stop'

$base = if ($env:MATTERDESK_RELEASE_URL) { $env:MATTERDESK_RELEASE_URL } else { 'https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/' }
$dir  = if ($env:MATTERDESK_INSTALL_DIR) { $env:MATTERDESK_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'MatterDesk' }
$op   = if ($env:MATTERDESK_OPERATOR)    { $env:MATTERDESK_OPERATOR }    else { 'LES' }

$zip = 'matterdesk-cli-win-x64.zip'
$url = $base.TrimEnd('/') + '/' + $zip
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile (Join-Path $dir $zip)
Expand-Archive -Path (Join-Path $dir $zip) -DestinationPath $dir -Force
Remove-Item (Join-Path $dir $zip)
$exe = (Resolve-Path (Join-Path $dir 'matterdesk.exe')).Path

Write-Host ""
Write-Host "Installed $exe"
Write-Host "Try it:  & `"$exe`" demo --operator $op"
Write-Host ""
Write-Host "Claude Desktop (%APPDATA%\Claude\claude_desktop_config.json) / Cursor (%USERPROFILE%\.cursor\mcp.json):"
$snippet = [ordered]@{
  mcpServers = [ordered]@{
    matterdesk = [ordered]@{
      command = $exe
      args    = @('mcp', '--operator', $op)
    }
  }
}
$snippet | ConvertTo-Json -Depth 5
