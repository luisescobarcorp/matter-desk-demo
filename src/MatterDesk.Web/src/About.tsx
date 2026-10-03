const platform = [
  ['Microsoft 365', 'Exchange Online (mail, calendar, contacts), SharePoint / OneDrive, Teams.', 'Where a firm’s email lives; what PLSync and email profiling read and write.'],
  ['Entra ID', 'Identity: app registrations, OAuth 2.0 / OIDC, delegated vs application permissions, admin consent.', 'Issues every token a Graph call carries. Prior use: ENNU’s HQ Dashboard signs staff in with Microsoft accounts (MSAL) and validates the Entra token on every API call.'],
  ['Microsoft Graph', 'One REST API over Microsoft 365 data: delta queries, change notifications, $batch, throttling.', 'The replacement for EWS: mail, calendar and contact sync, Outlook markers.'],
  ['Azure', 'Cloud infrastructure: App Service, Azure SQL, Key Vault, Storage, Monitor.', 'Where AIM365 is hosted — independent of M365 and Graph.'],
  ['EWS', 'The SOAP-era Exchange API. Phased disablement from 1 Oct 2026; removed 1 Apr 2027.', 'What the current integrations were built against, and why the Graph work has a deadline.'],
]

const estate = [
  ['HQ Dashboard', 'React / TypeScript + Cloudflare Worker API joining OpenDental, MindBody, HubSpot, Meta & Google Ads, GA4, Stripe, Paubox, Teams; cron sweeps; Vitest suites; deploy-then-verify CI.', 'Microsoft sign-in with Entra token validation; a production MCP server with 37 read-only tools behind OAuth / service tokens; “not measured” never renders as zero.'],
  ['ennueco (WordPress monorepo)', '~45,000 PHP files, 113 custom plugins: assessments, biomarker scoring, LabCorp PDF parsing, onboarding, e-signature, HubSpot (42+ field mappings), memberships, payments; lint / phpcs / PHPUnit gates.', 'Working inside a large existing production codebase: backup-first deploys, version verification, cache purge, hot-patch recovery.'],
  ['start.ennulife.com', 'React SPA lead funnel: nine quizzes, eleven persona pages, drip sequences; HubSpot, Firestore, GTM server-side tagging; 46 unit tests.', 'React across the API boundary at production volume.'],
  ['checkin', 'Passwordless weekly check-in writing to the patient’s OpenDental chart; n8n logic; Paubox; Teams; admin behind Microsoft sign-in.', 'AES-256-GCM tokens, one submission per day, outlier confirmation, no PHI in the repo.'],
  ['portal / onboarding', 'Next-gen patient portal and seven-step onboarding app; screens without a real endpoint show an error, never fake data.', 'The security audit proposed one PatientGuard authorization primitive — MatterAccessPolicy.VisibleTo() is that idea in C#.'],
  ['OpenDental + n8n', 'Practice-management database read by SQL and written through its API; 60+ workflows under HIPAA constraints.', 'A vendor schema other systems query directly — the same shape as PerfectLaw’s single SQL Server database.'],
]

