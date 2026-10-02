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
| **Entra ID** (formerly Azure AD) | The identity plane. App registrations, OAuth 2.0 / OpenID Connect, **delegated** vs **application** permissions, admin consent, conditional access. | Issues every token a Graph call carries. A sync service running without a user present needs application permissions and an Exchange application access policy scoped to the right mailboxes. |
| **Microsoft Graph** | One REST API (`graph.microsoft.com`) over Microsoft 365 data. Delta queries, change notifications (webhooks), `$batch`, throttling with `Retry-After`. | The replacement for EWS. Mail, calendar and contact sync, Outlook markers, drafting and sending from the matter. |
| **Azure** | The cloud infrastructure: VMs, App Service, Azure SQL, Key Vault, Storage, Monitor. | Where AIM365 is hosted. A hosting decision that is independent of Microsoft 365 and Graph — an on-prem firm still has its mail in Exchange Online. |
| **EWS** | Exchange Web Services, the SOAP-era API to Exchange. Being disabled in phases from 1 October 2026 and removed from Exchange Online on 1 April 2027. | What the existing integration surfaces were built against, and why the Graph work has a deadline. |

In one sentence: **Azure hosts the application, Microsoft 365 holds the firm's mail and documents, Entra
decides who may touch them, and Graph is the API through which it all happens.** MatterDesk uses this
model directly: `GraphMailSource` authenticates through Entra (device code or client credentials), reads
the Inbox through Graph delta queries, and writes the "Filed:" category back through Graph; the API itself
can be hosted anywhere.

## 2. Integration and automation experience, and what transfers

At ENNU I built and ran the automation layer that connected the business to outside systems: an n8n
workflow platform plus a local server, receiving webhooks from external services, polling partner APIs,
ingesting LabCorp PDF results into structured data, pushing results onward through REST APIs, and using
hosted and local language models for classification and extraction — all inside HIPAA constraints, where
the boundary of what data may go where is not negotiable.

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
| Observability | Run logs, failure queues and alerting so a silent failure became a visible one. | Sync state table, per-operator cursor, audit rows for denied access and profile changes. |

## 3. Model Context Protocol (MCP)

MCP is the open protocol through which an AI client (Claude, Copilot, Cursor, a custom agent) calls
tools and reads resources exposed by a server, over JSON-RPC. I have built and operated MCP servers that
expose internal data and actions to agents, configured the clients that consume them, and learned the
practical lessons: tool descriptions are an interface contract, arguments need tight schemas, and the
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

**What I have done in production (MySQL, live systems):**

- Schema design and change on running systems: additive migrations, backfills in batches, cut-overs
  without downtime.
- Indexing from evidence: slow-query logs and `EXPLAIN` plans rather than guesswork; composite indexes
  matched to the predicates that actually run.
- Transactions and isolation: multi-row writes that commit together, optimistic concurrency where two
  writers can collide, idempotent upserts for ingestion.
- Ingestion and ETL: API and file feeds landed into staging, validated, and merged; reporting queries
  that stay fast as volume grows.
- Operations: backups and restores tested, not assumed; access scoped per application user.

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

## 5. How I would work from day one

- Small, specific tasks taken as seriously as large ones; a bug fix with a test is a good first week.
- Everything inside company policy: approved AI tooling only, no source or client data outside the
  boundary the company sets.
- AI-assisted delivery with human ownership: an explicit spec first, small diffs, every generated change
  read and run; correctness, security, performance, maintainability, architectural consistency, SQL
  integrity and regression risk gated by a person, not by trust.
- Tests at every layer that matters to a DMS: API, SQL, component and end-to-end — permission cases first.
