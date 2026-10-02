# Background: how my experience maps to this role

Prepared by Luis Escobar for PerfectLaw, October 2026. This is the context behind MatterDesk — what I
have actually worked with, how I think about the Microsoft platform, and where I am still ramping. The
project in this repository is the code that backs these statements; the tests are the evidence.

## 1. How I think about the Microsoft platform

These names get used interchangeably in conversation, but they are different layers, and the distinction
matters for the Office 365 integration work.

| Layer | What it is | Where it shows up in PerfectLaw's work |
|---|---|---|
| **Microsoft 365** | The tenant's productivity workloads: **Exchange Online** (mail, calendar, contacts, tasks), SharePoint / OneDrive (files), Teams. | Where a firm's email actually lives. PLSync, the Exchange Profiler and Inbox / Sent profiling all read and write this data. |
| **Entra ID** (formerly Azure AD) | The identity plane. App registrations, OAuth 2.0 / OpenID Connect, **delegated** vs **application** permissions, admin consent, conditional access. | Issues every token a Graph call carries. A sync service running without a user present needs application permissions and an Exchange application access policy scoped to the right mailboxes. *Prior use:* ENNU's HQ Dashboard signs staff in with their Microsoft 365 accounts (MSAL) and its API validates the Entra ID token on every request, with an email allow-list, admin/member roles and scoped service tokens for machine access; Microsoft Teams receives alerts from every repository. |
| **Microsoft Graph** | One REST API (`graph.microsoft.com`) over Microsoft 365 data. Delta queries, change notifications (webhooks), `$batch`, throttling with `Retry-After`. | The replacement for EWS. Mail, calendar and contact sync, Outlook markers, drafting and sending from the matter. |
| **Azure** | The cloud infrastructure: VMs, App Service, Azure SQL, Key Vault, Storage, Monitor. | Where AIM365 is hosted. A hosting decision that is independent of Microsoft 365 and Graph — an on-prem firm still has its mail in Exchange Online. |
| **EWS** | Exchange Web Services, the SOAP-era API to Exchange. Being disabled in phases from 1 October 2026 and removed from Exchange Online on 1 April 2027. | What the existing integration surfaces were built against, and why the Graph work has a deadline. |

In one sentence: **Azure hosts the application, Microsoft 365 holds the firm's mail and documents, Entra
decides who may touch them, and Graph is the API through which it all happens.** MatterDesk uses this
model directly: `GraphMailSource` authenticates through Entra (device code or client credentials), reads
the Inbox through Graph delta queries, and writes the "Filed:" category back through Graph; the API itself
can be hosted anywhere.

## 1a. The production estate behind these statements (ENNU, 2025–2026)

Twenty-seven repositories across two GitHub organizations run ENNU's digital side; the seven below are the live
core and the ones relevant to this role. Facts are from the repositories themselves as of 2 October 2026.