const btcedge = [
  ['REST conventions: routing, status codes, validation, errors, authorization', '254 routes under /api on Node + Express; bearer gate with constant-time compare; an SSE hub that replaced ~40 polling hooks; WebSocket clients to Kalshi, Coinbase and five further exchange tapes.', 'The posting’s conventions at a scale where inconsistency surfaces within hours; pinned server/client wire contracts.'],
  ['Concurrency, data integrity, transactional consistency', 'Idempotent client_order_id derived from bet identity so a retry cannot double-buy; in-flight guard against overlapping placements; guardrails (per-order and daily limits, price, open-order cap, balance floor, loss breaker) in one server-side function shared by preview and place; write-then-rename atomic store writes after a torn write destroyed ~15 days of history; corrupt stores quarantined, never reset; authority-granting stores fail closed.', 'Idempotency, overlap guards and “check and action share one code path” are what a document profile, an Outlook marker and a billing batch need.'],
  ['Large datasets; storage and performance', 'Trade ledger began as an 80 MB JSON file rewritten every 1–5 seconds (~68 GB/hour of disk writes, 75% of a core); migrated to row-level SQLite writes, with S3 versioned backups.', 'Measure the storage cost before redesigning; row-level writes over whole-document rewrites, as any large table rewards.'],
  ['Authentication and authorization; OAuth / OIDC', 'Bearer gate on the API; on the MCP server an optional OAuth 2.1 authorization server with PKCE and dynamic client registration; role leases (observer / risk-officer / trader / admin) — a tool a role may not use is not even advertised; append-only audit of every mutating call; lockout-proof failed-auth throttle.', 'The OAuth server side, not only the client; authorization enforced by the server, never the model; audit as a first-class record.'],
  ['Automated testing that actually tests the behaviour', 'Vitest suites across hundreds of files (~2,600 server tests at one point, ~6,900 later); fixtures from real settled windows; mutation passes that break one clause and re-run the suite; integration tests on a real port reading SSE frames; wire-contract pins; source scans proving a module never imports an order-placement path.', 'Mutation passes check that a test tests the behaviour instead of assuming it — the posting’s concern about AI-generated tests.'],
  ['CI/CD, Git, operations', 'GitHub Actions gate (full server suite, both tsc configs, client build) before any push; systemd services on AWS Graviton reached only via SSM; timer-driven autodeploy; a parity script hashing every tracked file against the pushed commit; an off-box Lambda watchdog with least-privilege IAM; a CI check that fails when a reference doc drifts from its module.', 'Azure DevOps asks the same questions: what gates a push, how do you prove what runs is what was reviewed, who watches the watcher.'],
  ['MCP: governed access for AI agents', 'A dedicated mcp/ package: stdio and Streamable HTTP transports, role leases, write audit, resources with subscriptions, docs as resources, advisory sampling, slim views that name omitted keys, size-honest truncation, a report registry. Read-only in v1; later writes use the same endpoints and guardrails as the UI — no second brain.', 'Governed, permission-aware agent access to a system of record is where the DMS market is heading; I have shipped the role model and OAuth server end to end.'],
  ['AI-assisted development with human-owned review', 'Built spec-first with Claude Code and Cursor: twelve agent worktrees, a written contract at the top of 847 of 1,345 source files. Models in the product arrive display-and-grade-first: a nightly Claude review proposes but never applies changes; TimesFM forecasts are graded until their record earns influence; in-loop agents are zero-authority behind a fail-closed control file; a push-to-phone approval is the only way a model-proposed order is placed.', '“AI-generated code is not automatically trusted” is already the house rule: specs in, small diffs out, tests before trust, a human owning anything that touches data or money.'],
]

const coverage = [
  ['REST conventions: routing, methods, status codes, validation, errors, authorization, filtering, sorting', 'Controllers; 200/201/400/401/403/404/409/428; problem+json everywhere; filters and paging; Swagger', 'Demonstrated'],
  ['SQL Server: indexes, execution plans, concurrency, transactions, large DMS data', 'Explicit indexes; concurrency token → rowversion; one SaveChanges per unit of work; server-side UNION ALL. SSMS plans and T-SQL idioms: ramping from MySQL; BTCEdge: idempotent ids, atomic writes, quarantine, 80 MB JSON ledger migrated to SQLite row writes', 'Demonstrated / ramping'],
  ['Stored procedures when appropriate', 'Keep procedures where a rule is shared with desktop AIM; EF Core for new surfaces', 'Background'],
  ['React across the API boundary', 'This screen: list, detail tabs, search, operator switcher; typed client; loading / error / 403 states', 'Demonstrated'],
  ['Profiles, metadata, search, folders, permissions, versions, relationships, full-text', 'Document profiles with versions; matter-hub relationships; predicate in every query; cross-type search. Folders not modelled', 'Demonstrated (folders: not yet)'],
  ['Microsoft 365 / Graph / Exchange–Outlook', 'GraphMailSource: Entra auth, Inbox delta queries, Outlook category marker, idempotent filing', 'Demonstrated'],
  ['Authentication and authorization; OAuth / OIDC', 'JWT Bearer against any OIDC issuer; policy scheme; claim → operator mapping. In production: MSAL sign-in with Entra ID token validation, role gates, scoped service tokens', 'Demonstrated'],
  ['Azure DevOps / CI-CD / Git', 'azure-pipelines.yml; Git history; the same container runs on Azure App Service or Container Apps', 'Demonstrated'],
  ['Automated testing incl. Playwright', '23 xUnit tests through the real pipeline; 6 Playwright tests in Chrome. React component tests not yet added; BTCEdge: thousands of Vitest tests, mutation passes, CI gate', 'Demonstrated (component tests: not yet)'],
  ['AI-assisted development with human-owned review', 'Spec first, small diffs, every change read and run; two agent-introduced bugs caught by tests; MCP server at /mcp; BTCEdge built spec-first in Claude Code / Cursor (12 agent worktrees, written contracts at the top of 847 files)', 'Demonstrated'],
  ['Existing production codebase, not only greenfield', 'A 113-plugin WordPress monorepo and a 27-repository estate maintained in production; backup-first deploys, hot-patch recovery, audit → single authorization primitive; a 442K-line TypeScript system operated in production with autodeploy and parity checks', 'Demonstrated (different stack)'],
  ['5+ years, strong C# / ASP.NET Core', 'Working C# / ASP.NET Core 8 in this repository; 20 years across the rest of the stack; the project exists so the C# can be read rather than asserted', 'Demonstrated'],
]

