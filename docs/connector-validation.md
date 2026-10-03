# MatterDesk MCP connector validation

- **Date:** 2026-10-02 (UTC, ~23:32–23:40)
- **Endpoint:** `https://szhzkeau4r.us-east-1.awsapprunner.com/mcp/{operatorCode}` (Streamable HTTP, JSON-RPC over POST, protocol `2025-06-18`)
- **Operators tested:** `JDU` (granted the restricted matter), `LES` (not granted), `NOPE` (unknown → 403)
- **Claude model:** `claude-sonnet-4-5` (resolved by the API to `claude-sonnet-4-5-20250929`)

| Section | Verdict |
|---|---|
| 1. Raw HTTP protocol smoke (curl) | **PASS** |
| 2a. Anthropic MCP connector, `/mcp/JDU` | **PASS** – 2 `mcp_tool_use` + 2 `mcp_tool_result` blocks, correct answer |
| 2b. Anthropic MCP connector, `/mcp/LES` | **PASS** – Claude reports zero Falcon matters (visibility enforced per operator) |
| 2c. Anthropic MCP connector, `/mcp/NOPE` | **PASS (expected failure)** – API returns HTTP 400 `invalid_request_error`, server 403 surfaced as "Error while communicating with MCP server" |
| 3. Local stdio bridge (`matterdesk mcp --operator …`) | **PASS** – byte-identical `structuredContent` to raw HTTP |

---

## 1. Raw protocol smoke (curl)

Request shape for every call:

```
POST https://szhzkeau4r.us-east-1.awsapprunner.com/mcp/<CODE>
Content-Type: application/json
Accept: application/json, text/event-stream
MCP-Protocol-Version: 2025-06-18
```

Observations: the server answers with plain `application/json` (not SSE), returns `mcp-protocol-version: 2025-06-18`
in the response headers, and does **not** issue an `Mcp-Session-Id` header (stateless mode), so nothing had to be echoed back.

### 1.1 `initialize` → `/mcp/JDU` — HTTP 200

```json
{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{"tools":{"listChanged":false}},
 "serverInfo":{"name":"matterdesk","version":"0.1.0"},
 "instructions":"MatterDesk tools act as operator JDU. Restricted matters the operator has not been granted are invisible; do not try to infer their existence."}}
```

### 1.2 `notifications/initialized` → `/mcp/JDU` — HTTP 202, empty body (correct for a notification)

### 1.3 `tools/list` → `/mcp/JDU` — HTTP 200

Three tools returned, each with a JSON-schema `inputSchema`:

| name | required input | description (abridged) |
|---|---|---|
| `search_matters` | `query` (string, ≥2 chars), optional `limit` | Search matters, document profiles and profiled email the current operator may see |
| `get_matter` | `matterNumber` (CCCCC-MMMM) | Profile of one matter incl. whether the operator may edit it |
| `list_documents` | `matterNumber`, optional `limit` | Document profiles filed to a matter; fails if restricted and no grant |

### 1.4 `tools/call search_matters {"query":"Falcon"}` → `/mcp/JDU` — HTTP 200, **total 3**

```json
{"operator":"JDU","query":"Falcon","total":3,"hits":[
 {"kind":"document","id":6,"matterNumber":"10099-0001","title":"Falcon — board memo","snippet":"Falcon; board; confidential","whenUtc":"2026-02-08T14:00:00"},
 {"kind":"document","id":5,"matterNumber":"10099-0001","title":"Falcon — separation term sheet (PRIVILEGED)","snippet":"Falcon; separation; privileged; Acme","whenUtc":"2026-02-06T14:00:00"},
 {"kind":"matter","id":3,"matterNumber":"10099-0001","title":"Project Falcon — partner departure dispute","snippet":"Confidential Client (Restricted)","whenUtc":"2026-02-04T14:00:00"}]}
```
(returned both as a `content[0].text` JSON string and as `structuredContent`; `isError:false`)

### 1.5 `tools/call search_matters {"query":"Falcon"}` → `/mcp/LES` — HTTP 200, **total 0**

```json
{"operator":"LES","query":"Falcon","total":0,"hits":[]}
```

### 1.6 `initialize` → `/mcp/NOPE` — **HTTP 403**

```json
{"title":"Authenticated identity is not a known operator.","status":403}
```

### 1.7 Header variant: `POST /mcp` + `X-Operator-Code: JDU`, `tools/call search_matters Falcon` — HTTP 200, total 3 (identical payload to 1.4)

**Verdict:** PASS — handshake, tool listing, per-operator visibility (3 vs 0) and 403 for unknown operator all behave as specified.

---

## 2. Anthropic MCP connector (Messages API)

Request shape (identical for all three runs except the `url`):

```
POST https://api.anthropic.com/v1/messages
x-api-key: <redacted>
anthropic-version: 2023-06-01
anthropic-beta: mcp-client-2025-04-04
content-type: application/json

{
  "model": "claude-sonnet-4-5",
  "max_tokens": 1024,
  "mcp_servers": [{"type":"url","url":"https://szhzkeau4r.us-east-1.awsapprunner.com/mcp/<CODE>","name":"matterdesk"}],
  "messages": [{"role":"user","content":"Using the matterdesk tools, search for matters mentioning 'Falcon', then list the documents on the first result and summarise what you found in three bullet points."}]
}
```

No API errors were encountered: the beta header, `mcp_servers` field and model id were all accepted on the first attempt.

### 2a. `/mcp/JDU` — HTTP 200, `stop_reason: end_turn`