| System | What it is | Why it is relevant here |
|---|---|---|
| **HQ Dashboard** (hq.ennulife.com) | React 19 / TypeScript front end on Cloudflare Pages; a Cloudflare Worker API joining OpenDental, MindBody, HubSpot, Meta Marketing API, Google Ads, GA4, Search Console, Google Business Profile, Stripe, Authorize.net, WooCommerce, Paubox and Microsoft Teams into one picture; cron sweeps every minute / 5 min / hourly / daily; 76 n8n workflow mirrors; Vitest suites over billing, attribution, email and lead scoping; CI that deploys on push and then verifies the live page serves the new bundle hash. | Microsoft sign-in (MSAL) with Entra ID token validation in the API; a production **MCP server exposing 37 read-only tools** behind OAuth or scoped service tokens, used by Claude and other agents; the "honest data" rule — a value that was not measured renders as a dash, never as zero. |
| **ennueco** (WordPress monorepo) | ~45,000 PHP files, 113 custom plugins behind ennulife.com, ennu.co and ennupeptides.com: assessments, biomarker scoring, LabCorp PDF parsing, member onboarding, e-signature, HubSpot integration (42+ field mappings), memberships, affiliates, booking, payments; 13 GitHub Actions workflows (lint, phpcs, PHPUnit, quality gates). | **Working inside a large existing production codebase.** The deploy workflow backs up the live plugin before writing, verifies the version after, and purges cache; the must-use plugin sync compares before overwriting so a host-side hot patch is never lost. That workflow exists because production once ran code that existed nowhere in git — the same lesson a shared, decades-old schema teaches. |
| **start.ennulife.com** (lead funnel) | React 19 / Vite SPA: nine quizzes, eleven persona landing pages, drip sequences; HubSpot forms, Firestore, Paubox, GTM server-side tagging, GA4 cross-domain; 46 unit tests. | React across the API boundary at production volume; a GitHub Actions cron that pings n8n because the platform's own scheduler did not register — knowing where automation silently stops. |
| **checkin** (checkin.ennulife.com) | Passwordless weekly weight check-in for weight-loss members; logic in n8n; writes to the patient's OpenDental chart, mirrors to HubSpot, Paubox email with delivery receipts, Teams alerts; staff admin behind Microsoft sign-in. | Safety design for a one-click link: AES-256-GCM tokens, one check-in per day, outlier confirmation, never echo the prior value, no PHI in the repository. |
| **portal** / **onboarding** | Next-generation patient portal and a seven-step onboarding app (React, TypeScript, TanStack Query; Cloudflare Worker proxying to WordPress); every screen that lacks a real endpoint shows an error state rather than fake data. | The portal's security audit of the WordPress estate proposed **one `PatientGuard` authorization primitive** instead of per-route checks. `MatterAccessPolicy.VisibleTo()` in this project is that idea in C#. |
| **OpenDental integration** | The medical division's practice-management system, read by SQL (via n8n webhooks) and written through its API for check-ins and automations. | A practice-management database with its own schema that other systems read directly — the same shape as PerfectLaw's single SQL Server database that firms query themselves. |
| **n8n Cloud** (n8n.ennulife.com) | 60+ workflows moving data between the systems above; HIPAA constraints throughout. | Webhooks, incremental fetch, idempotency, retries, failure queues — the table in section 2. |

## 1b. A personal project: BTCEdge (2026)

BTCEdge is a personal project I have built and run since mid-2026: one Node/TypeScript process trading Kalshi
15-minute bitcoin up/down contracts on my own account, plus a React dashboard. It is a hobby and a learning
exercise, not a business, and I make no claim about its results. It is the project where the posting's hardest
asks — concurrency, data integrity, authentication and authorization, automated testing, MCP, and AI-assisted
development with human-owned review — had real money behind them, so the engineering had to be right rather
than merely tidy. The repository is private (1,357 tracked paths, about 442,000 lines, TypeScript throughout:
server, client, shared types and an MCP package, plus Python ops scripts) and can be walked through on screen.