const patterns = [
  ['OAuth clients & token lifecycle', 'Client-credential and refresh flows against vendor APIs; secrets out of workflows.', 'Entra app registration; Azure.Identity; delegated vs application permissions.'],
  ['Webhook receipt', 'Inbound events validated, de-duplicated and queued before side effects.', 'Graph change notifications: validation token, clientState, lifecycle renewal.'],
  ['Incremental fetch', '“Changed since” polling with a persisted watermark.', 'Delta queries; delta link stored per mailbox.'],
  ['Idempotency', 'External ids as the dedupe key; safe to re-run after partial failure.', '(MatterId, ExternalMessageId) unique; profile durable before the marker.'],
  ['Throttling & retries', 'Vendor 429s with backoff and dead-letter queues.', 'Graph 429 / Retry-After; per-tenant throttling.'],
  ['Document ingestion', 'LabCorp PDF results parsed, validated, attached to the right record.', 'Attachments and metadata profiled onto the matter.'],
  ['Data boundary', 'PHI never left approved systems (HIPAA).', 'Client and privileged data stay inside the firm’s boundary; approved AI tooling only.'],
  ['Access control & audit', 'RBAC, AES-256-GCM at rest, audit trails, consent tracking.', 'Matter security as a query predicate; 401 / 403 / 404 reasoning; audit rows.'],
  ['Observability', 'Run logs, failure queues, alerting.', 'Sync state table, per-operator cursor, audit rows.'],
]

function Grid({ head, rows }: { head: string[]; rows: string[][] }) {
  return (
    <table className="grid about-grid">
      <thead><tr>{head.map((h) => <th key={h}>{h}</th>)}</tr></thead>
      <tbody>{rows.map((r) => <tr key={r[0]}>{r.map((c, i) => <td key={i} className={i === 0 ? 'about-key' : ''}>{c}</td>)}</tr>)}</tbody>
    </table>
  )
}

