const platform = [
  ['Microsoft 365', 'Exchange Online (mail, calendar, contacts), SharePoint / OneDrive, Teams.', 'Where a firm’s email lives; what PLSync and email profiling read and write.'],
  ['Entra ID', 'Identity: app registrations, OAuth 2.0 / OIDC, delegated vs application permissions, admin consent.', 'Issues every token a Graph call carries.'],
  ['Microsoft Graph', 'One REST API over Microsoft 365 data: delta queries, change notifications, $batch, throttling.', 'The replacement for EWS: mail, calendar and contact sync, Outlook markers.'],
  ['Azure', 'Cloud infrastructure: App Service, Azure SQL, Key Vault, Storage, Monitor.', 'Where AIM365 is hosted — independent of M365 and Graph.'],
  ['EWS', 'The SOAP-era Exchange API. Phased disablement from 1 Oct 2026; removed 1 Apr 2027.', 'What the current integrations were built against, and why the Graph work has a deadline.'],
]

const patterns = [
  ['OAuth clients & token lifecycle', 'Client-credential and refresh flows against vendor APIs; secrets out of workflows.', 'Entra app registration; Azure.Identity; delegated vs application permissions.'],
  ['Webhook receipt', 'Inbound events validated, de-duplicated and queued before side effects.', 'Graph change notifications: validation token, clientState, lifecycle renewal.'],
  ['Incremental fetch', '“Changed since” polling with a persisted watermark.', 'Delta queries; delta link stored per mailbox.'],
  ['Idempotency', 'External ids as the dedupe key; safe to re-run after partial failure.', '(MatterId, ExternalMessageId) unique; profile durable before the marker.'],
  ['Throttling & retries', 'Vendor 429s with backoff and dead-letter queues.', 'Graph 429 / Retry-After; per-tenant throttling.'],
  ['Document ingestion', 'LabCorp PDF results parsed, validated, attached to the right record.', 'Attachments and metadata profiled onto the matter.'],
  ['Data boundary', 'PHI never left approved systems (HIPAA).', 'Client and privileged data stay inside the firm’s boundary; approved AI tooling only.'],
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
        <span className="mono"> 10099-0001</span>; <b>JDU</b> can. Search for <i>Falcon</i> as each one.
      </p>

      <h2>How I think about the Microsoft platform</h2>
      <Grid head={['Layer', 'What it is', 'Where it shows up in PerfectLaw’s work']} rows={platform} />
      <p>
        In one sentence: Azure hosts the application, Microsoft 365 holds the firm’s mail and documents, Entra decides who may
        touch them, and Graph is the API through which it all happens. <span className="mono">GraphMailSource</span> in this
        project authenticates through Entra, reads the Inbox through Graph delta queries and writes the “Filed:” category back
        through Graph; the API itself can be hosted anywhere.
      </p>

      <h2>Integration and automation experience, and what transfers</h2>
      <p>
        At ENNU I built and ran the automation layer that connected the business to outside systems: an n8n workflow platform
        plus a local server, receiving webhooks, polling partner APIs, ingesting LabCorp PDF results into structured data,
        pushing results onward through REST APIs, and using hosted and local language models for classification and extraction,
        inside HIPAA constraints. The platform was different; the engineering problems were the same ones a Graph sync layer has.
      </p>
      <Grid head={['Pattern', 'What I did at ENNU', 'Graph-era equivalent']} rows={patterns} />

      <h2>Model Context Protocol (MCP)</h2>
      <p>
        MCP is the open protocol through which an AI client calls tools and reads resources exposed by a server over JSON-RPC.
        I have built and operated MCP servers that expose internal data and actions to agents, configured the clients that use
        them, and learned the practical lessons: tool descriptions are an interface contract, arguments need tight schemas, and
        the server, not the model, must enforce authorization.
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
        My production database depth is on MySQL rather than SQL Server. What transfers: schema change on live systems
        (additive migrations, batched backfills, zero-downtime cut-overs); indexing from slow-query logs and execution plans;
        transactions, isolation and optimistic concurrency; ingestion and ETL through staging tables; tested backups and
        per-application access. What this project shows on SQL Server / EF Core: the permission predicate as an
        <span className="mono"> EXISTS</span> probe with the index to serve it, explicit indexes, a concurrency token that maps to
        <span className="mono"> rowversion</span>, a server-side <span className="mono">UNION ALL</span> search, and transaction
        boundaries where they belong. What I am actively ramping on: T-SQL idioms, execution plans in SSMS, snapshot isolation,
        full-text <span className="mono">CONTAINS</span>, stored procedures shared with a desktop client, SQL Agent. I would rather
        be exact about this than overstate it.
      </p>

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