| What the posting asks for | What BTCEdge does | Why it transfers |
|---|---|---|
| REST API conventions: routing, methods, status codes, validation, error handling, authorization | 254 routes under `/api` on Node + Express; bearer-token gate with a constant-time compare on every request; a Server-Sent Events hub that replaced ~40 polling hooks for the eight topics that matter; WebSocket clients to Kalshi, Coinbase and five further exchange tapes. | The posting's conventions at a scale where inconsistency surfaces as a bug within hours; server and React client share pinned wire contracts — the React/API boundary the posting stresses. |
| Concurrency, data integrity, transactional consistency | Idempotent `client_order_id` derived from bet identity so a retry cannot double-buy; an in-flight guard against overlapping placements; guardrails (per-order and daily limits, max price, open-order cap, balance floor, loss breaker) enforced server-side in one function shared by preview and place, so preview cannot disagree with the gate; atomic write-then-rename store writes after a torn write destroyed ~15 days of history; corrupt stores quarantined, never silently reset; fail-closed stores for anything that grants authority. | Idempotency, overlap guards and "the check and the action share one code path" are what a document profile, an Outlook marker and a billing batch need to stay consistent. |
| Large datasets; storage and performance | The trade ledger began as an 80 MB JSON file rewritten every 1–5 seconds (~68 GB/hour of disk writes, 75% of a core); migrated to row-level SQLite writes, with JSON ledgers kept only where append-only is right and S3 versioned backups of both. | Measure the storage cost before redesigning; row-level writes instead of whole-document rewrites — the instinct a large DMS table rewards. |
| Authentication and authorization; OAuth / OIDC | Bearer gate on the API; on the MCP server an optional OAuth 2.1 authorization server with PKCE and dynamic client registration (for claude.ai custom connectors); role leases (observer / risk-officer / trader / admin) where a tool a role may not use is not even advertised; an append-only audit of every mutating call; a failed-auth throttle designed to be lockout-proof. | The OAuth server side, not only the client; authorization enforced by the server, never by the model; audit as a first-class record. Matter security and operator roles have the same shape. |
| Automated testing that actually tests the intended behaviour | Vitest suites across hundreds of files (~2,600 server tests at one point, ~6,900 later); fixtures built from real settled windows; mutation passes that break one clause and re-run the suite to find clauses no test protects; integration tests that boot Express on a real port and read SSE frames; wire-contract pins between server and client; source scans proving a module never imports an order-placement path; a test that fails if any store module forgets its test-mode in-memory guard. | Mutation passes are how I check that a test tests the behaviour instead of assuming it — the concern the posting raises about AI-generated tests. |
| CI/CD, Git, operations | GitHub Actions gate (full server suite, both `tsc` configs, client build) before any push; systemd services on an AWS Graviton instance reached only via SSM; timer-driven autodeploy; a parity script hashing every tracked file on the box against the pushed commit; an off-box Lambda watchdog under a least-privilege IAM policy; a CI check that fails when the executor's behavioural reference doc drifts from the module it describes, because it went stale twice. | Azure DevOps is a different product with the same questions: what gates a push, how do you prove what runs is what was reviewed, and who watches the watcher. |
| MCP: governed access for AI agents | A dedicated `mcp/` package: stdio and Streamable HTTP transports, role leases, write audit, resources with subscriptions on long-lived transports, docs served as resources, advisory sampling (the model votes, decides nothing), slim views that name omitted keys, size-honest truncation, a curated report registry. Read-only in v1; later writes go through the same endpoints and guardrails the UI uses — no second brain. | Governed, permission-aware agent access to a system of record is where the DMS market is heading; I have shipped the role model and authorization server end to end. |
| AI-assisted development with human-owned review; detailed specifications to agents | Built spec-first with Claude Code and Cursor: twelve agent worktrees, a `CLAUDE.md`, and a written contract at the top of 847 of 1,345 source files stating intent so later readers and agents do not reconstruct it from the implementation. Models in the product arrive display-and-grade-first: a nightly Claude review proposes but never applies changes; TimesFM forecasts are shown and graded until their record earns influence; in-loop agents are zero-authority behind a fail-closed control file; a push-to-phone approval with a one-time token is the only way a model-proposed order is placed. | "AI-generated code is not automatically trusted" is already the house rule: specs in, small diffs out, tests before trust, a human owning anything that touches data or money. |

Two rules BTCEdge taught that carry straight into a legal DMS: **a number that was not measured is never shown
as zero** (it renders as a dash, and stale data is drawn dimmer with its age), and **no component — model, agent
or module — gets authority over data or money until its own graded record has earned it**.

## 2. Integration and automation experience, and what transfers

At ENNU Life (CTO & AI Architect, 2025–2026) I built and ran the automation layer that connected a
multi-state telehealth business to outside systems: an n8n workflow platform plus a self-hosted server,
receiving webhooks from external services, polling partner APIs, ingesting LabCorp PDF results into
structured data (document intelligence: PDF parsing and OCR), keeping a 69K-contact CRM instance with
300+ synced fields consistent with the operational systems, pushing results onward through REST APIs, and
using hosted and local language models for classification and extraction — all under HIPAA controls
(AES-256-GCM encryption, RBAC, audit logging, consent tracking), where the boundary of what data may go
where is not negotiable. Alongside it the platform was modernized to a modular, API-first, event-driven
architecture (90+ components, typed event bus) without interrupting operations — the same brownfield
discipline a shared, decades-old schema demands.

The platform was different; the engineering problems were the same ones a Graph sync layer has.

| Pattern | What I did at ENNU | The Graph-era equivalent |
|---|---|---|
| OAuth clients and token lifecycle | Client-credential and refresh-token flows against vendor APIs; secrets kept out of workflows. | Entra app registration; MSAL / `Azure.Identity`; delegated vs application permissions; admin consent per tenant. |
| Webhook receipt | Inbound events validated, de-duplicated and queued before any side effect. | Graph change notifications: validation-token handshake, `clientState` check, subscription lifecycle renewal. |
| Incremental fetch | "Changed since" polling with a persisted watermark so nothing is pulled twice. | Delta queries with the delta link stored per mailbox (`MailSyncState.DeltaLink`). |
| Idempotency | External identifiers as the dedupe key; safe to re-run after any partial failure. | `(MatterId, ExternalMessageId)` unique; profile row durable before the Outlook marker is attempted. |
| Throttling and retries | Vendor 429s handled with backoff and dead-letter queues. | Graph 429 / `Retry-After`; per-tenant throttling in a background worker. |
| Document ingestion | PDF results parsed into fields, validated, attached to the right record. | Attachments and message metadata profiled onto the matter; keywords and document type extracted. |
| Data boundary | PHI never left approved systems; no external AI tool saw it without a BAA and an approved configuration. | Client data and privileged material stay inside the firm's boundary; AI tooling only in the configuration the company approves. |
| Access control and audit | RBAC, AES-256-GCM at rest, audit trails and consent tracking as standing controls. | Matter security as a query predicate; 401 / 403 / 404 reasoning; audit rows for denied access and profile changes. |
| Observability | Run logs, failure queues and alerting so a silent failure became a visible one. | Sync state table, per-operator cursor, audit rows for denied access and profile changes. |