export default function About() {
  return (
    <article className="about" data-testid="about">
      <h1>About this project</h1>
      <p>
        MatterDesk is a small, complete slice of a matter-centric document and email profiling system, built by
        Luis Escobar for PerfectLaw in October 2026. The matter is the hub, documents and email are profiled onto it,
        and permissions live in the query. Stack: ASP.NET Core 8, EF Core 8 (SQL Server / SQLite), Microsoft Graph SDK,
        OIDC bearer auth, React + Vite + TypeScript, xUnit integration tests, Playwright end-to-end, Azure Pipelines.
      </p>
      <p className="about-try">
        Try it: switch the operator in the header. <b>LES</b> and <b>PAR</b> cannot see the restricted matter
        <span className="mono"> 10099-0001</span>; <b>JDU</b> can. Search for <i>Falcon</i> as each one. The data is a seeded
        small-firm book (18 matters, 77 documents, no real client data): LES sees the 11 general matters, PAR 17, JDU all 18.
      </p>

      <h2>How I think about the Microsoft platform</h2>
      <Grid head={['Layer', 'What it is', 'Where it shows up in PerfectLaw’s work']} rows={platform} />
      <p>
        In one sentence: Azure hosts the application, Microsoft 365 holds the firm’s mail and documents, Entra decides who may
        touch them, and Graph is the API through which it all happens. <span className="mono">GraphMailSource</span> in this
        project authenticates through Entra, reads the Inbox through Graph delta queries and writes the “Filed:” category back
        through Graph; the API itself can be hosted anywhere.
      </p>

      <h2>The production estate behind these statements (ENNU, 2025–2026)</h2>
      <p>
        Twenty-seven repositories across two GitHub organizations run ENNU’s digital side. The six below are the live core and
        the ones relevant to this role; facts are from the repositories themselves as of 2 October 2026.
      </p>
      <Grid head={['System', 'What it is', 'Why it is relevant here']} rows={estate} />

      <h2>A personal project: BTCEdge (2026)</h2>
      <p>
        BTCEdge is a personal project I have built and run since mid-2026: one Node/TypeScript process trading Kalshi 15-minute
        bitcoin up/down contracts on my own account, plus a React dashboard. It is a hobby and a learning exercise, not a
        business, and I make no claim about its results. It is the project where the posting’s hardest asks — concurrency, data
        integrity, authentication and authorization, automated testing, MCP, and AI-assisted development with human-owned review —
        had real money behind them, so the engineering had to be right rather than merely tidy. The repository is private (about
        442,000 lines of TypeScript across server, client, shared types and an MCP package) and can be walked through on screen.
      </p>
      <Grid head={['What the posting asks for', 'What BTCEdge does', 'Why it transfers']} rows={btcedge} />
      <p>
        Two rules it taught that carry straight into a legal DMS: a number that was not measured is never shown as zero (it
        renders as a dash, and stale data is drawn dimmer with its age), and no component — model, agent or module — gets
        authority over data or money until its own graded record has earned it.
      </p>

      <h2>Integration and automation experience, and what transfers</h2>
      <p>
        At ENNU Life (CTO &amp; AI Architect, 2025–2026) I built and ran the automation layer that connected a multi-state
        telehealth business to outside systems: an n8n workflow platform plus a self-hosted server, receiving webhooks, polling
        partner APIs, ingesting LabCorp PDF results into structured data (PDF parsing and OCR), keeping a 69K-contact CRM instance
        with 300+ synced fields consistent with operational systems, pushing results onward through REST APIs, and using hosted
        and local language models for classification and extraction — under HIPAA controls (AES-256-GCM, RBAC, audit logging,
        consent tracking). Alongside it the platform was modernized to a modular, API-first, event-driven architecture (90+
        components) without interrupting operations. The platform was different; the engineering problems were the same ones a
        Graph sync layer has.
      </p>
      <Grid head={['Pattern', 'What I did at ENNU', 'Graph-era equivalent']} rows={patterns} />

      <h2>Model Context Protocol (MCP)</h2>
      <p>
        MCP is the open protocol through which an AI client calls tools and reads resources exposed by a server over JSON-RPC.
        I have built and operated MCP servers in production: HQ Dashboard’s <span className="mono">POST /mcp</span> exposes 37
        read-only tools over the company’s operating data behind OAuth or scoped service tokens; a WordPress MCP server and a
        HubSpot MCP server (51 tools) sit alongside the monorepo; and I use MCP clients (Claude Code, Cursor) against them daily.
        The practical lessons: tool descriptions are an interface contract, arguments need tight schemas, read-only by default,
        and the server, not the model, must enforce authorization.
      </p>
      <p>
        This API includes a working MCP server at <span className="mono">POST /mcp</span> with three tools:
        <span className="mono"> search_matters</span>, <span className="mono">get_matter</span> and
        <span className="mono"> list_documents</span>. Every call runs under the authenticated operator, so the same permission
        predicate that protects the REST API and this screen decides what an agent can see. An agent acting for LES gets zero
        hits for the restricted matter and an error that does not reveal its title; the same tool as JDU returns it.
      </p>

      <h2>Database and data</h2>
      <p>
        My production database depth is on MySQL 8.0 (behind CRM-connected and multi-tenant SaaS systems) rather than SQL
        Server. What transfers: schema change on live systems
        (additive migrations, batched backfills, zero-downtime cut-overs); indexing from slow-query logs and execution plans;
        transactions, isolation and optimistic concurrency; ingestion and ETL through staging tables; tested backups and
        per-application access; reading another vendor’s practice-management schema (OpenDental) directly by SQL for dashboards
        and automations without breaking the product that owns it. What this project shows on SQL Server / EF Core: the permission predicate as an
        <span className="mono"> EXISTS</span> probe with the index to serve it, explicit indexes, a concurrency token that maps to
        <span className="mono"> rowversion</span>, a server-side <span className="mono">UNION ALL</span> search, and transaction
        boundaries where they belong. What I am actively ramping on: T-SQL idioms, execution plans in SSMS, snapshot isolation,
        full-text <span className="mono">CONTAINS</span>, stored procedures shared with a desktop client, SQL Agent. I would rather
        be exact about this than overstate it.
      </p>

      <h2>The posting, requirement by requirement</h2>
      <p>
        The listing (“Senior Full-Stack .NET / REST API Developer for Product Integration with Microsoft Office 365”) against where
        each line is demonstrated: in this project, in prior work, or honestly marked as ramping.
      </p>
      <Grid head={['Posting requirement', 'Where it is demonstrated', 'Status']} rows={coverage} />

      <h2>How I would work from day one</h2>
      <ul>
        <li>Small, specific tasks taken as seriously as large ones; a bug fix with a test is a good first week.</li>
        <li>Everything inside company policy: approved AI tooling only, no source or client data outside the boundary the company sets.</li>
        <li>AI-assisted delivery with human ownership: explicit spec, small diffs, every generated change read and run; correctness, security, performance, maintainability, architectural consistency, SQL integrity and regression risk gated by a person.</li>
        <li>Tests at every layer that matters to a DMS: API, SQL, component and end-to-end, permission cases first.</li>
      </ul>
      <p className="hint">Source, tests and the full write-up are in the repository README and <span className="mono">docs/BACKGROUND.md</span>.</p>
    </article>
  )
}
