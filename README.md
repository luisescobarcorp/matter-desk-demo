# MatterDesk

A small, complete slice of a matter-centric document and email profiling API, built to the shape of a
law-practice "all-in-one" system: the **matter is the hub**, documents and email are **profiled** onto it,
and **permissions live in the query**.

Stack: ASP.NET Core 8 (controllers) · EF Core 8 (SQL Server / SQLite) · Microsoft Graph SDK 6 (delta sync) ·
OIDC bearer auth · MCP server (tools for AI clients) · React 19 + Vite + TypeScript · xUnit integration tests ·
Playwright end-to-end · Azure Pipelines.

Prepared by Luis Escobar for PerfectLaw, October 2026. Source:
**[github.com/luisescobarcorp/matter-desk-demo](https://github.com/luisescobarcorp/matter-desk-demo)**;
hosted demo: **https://szhzkeau4r.us-east-1.awsapprunner.com**. The background behind it — how I think about the
Microsoft platform (Azure / Microsoft 365 / Entra / Graph / EWS), the integration and automation work that
transfers to a Graph sync layer, MCP, and my database experience stated exactly — is in
**[docs/BACKGROUND.md](docs/BACKGROUND.md)** and on the app's **About this project** page.
It also covers BTCEdge, a personal real-time trading system (TypeScript, 442K lines, MCP server, ~6,900 tests) that is the strongest evidence for the concurrency, auth, testing and AI-assisted-development parts of the posting.

## Start here

Five minutes, in this order:

1. **Open the live app** — [https://szhzkeau4r.us-east-1.awsapprunner.com](https://szhzkeau4r.us-east-1.awsapprunner.com). You are signed in as **LES — Luis Escobar (attorney)**. The list is a small firm's book (11 general matters). Switch **Signed in as** to **JDU — J. Duncan** and the list grows to 18, including restricted matter **10099-0001 Project Falcon** (red badge, two documents). **PAR** sees 17: every restricted matter except Falcon.
2. **Search `Falcon`.** As LES the result is empty (total 0). As JDU it is the matter plus its two documents (total 3). Same query, same SQL, different operator.
3. **Watch the Activity panel** on the right of every page. A search typed in the browser appears as a `web` row within two seconds. Leave the tab open for the next two steps.
4. **Run the executable.** Release [v0.1.0](https://github.com/luisescobarcorp/matter-desk-demo/releases/tag/v0.1.0): [Windows](https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/matterdesk-cli-win-x64.zip) (`matterdesk.exe`), [Linux](https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/matterdesk-cli-linux-x64.zip), [macOS arm64](https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/matterdesk-cli-osx-arm64.zip). Unzip and run `matterdesk demo` — a 20-step REST walkthrough (401, 403, the permission boundary, 428/409 on a profile edit, then MCP). The scratch document is deleted at the end. Each call shows up in the Activity panel as a `cli` row.
5. **Connect an AI client.** [connectors/README.md](connectors/README.md) covers Claude Desktop, Claude.ai, ChatGPT, Cursor and VS Code (header, `/mcp/{operator}`, or the stdio bridge). Slash commands shipped by the server: `demo`, `find_matter`, `file_check`. A tool call shows up as an `mcp` row.

Demo video (captioned, about two minutes): [matterdesk-demo.mp4](https://github.com/luisescobarcorp/matter-desk-demo/releases/download/v0.1.0/matterdesk-demo.mp4).

Below: why the architecture is shaped this way, how to clone and run it, the test suites, and an index of the longer docs.

![Matter documents](docs/screenshot_matter_documents.png)

## Why these particular choices

| Concern | What this project does | Why |
|---|---|---|
| **Authorization** | `MatterAccessPolicy.VisibleTo()` is an `IQueryable` extension composed into every read. It translates to an `EXISTS` probe against `MatterAccess`. | A user outside a restricted matter must get an **empty result**, not a row filtered after the fact. Search, lists and detail all share one predicate. |
| **403 vs 404** | `GET /api/matters/{id}` returns 403 when the matter exists but is restricted; `GET /api/documents/{id}` returns 404. | Matter numbers are on every bill and are not secret, so an explicit, auditable denial is fine. A document id must not reveal that a restricted file exists. |
| **Concurrency** | `Document.Version` is an EF concurrency token. `PUT /api/documents/{id}/profile` requires `If-Match` and returns **409** with a problem-details body on mismatch, **428** if the header is missing. | Two people editing the same profile must not silently overwrite each other. On SQL Server this would normally be a `rowversion` column; a counter is used so the same model runs on SQLite in tests. |
| **Transactions** | Profile row + first version row are created in a single `SaveChanges`. | They must commit together. |
| **Pagination** | Mandatory on every list; `pageSize` clamped to 200; `PagedResult<T>` with `total` and `hasMore`. | Mail and document tables are large. |
| **Errors** | `AddProblemDetails()` plus an explicit `InvalidModelStateResponseFactory`; every failure is RFC 7807 `application/problem+json`. | One error shape for the React client and the tests. |
| **Graph** | `GraphMailSource` uses **delta queries** on the Inbox, persists the delta link per operator, and marks filed messages with an Outlook **category** (`Filed: 10042-0003`). Delegated (device code) and Application (client credentials) modes. | Replaces the EWS-era approach (public-folder watcher / MAPI polling) that Exchange Online stops serving on 1 April 2027. Never pulls a full mailbox twice. |
| **Idempotency** | `(MatterId, ExternalMessageId)` is unique; a message already filed is skipped; the profile row is durable *before* the marker is attempted and a marker failure does not roll it back. | Sync must be safe to re-run after any partial failure. |
| **Auth plumbing** | A policy scheme forwards to **JWT Bearer** (Entra ID / any OIDC issuer) when an `Authorization` header is present; in Development/Testing only, an `X-Operator-Code` header scheme is available. | Production uses OIDC; tests and local demos do not need a tenant. The dev scheme is not registered outside those environments. |
| **Search** | `UNION ALL` of matters, documents and emails projected to one shape server-side, ordered and paged in SQL. | On SQL Server the text match would use the full-text index (`CONTAINS`); `LIKE` keeps the plan shape identical on SQLite. |
| **Data access** | EF Core with explicit indexes in `OnModelCreating` (`(OperatorId, MatterId)` for the permission probe; `(MatterId, DocumentType)`; `(MatterId, ExternalMessageId)` unique). | The predicate and the hot paths have the indexes they need. Stored procedures would still be used where a rule is shared with a desktop client. |
| **MCP** | `POST /mcp` is a minimal Model Context Protocol server (Streamable HTTP, protocol `2025-06-18`) exposing `search_matters`, `get_matter` and `list_documents`. Tools run under the authenticated operator and reuse `SearchService` and `MatterAccessPolicy`. | An agent (Copilot, Claude, Cursor, a custom assistant) gets governed, permission-aware access to the same data with no copy of it anywhere. The server, not the model, enforces who sees what. |

## Coverage against the posting

| Posting line | Here |
|---|---|
| REST conventions (routing, methods, status codes, validation, errors, authorization, filtering, sorting) | `Controllers/`, problem+json everywhere, Swagger |
| SQL Server: indexes, plans, concurrency, transactions, large DMS data | `Data/MatterDeskDbContext.cs`, `Document.Version`, paging, server-side `UNION ALL` |
| React across the API boundary | `src/MatterDesk.Web` |
| Profiles, metadata, search, permissions, versions, relationships, full-text | `Domain/`, `Authorization/`, `Search/` |
| Microsoft Graph / Exchange–Outlook | `Mail/GraphMailSource.cs`, `Mail/EmailProfilingService.cs` |
| OAuth / OIDC | `Auth/`, `Program.cs` |
| Azure DevOps / CI | `azure-pipelines.yml` |
| Automated testing incl. Playwright | `tests/` (28 API + 9 e2e) |
| AI-assisted development with human-owned review | "How this was built" below; MCP server at `/mcp`; `matterdesk mcp` stdio bridge |

The full line-by-line map, including what is marked *ramping* or *not yet*, is in [docs/BACKGROUND.md](docs/BACKGROUND.md#5-the-posting-requirement-by-requirement).

## Run it

Prerequisites: .NET 8 SDK, Node 20+, Google Chrome (for Playwright `channel: 'chrome'`).

```bash
git clone https://github.com/luisescobarcorp/matter-desk-demo.git
cd matter-desk-demo
dotnet build            # whole solution: API, CLI, tests

# API (SQLite by default; set ConnectionStrings__SqlServer to use SQL Server)
cd src/MatterDesk.Api
dotnet run --urls http://localhost:5080
# Swagger UI: http://localhost:5080/swagger  (use the DevHeader lock: LES, JDU or PAR)

# Web
cd src/MatterDesk.Web
npm install && npm run dev      # http://localhost:5173, proxies /api to :5080
```

Try the permission boundary directly:

```bash
curl -H "X-Operator-Code: LES" http://localhost:5080/api/matters                 # 11 matters (the general ones)
curl -H "X-Operator-Code: JDU" http://localhost:5080/api/matters                 # 18 matters (incl. 7 restricted)
curl -H "X-Operator-Code: PAR" http://localhost:5080/api/matters                 # 17 matters (every restricted one but 10099-0001)
curl -H "X-Operator-Code: LES" "http://localhost:5080/api/search?q=Falcon"       # total: 0
curl -H "X-Operator-Code: JDU" "http://localhost:5080/api/search?q=Falcon"       # matter + 2 documents
```

And the same boundary through MCP (what an AI client would send):

```bash
curl -s -X POST http://localhost:5080/mcp -H "Content-Type: application/json" -H "X-Operator-Code: LES" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
curl -s -X POST http://localhost:5080/mcp -H "Content-Type: application/json" -H "X-Operator-Code: LES" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"search_matters","arguments":{"query":"Falcon"}}}'   # total: 0
curl -s -X POST http://localhost:5080/mcp -H "Content-Type: application/json" -H "X-Operator-Code: JDU" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_documents","arguments":{"matterNumber":"10099-0001"}}}'
```

To point an MCP client at it, configure an HTTP server with URL `http://localhost:5080/mcp` and the
`X-Operator-Code` header (or a Bearer token in production). Remote connectors that cannot send custom
headers (Claude.ai, ChatGPT) can use `http://localhost:5080/mcp/JDU` instead: the route segment is read by
the same dev-header scheme, so it only exists where that scheme is enabled, a header still wins when both
are present, and an unknown code is still 403.

**Slash commands.** The server also implements `prompts/list` / `prompts/get` and advertises `prompts` in
its `initialize` capabilities. Three prompts ship: `demo` (no arguments — search for Falcon, list documents,
open the matter, report what was visible and as whom), `find_matter` (`query`) and `file_check`
(`matterNumber`). In Claude Desktop type `/` in the message box and pick **matterdesk → demo**; in Cursor
the prompts appear in the MCP panel for the server. They work through the stdio bridge unchanged because
`matterdesk mcp` forwards every JSON-RPC method as-is. Each `prompts/get` is itself recorded in the
Activity panel as `prompt.<name>`.

## See it happen

The right-hand **Activity** panel on every page of the web app is a live log of governed actions. The API
writes one `ActivityEvent` row per search, document create/update and MCP tool call (`IActivityRecorder`,
a single insert on the request's own context), tagged with the operator, the outcome (`ok`, `denied`,
`conflict`, `error`), the HTTP status and the **channel**: `web` for the browser, `cli` when the request
carries `X-Client: matterdesk-cli` (the executable sends it by default), `mcp` for anything through `/mcp`.
The panel polls `GET /api/activity?after=<id>` every two seconds and flashes a toast for each new row, so a
`search_matters` call made from Claude Desktop or Cursor, a `document.create` from the EXE and a search
typed into the browser all appear in the same list within a couple of seconds — as the same operator-scoped
log, including the denied attempts. The feed is visible to any authenticated operator (it carries matter
numbers, document ids and query text, never titles), which is the point: it is an audit trail of who did
what through which surface.

## Local client

`src/MatterDesk.Cli` is a small executable that talks to the API over HTTP — the same shape as a desktop
component talking to a web tier: it authenticates, treats 401/403/404/409/428 as decisions rather than
faults, retries only transient failures (429/503/504 and socket errors, with backoff and `Retry-After`),
and never assumes the server is the build it was compiled against.

```bash
dotnet run --project src/MatterDesk.Cli -- demo                      # against the hosted demo, as LES
dotnet run --project src/MatterDesk.Cli -- demo --api http://localhost:5080 --operator JDU
```

Ad-hoc calls, each its own command: `matterdesk matters`, `matterdesk search Falcon`, `matterdesk documents 10099-0001`, `matterdesk tools`, `matterdesk call search_matters query=Falcon`.

`demo` walks the whole surface and prints what each status code proves: 401 with no identity, 403 for an
unknown operator, the restricted matter visible to JDU only, 403 vs 404 on restricted matter vs document,
zero search hits for the ungranted operator, problem+json on validation, 201 → 428 → 200 → 409 → 403 on a
profile edit, and the MCP tools returning exactly what REST returns for the same operator.

`matterdesk mcp` turns the executable into a **stdio MCP server** that bridges to the hosted `/mcp`
endpoint as the configured operator — the transport Claude Desktop, Cursor and VS Code speak. Nothing is
interpreted locally, so the server's authorization is the only authorization:

```json
{ "mcpServers": { "matterdesk": { "command": "matterdesk", "args": ["mcp", "--operator", "JDU"] } } }
```

`dotnet publish src/MatterDesk.Cli -c Release -r win-x64` (or `linux-x64`, `osx-arm64`) produces a single
self-contained file with no .NET install required.

**Local client (EXE).** Prebuilt zips — `matterdesk-cli-win-x64.zip` (`matterdesk.exe`),
`matterdesk-cli-linux-x64.zip`, `matterdesk-cli-osx-arm64.zip` — are attached to GitHub release
**[v0.1.0](https://github.com/luisescobarcorp/matter-desk-demo/releases/tag/v0.1.0)**; each holds one
self-contained executable, nothing to install. `connectors/install.sh` / `install.ps1` download the right
one from that release and print the Claude Desktop / Cursor snippet with the absolute path filled in:

```bash
curl -fsSL https://raw.githubusercontent.com/luisescobarcorp/matter-desk-demo/main/connectors/install.sh | bash
```

```powershell
irm https://raw.githubusercontent.com/luisescobarcorp/matter-desk-demo/main/connectors/install.ps1 | iex
```

Ready-made
connector files for Claude Desktop, Claude.ai, ChatGPT, Cursor and VS Code, and the three ways to connect
(header, header-less `/mcp/{operator}` URL, local stdio bridge), are in
**[connectors/README.md](connectors/README.md)**. A captured run of `matterdesk demo` against the hosted
API is in [docs/cli-demo-output.txt](docs/cli-demo-output.txt). A recorded end-to-end validation — raw
protocol, Claude via Anthropic's MCP connector, and the stdio bridge — is in
[docs/connector-validation.md](docs/connector-validation.md).

## Hosted demo

A hosted copy runs with the seeded demo data (no real client data) and the operator header enabled
through `Auth:AllowDevHeader=true`. The seed is a small Miami firm's book: 11 clients, 18 matters across
commercial litigation, real estate, employment, estate planning, insurance defence, M&A, construction
lien and immigration work (open, on hold and closed), 77 documents with versions, filed email, and a few
days of Activity history (`Data/Seed.cs` for the three matters the tests pin, `Data/Seed.Firm.cs` for the
rest, added idempotently by matter number). Switch the operator in the top-right to see the permission boundary
move; open **About this project** for the background. Live at **https://szhzkeau4r.us-east-1.awsapprunner.com** (Swagger at `/swagger`, MCP at `/mcp`, or `/mcp/LES`, `/mcp/JDU`, `/mcp/PAR` for connectors that cannot send a header).

How it is hosted: one container (the API serves the React build from `wwwroot`, with a SPA fallback
that leaves `/api`, `/mcp` and `/swagger` alone), built by `dotnet publish /t:PublishContainer` with
no Docker daemon, pushed to Amazon ECR and run on AWS App Runner behind its managed HTTPS endpoint.
`deploy/aws-apprunner.sh` does the whole thing idempotently (ECR repo, image, pull role, service
create-or-update, health check on `/healthz`). The same image runs anywhere a container runs,
including Azure App Service or Azure Container Apps.

## Tests

```bash
# API integration tests: real pipeline, SQLite in-memory, fake Graph source — 28 tests
dotnet test

# End-to-end: boots the API and Vite, drives the React screen in Chrome — 9 tests
cd tests/e2e && npm install && npx playwright test
```

What the suites prove:

- **Permissions** — unauthenticated → 401; unknown operator → 403; the restricted matter is absent from lists; `GET` is 200 for the granted operator and 403 for the other; its documents are 404 for the other; search returns zero hits for `Falcon` and never shows a restricted document even when a keyword (`Acme`) matches; a viewer without `CanEdit` cannot profile.
- **Profiles** — 201 with `Location`, version 1 and one version row; validation errors are `application/problem+json`; `PUT` without `If-Match` → 428; stale version → 409; reload and retry → 200; `ETag` matches the version.
- **Mail sync** — delta paging across pages; second sync scans zero and resumes from the stored delta link; a message tagged with a restricted matter is **not** filed by an ungranted operator but is by a granted one; marker failure keeps the profile and reports `MarkerSet = false`; per-matter sync files only into that matter and honours edit rights.
- **MCP** — unauthenticated → 401; `initialize` / `tools/list` describe three tools; `search_matters` for `Falcon` returns zero hits as LES and hits as JDU; `get_matter` / `list_documents` on the restricted matter return an error for LES that does not reveal the title, and data for JDU; an unsupported method is a JSON-RPC `-32601`, not a crash; `/mcp/{operatorCode}` enforces the same boundary as the header, 403 for an unknown code, header wins when both are present.
- **Activity and prompts** — an MCP `tools/call` is recorded with channel `mcp`, a readable summary and `denied` for the ungranted operator without leaking the title; REST search is `web` and a document create with `X-Client: matterdesk-cli` is `cli`; `initialize` advertises `prompts`; `prompts/list` returns `demo`, `find_matter`, `file_check`; `prompts/get demo` returns a user message naming the operator and the Activity panel, missing/unknown prompts are `-32602`, and the get is logged as `prompt.demo`.
- **End-to-end** — the React screen shows the right matters per operator, loads documents and email, shows no results for restricted content, confirms the API returns 403 and a zero-hit search for the ungranted operator, opens the About page from the nav and from `#about`, exercises the MCP endpoint over HTTP, and the Activity panel shows a `web` row for a browser search and an `mcp` row plus a toast for a `tools/call` posted to `/mcp/JDU`.

## Connect Microsoft Graph (optional)

1. Register an app in Entra ID. For delegated mode: public client, allow device-code flow, API permission `Mail.ReadWrite` (delegated). For application mode: a client secret, `Mail.ReadWrite` (application) with admin consent, and an **Exchange application access policy** so the app can reach only the mailboxes it should.
2. Configure:

```json
"Graph": { "Mode": "Delegated", "TenantId": "<tenant>", "ClientId": "<app id>" }
```

3. `POST /api/mail/sync`. In delegated mode the first call logs a device-code prompt; the token cache persists afterwards. Messages whose subject contains a visible matter number (`[10042-0003]`) are filed and tagged in Outlook.

## Layout

```
src/MatterDesk.Api
  Auth/            policy scheme, JWT bearer, dev header scheme, current-operator middleware
  Authorization/   MatterAccessPolicy — the predicate, once
  Activities/      IActivityRecorder — one row per governed action with its channel (web | cli | mcp)
  Controllers/     Matters, Documents, Search, Activity, MailSync, Mcp (JSON-RPC over Streamable HTTP: tools + prompts)
  Data/            DbContext (indexes, concurrency token bump), Seed (core matters) + Seed.Firm (the rest of the book, data-driven), schema patches
  Domain/          Operator, Client, Matter, MatterAccess, Document, DocumentVersion, ProfiledEmail, MailSyncState, ActivityEvent
  Mail/            IMailSource, GraphMailSource (delta + category marker), EmailProfilingService
  Mcp/             MatterTools (tool descriptors and handlers, operator-scoped), MatterPrompts (slash commands)
  Search/          SearchService — the UNION ALL search shared by REST and MCP
src/MatterDesk.Web react screen: matter list, documents/email tabs, search, operator switcher, live Activity panel, About page
src/MatterDesk.Cli  local executable: REST walkthrough (`demo`), ad-hoc calls, and a stdio MCP bridge to the hosted endpoint
tests/MatterDesk.Api.Tests  xUnit + WebApplicationFactory + SQLite in-memory + FakeMailSource
tests/e2e                   Playwright (Chrome) with webServer bootstrapping both apps
connectors/                 MCP connector files (Claude Desktop, Claude.ai, ChatGPT, Cursor, VS Code) and CLI install scripts
docs/                       BACKGROUND.md (platform model, integration experience, MCP, data), screenshots, cli-demo-output.txt
azure-pipelines.yml         build, API tests, web build, Playwright
```

## What I would do differently in a real codebase

- Migrations (`dotnet ef migrations`) in the release pipeline instead of `EnsureCreated` at startup.
- `rowversion` on SQL Server for the concurrency token; full-text index and `CONTAINS` for document/email text; a persisted computed column for the matter-number token in subjects.
- Graph **change notifications** (webhooks with lifecycle renewal) to trigger sync instead of a manual `POST`; a background worker with per-tenant throttling and `Retry-After` handling.
- Audit rows for every denied access and every profile change.
- A policy-based authorization handler (`IAuthorizationRequirement`) wrapping `MatterAccessPolicy` so controllers declare `[Authorize(Policy = "CanViewMatter")]` instead of calling the probe.
- The official `ModelContextProtocol` SDK with SSE streaming, resources (`matter://10042-0003`) and OAuth resource-indicator metadata, once the tool set is larger than three; the hand-rolled endpoint here keeps the authorization path visible in one file.

## How this was built

Spec first (the table above), then code, with an AI coding assistant drafting against that spec in small diffs.
Every generated change was read and run; the two bugs it introduced (a `UNION` that EF could not translate after
a record-constructor projection, and a `[Produces]` attribute that silently overrode `application/problem+json`)
were caught by the tests, not by trust.

## Docs

| Doc | What it is |
|---|---|
| [docs/BACKGROUND.md](docs/BACKGROUND.md) | Platform model (Azure, Microsoft 365, Entra, Graph, EWS), ENNU integration work, MCP, database experience, the posting line by line, BTCEdge |
| [docs/cli-demo-output.txt](docs/cli-demo-output.txt) | Captured `matterdesk demo` run (20 status lines) |
| [docs/connector-validation.md](docs/connector-validation.md) | Recorded connector validation: raw MCP, Anthropic connector, stdio bridge |
| [connectors/README.md](connectors/README.md) | How to attach Claude, ChatGPT, Cursor or VS Code, and the slash commands |
| [docs/PUBLISH.md](docs/PUBLISH.md) | How this repository and the v0.1.0 release were published |

## Licence

MIT — see [LICENSE](LICENSE).