## 3. Model Context Protocol (MCP)

MCP is the open protocol through which an AI client (Claude, Copilot, Cursor, a custom agent) calls
tools and reads resources exposed by a server, over JSON-RPC. I have built and operated MCP servers in
production: HQ Dashboard's `POST /mcp` exposes 37 read-only tools over the company's operating data behind
OAuth or scoped service tokens; a WordPress MCP server and a HubSpot MCP server (51 tools) sit alongside the
WordPress monorepo; and I use MCP clients (Claude Code, Cursor) against them daily. The practical lessons:
tool descriptions are an interface contract, arguments need tight schemas, read-only by default, and the
server — not the model — must enforce authorization.

MatterDesk includes a working MCP server at `POST /mcp` (Streamable HTTP, protocol `2025-06-18`) with
three tools: `search_matters`, `get_matter` and `list_documents`. Every call is authenticated like any
other API request and runs under the resolved operator, so the same `MatterAccessPolicy` predicate that
protects the REST API and the React screen also decides what an agent can see. The tests prove it: an
agent acting for operator LES gets zero hits for the restricted matter and an error that does not reveal
its title; the same tool as JDU returns it.

Why this matters for a system like PerfectLaw's: the single database already holds the matter, its
documents, its email and its billing. Exposing that through governed, permission-aware tools is how
Copilot and other agents get to use it without copying the data anywhere — and it is a differentiator
that stitched-together stacks cannot offer without integration work.

## 4. Database and data

My production database depth is on MySQL rather than SQL Server, so this section says what transfers,
what I have shown in this project, and what I am ramping on.

**What I have done in production (MySQL 8.0 behind CRM-connected and multi-tenant SaaS systems, live):**

- Schema design and change on running systems: additive migrations, backfills in batches, cut-overs
  without downtime.
- Indexing from evidence: slow-query logs and `EXPLAIN` plans rather than guesswork; composite indexes
  matched to the predicates that actually run.
- Transactions and isolation: multi-row writes that commit together, optimistic concurrency where two
  writers can collide, idempotent upserts for ingestion.
- Ingestion and ETL: API and file feeds landed into staging, validated, and merged; reporting queries
  that stay fast as volume grows.
- Operations: backups and restores tested, not assumed; access scoped per application user.
- Reading another vendor's practice-management schema directly: OpenDental (patients, appointments,
  payments) queried by SQL for dashboards and automations, and written through its API — the same
  discipline as querying a law firm's All-in-One database without breaking the product that owns it.
- Reporting that tells the truth: a value that was not measured renders as "not tracked yet", never as zero;
  several production fixes were exactly that distinction.

**What this project demonstrates on SQL Server / EF Core:**

- The permission predicate expressed once as an `IQueryable` extension and translated to an `EXISTS`
  probe against `MatterAccess`, with the `(OperatorId, MatterId)` index to serve it.
- Explicit indexes in `OnModelCreating`; a concurrency token that maps to `rowversion` on SQL Server;
  a `UNION ALL` search projected server-side so EF Core translates it instead of pulling rows to the client.
- Transaction boundaries where they belong (profile row and first version row in one `SaveChanges`).

**What I am actively ramping on for SQL Server specifically:** T-SQL idioms where they differ from MySQL,
reading execution plans in SSMS, `rowversion` and snapshot isolation, full-text indexes and `CONTAINS`,
stored procedures shared with a desktop client, SQL Agent jobs, and the EF Core SQL Server provider's
translation behaviour. I would rather be exact about this than overstate it.

## 5. The posting, requirement by requirement