Content block sequence: `text → mcp_tool_use → mcp_tool_result → text → mcp_tool_use → mcp_tool_result → text`

Tool-call sequence made by Claude:

1. `mcp_tool_use` `search_matters` (server `matterdesk`) input `{"query":"Falcon"}`
   → `mcp_tool_result` `is_error:false`, payload = the same 3-hit JSON as §1.4 (`operator: JDU`, `total: 3`)
2. `mcp_tool_use` `list_documents` (server `matterdesk`) input `{"matterNumber":"10099-0001"}`
   → `mcp_tool_result` `is_error:false`:

```json
{"matterNumber":"10099-0001","count":2,"documents":[
 {"id":6,"title":"Falcon — board memo","documentType":"Memo","authorCode":"JDU","keywords":"Falcon; board; confidential","modifiedUtc":"2026-02-08T14:00:00","versionCount":1},
 {"id":5,"title":"Falcon — separation term sheet (PRIVILEGED)","documentType":"Agreement","authorCode":"JDU","keywords":"Falcon; separation; privileged; Acme","modifiedUtc":"2026-02-06T14:00:00","versionCount":1}]}
```

Claude's final text (verbatim):

> ## Summary
>
> Here's what I found in three bullet points:
>
> • **Matter identified**: "Project Falcon — partner departure dispute" (matter #10099-0001) for a Confidential/Restricted client, relating to a partner departure dispute
>
> • **Two documents filed**: A board memo and a privileged separation term sheet, both authored by JDU and created in early February 2026
>
> • **Sensitive nature**: Both documents are marked as confidential/privileged and appear to relate to separation negotiations with a partner, possibly involving a company called Acme

Usage: 3,927 input / 291 output tokens.

**Verdict:** PASS — Claude discovered the tools via the connector, chained two tool calls and produced a correct summary.

### 2b. `/mcp/LES` — HTTP 200, `stop_reason: end_turn`

Content block sequence: `text → mcp_tool_use → mcp_tool_result → text`

1. `mcp_tool_use` `search_matters` input `{"query":"Falcon"}`
   → `mcp_tool_result` `is_error:false`: `{"operator":"LES","query":"Falcon","total":0,"hits":[]}`

Claude's final text (verbatim):

> The search for 'Falcon' returned no results. Let me summarize what I found:
>
> **Summary:**
>
> • **No matters found**: The search for "Falcon" did not return any matching matters in the system
>
> • **Search scope**: The search checked matter numbers, titles, client names, document titles/keywords, and email subjects
>
> • **No documents to list**: Since no matters were found, there are no documents to list from a first result
>
> It appears there are currently no matters in the system that mention "Falcon" in any of the searchable fields.

Usage: 1,988 input / 207 output tokens.

**Verdict:** PASS — same prompt, same server, different operator: the restricted matter never reaches the model.

### 2c. `/mcp/NOPE` — HTTP 400 (no message created, no tokens billed)

```json
{"type":"error","error":{"type":"invalid_request_error","message":"mcp_servers[0] 'matterdesk': Error while communicating with MCP server."},"request_id":"<redacted>"}
```

The server's 403 during the connector's `initialize` is surfaced by the Anthropic API as a request-level error rather than a tool error block — Claude is never invoked.

**Verdict:** PASS (expected failure mode) — an unknown operator cannot be used through the connector at all.

---

## 3. Local stdio bridge

Binary: `/tmp/cli/linux-x64/matterdesk` (prebuilt). `matterdesk mcp --operator <CODE>` starts a stdio MCP server that forwards
every JSON-RPC message to the hosted `/mcp` as that operator (stderr banner: `matterdesk mcp bridge → https://szhzkeau4r.us-east-1.awsapprunner.com/mcp as JDU`).
This is the exact command used by `connectors/claude-desktop.json` and the `matterdesk-local` entry of `connectors/cursor-mcp.json`.

Input piped to stdin (newline-delimited JSON-RPC):

```
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"stdio-smoke","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"search_matters","arguments":{"query":"Falcon"}}}
```

### `--operator JDU` (exit 0)

```
{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{"tools":{"listChanged":false}},"serverInfo":{"name":"matterdesk","version":"0.1.0"},"instructions":"MatterDesk tools act as operator JDU. ..."}}
{"id":2,"tools":["search_matters","get_matter","list_documents"]}
{"id":3,"structuredContent":{"operator":"JDU","query":"Falcon","total":3,"hits":["10099-0001","10099-0001","10099-0001"]}}
```
(the notification produced no stdout line, as required)

`diff` of the `structuredContent` from the stdio response vs. a fresh raw HTTP `tools/call` to `/mcp/JDU`: **identical**.

### `--operator LES` (exit 0)

```
{"jsonrpc":"2.0","id":1,"result":{... "instructions":"MatterDesk tools act as operator LES. ..."}}
{"id":3,"structuredContent":{"operator":"LES","query":"Falcon","total":0,"hits":[]}}
```

**Verdict:** PASS — the stdio bridge is a transparent proxy; Claude Desktop / Cursor / VS Code stdio configs will see the same tools and the same per-operator results as the remote URL.

---

## Overall

All sections pass. The hosted MatterDesk MCP server works as a remote connector for the Anthropic Messages API
(`mcp-client-2025-04-04` beta), enforces operator visibility end to end (JDU sees matter 10099-0001 and its two documents;
LES sees nothing; NOPE is rejected at handshake), and the local stdio bridge is behaviourally identical to direct HTTP.
