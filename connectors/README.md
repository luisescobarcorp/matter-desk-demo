# Connecting an AI client to the MatterDesk demo

Hosted demo: `https://szhzkeau4r.us-east-1.awsapprunner.com` — seeded data, no real client data.
Operators: `LES` (two matters), `JDU` (three, including the restricted one), `PAR`.

The MCP server exposes three tools — `search_matters`, `get_matter`, `list_documents` — and runs them
under the connected operator through the same `MatterAccessPolicy` the REST API uses. The client never
sees what the operator is not granted.

## Slash commands (MCP prompts)

The server also implements `prompts/list` / `prompts/get`. In **Claude Desktop** type `/` in the message
box and pick **matterdesk → demo**; in **Cursor** the prompts appear in the MCP panel for the server. They
work over every path below, including the stdio bridge, which forwards all JSON-RPC methods unchanged.

| Prompt | Arguments | What it asks the model to do |
|---|---|---|
| `demo` | none | `search_matters` for "Falcon", `list_documents` on the first hit, `get_matter`, then summarise, state which operator it acted as, and note that every step was recorded in the Activity panel |
| `find_matter` | `query` (required) | search, present hits as a table (matter number, title, client); if they all belong to one matter, list its documents |
| `file_check` | `matterNumber` (required) | get the matter, list its documents, flag anything marked privileged, summarise |

Every tool call and prompt get lands in the web app's **Activity** panel within two seconds, tagged `mcp`
with the operator and outcome — open the hosted URL in a browser next to Claude or Cursor and watch it.

## Three ways to connect

**(a) Remote HTTP MCP with a header** — for clients that can send custom headers (Cursor, VS Code,
anything using Streamable HTTP). URL `https://szhzkeau4r.us-east-1.awsapprunner.com/mcp`, header
`X-Operator-Code: LES` (or `JDU`, `PAR`). Files: [`cursor-mcp.json`](cursor-mcp.json),
[`vscode-mcp.json`](vscode-mcp.json).

**(b) Header-less remote URL** — for Claude.ai and ChatGPT custom connectors, which cannot add headers.
Put the operator in the path: `https://szhzkeau4r.us-east-1.awsapprunner.com/mcp/LES` (or `/mcp/JDU`,
`/mcp/PAR`). No authentication to configure. An unknown code is 403; if a header and a path code are both
present, the header wins. Steps: [`claude-ai-remote.md`](claude-ai-remote.md),
[`chatgpt-remote.md`](chatgpt-remote.md).

**(c) Local stdio bridge** — for Claude Desktop and any client that only speaks stdio. The `matterdesk`
executable (`matterdesk mcp --operator LES`) is a stdio MCP server that forwards every request to the
hosted `/mcp` as that operator; nothing is interpreted locally. Files:
[`claude-desktop.json`](claude-desktop.json), the stdio entry in [`cursor-mcp.json`](cursor-mcp.json).
Get the executable with [`install.sh`](install.sh) / [`install.ps1`](install.ps1) (they download the
zip for your platform from release
[v0.1.0](https://github.com/luisescobarcorp/matter-desk-demo/releases/tag/v0.1.0)), download a zip from
that release yourself, or build it with `dotnet publish src/MatterDesk.Cli -c Release -r <rid>`.

```bash
curl -fsSL https://raw.githubusercontent.com/luisescobarcorp/matter-desk-demo/main/connectors/install.sh | bash
```

```powershell
irm https://raw.githubusercontent.com/luisescobarcorp/matter-desk-demo/main/connectors/install.ps1 | iex
```

## Verify first

A recorded end-to-end validation of all three paths — raw protocol with curl, Claude via Anthropic's MCP
connector (`mcp_servers`) against `/mcp/JDU` and `/mcp/LES`, and the stdio bridge — is in
[`docs/connector-validation.md`](../docs/connector-validation.md).

```bash
matterdesk demo                      # 20 steps against the hosted API as LES, each with what the code proves
matterdesk demo --operator JDU
matterdesk tools                     # tools/list through MCP
matterdesk call search_matters query=Falcon   # total 0 as LES, hits as JDU
echo '{"jsonrpc":"2.0","id":1,"method":"prompts/list"}' | matterdesk mcp --operator JDU   # three prompts, via the bridge
```

Or with curl, no install:

```bash
curl -s -X POST https://szhzkeau4r.us-east-1.awsapprunner.com/mcp/JDU -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search_matters","arguments":{"query":"Falcon"}}}'
```

## About the operator-code scheme

This is demo data, and the `X-Operator-Code` header / `/mcp/{operatorCode}` route exist only because the
hosted instance runs with `Auth:AllowDevHeader=true`. The scheme is not registered otherwise. A production
deployment would use Entra ID (or any OIDC issuer) bearer tokens on the same `/mcp` endpoint; the tools and
the authorization predicate behind them do not change.