The Indeed listing ("Senior Full-Stack .NET / REST API Developer for Product Integration with Microsoft
Office 365") against where each line is demonstrated — in this project, in prior work, or honestly
marked as ramping.

| Posting requirement | Where it is demonstrated | Status |
|---|---|---|
| REST API conventions: routing, methods, status codes, validation, error handling, authorization, filtering, sorting | Controllers: attribute routing; 200/201/400/401/403/404/409/428; DataAnnotations + RFC 7807 `problem+json` for every error; `[Authorize]` + operator middleware; filters on `areaOfLaw` / `clientNumber` / `documentType`; ordered, paged lists; Swagger/OpenAPI with XML comments | Demonstrated |
| SQL Server: query optimisation, indexes, execution plans, transactional consistency, optimistic concurrency, large DMS datasets | EF Core SQL Server provider; explicit indexes for the permission probe and hot paths; concurrency token → `rowversion`; profile + first version in one `SaveChanges`; mandatory pagination; `UNION ALL` search kept server-side. SSMS execution plans and T-SQL idioms: ramping from MySQL `EXPLAIN` experience; BTCEdge: idempotent ids, atomic writes, quarantine, 80 MB JSON ledger migrated to SQLite row writes | Demonstrated / ramping |
| Stored procedures when appropriate | Position stated in the README: keep procedures where a rule is shared with desktop AIM; EF Core for new surfaces | Background |
| React: screens, components, API wiring, state, async, error handling, UI/API diagnosis | `MatterDesk.Web`: matter list, detail tabs, search, operator switcher, About page; typed API client; loading / error / 403 states; request cancellation on re-render | Demonstrated |
| Users, profiles and metadata, searching, folder structures, permissions, versions, relationships, full-text | Operators; document profiles with versions; matter-hub relationships; permission predicate in every query; cross-type search (full-text `CONTAINS` on SQL Server noted); folder structures not modelled | Demonstrated (folders: not yet) |
| Microsoft 365 integrations / Microsoft Graph / Exchange–Outlook | `GraphMailSource`: Entra auth (device code or client credentials), Inbox delta queries with stored delta link, Outlook category marker, idempotent filing; change notifications listed as next step | Demonstrated |
| Authentication and authorization; OAuth / OIDC | JWT Bearer against any OIDC issuer (Entra), policy scheme, claim → operator mapping, 401 vs 403 vs 404 reasoning. In production: Microsoft sign-in via MSAL with Entra ID token validation, role gates and scoped service tokens (HQ Dashboard) | Demonstrated |
| Azure / Azure DevOps / CI-CD / Git | `azure-pipelines.yml` (build, API tests, web build, Playwright); Git history; the container image runs unchanged on Azure App Service or Container Apps (hosted on AWS App Runner for the demo) | Demonstrated |
| Automated testing: unit, API, integration, SQL, component, end-to-end, Playwright, regression | 23 xUnit tests through the real pipeline on SQLite in-memory (permissions, concurrency, mail sync, MCP); 6 Playwright tests in Chrome booting both apps; React component tests not yet added. In production: Vitest suites over billing, attribution and lead scoping; PHPUnit / phpcs / lint quality gates; post-deploy bundle-hash verification; BTCEdge: thousands of Vitest tests, mutation passes, CI gate | Demonstrated (component tests: not yet) |
| AI-assisted development with human-owned review; detailed specifications to agents | Built spec-first with an agent drafting in small diffs; two agent-introduced bugs caught by tests and documented in the README; daily use of Claude Code, Cursor and Codex-style agents; MCP servers built for agents; BTCEdge built spec-first in Claude Code / Cursor (12 agent worktrees, written contracts at the top of 847 files) | Demonstrated |
| Existing production codebase, not only greenfield | Production automation and integration systems maintained and extended at ENNU; approach to a shared schema described above; a 442K-line TypeScript system operated in production with autodeploy and parity checks | Background |
| 5+ years, strong C# / ASP.NET Core | Working C# / ASP.NET Core 8 in this repository; depth across the rest of the stack from prior roles; the project exists so the C# can be read rather than asserted | Demonstrated |

## 6. How I would work from day one

- Small, specific tasks taken as seriously as large ones; a bug fix with a test is a good first week.
- Everything inside company policy: approved AI tooling only, no source or client data outside the
  boundary the company sets.
- AI-assisted delivery with human ownership: an explicit spec first, small diffs, every generated change
  read and run; correctness, security, performance, maintainability, architectural consistency, SQL
  integrity and regression risk gated by a person, not by trust.
- Tests at every layer that matters to a DMS: API, SQL, component and end-to-end — permission cases first.
