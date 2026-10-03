import { useEffect, useState } from 'react'
import type { Document, Email, Matter, Operator, Paged, SearchHit } from './api'
import { ApiError, api, operators } from './api'
import { useApi } from './useApi'
import About from './About'
import { ActivityPanel, ToastProvider, useToast } from './Activity'

const fmt = (iso: string) => new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })

const StatusPill = ({ status }: { status: Matter['status'] }) => (
  <span className={`pill status ${status.toLowerCase().replace(' ', '-')}`} data-testid="matter-status">{status}</span>
)

type View = 'matters' | 'about'
const viewFromHash = (): View => (window.location.hash === '#about' ? 'about' : 'matters')

export default function App() {
  const [operator, setOperator] = useState<Operator>('LES')
  const [selected, setSelected] = useState<number | null>(null)
  const [query, setQuery] = useState('')
  const [view, setView] = useState<View>(() => viewFromHash())

  useEffect(() => {
    const onHash = () => setView(viewFromHash())
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  const go = (v: View) => { setView(v); window.location.hash = v === 'about' ? 'about' : '' }

  return (
    <ToastProvider>
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark">MD</span>
          <div>
            <div className="brand-title">MatterDesk</div>
            <div className="brand-sub">matters · documents · profiled email</div>
          </div>
        </div>
        <nav className="nav" aria-label="Views">
          <button data-testid="nav-matters" aria-current={view === 'matters'} onClick={() => go('matters')}>Matters</button>
          <button data-testid="nav-about" aria-current={view === 'about'} onClick={() => go('about')}>About this project</button>
        </nav>
        <SearchBox value={query} onChange={(v) => { setQuery(v); if (view !== 'matters') go('matters') }} />
        <label className="operator">
          Signed in as
          <select data-testid="operator" value={operator} onChange={(e) => { setOperator(e.target.value as Operator); setSelected(null) }}>
            {operators.map((o) => <option key={o.code} value={o.code}>{o.label}</option>)}
          </select>
        </label>
      </header>

      {view === 'about' ? (
        <main className="layout single">
          <section className="pane"><About /></section>
          <ActivityPanel operator={operator} />
        </main>
      ) : (
        <main className="layout">
          <section className="pane matters">
            <h2>Matters</h2>
            <MatterList operator={operator} selected={selected} onSelect={(id) => { setQuery(''); setSelected(id) }} />
          </section>
          <section className="pane wide">
            {query.trim().length >= 2
              ? <SearchResults operator={operator} query={query.trim()} onOpen={(id) => { setQuery(''); setSelected(id) }} />
              : selected
                ? <MatterDetail key={`${operator}-${selected}`} operator={operator} id={selected} />
                : <p className="hint">Select a matter, or search across matters, documents and email. Switch operator to see how permissions change what is visible.</p>}
          </section>
          <ActivityPanel operator={operator} />
        </main>
      )}
    </div>
    </ToastProvider>
  )
}

function SearchBox({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <input
      data-testid="search"
      className="search"
      placeholder="Search matters, documents, email…"
      value={value}
      onChange={(e) => onChange(e.target.value)}
    />
  )
}

function Status({ state, onRetry, children }: { state: { status: string; error?: { status: number; title: string; detail?: string } }; onRetry?: () => void; children: React.ReactNode }) {
  if (state.status === 'loading') return <p className="status" data-testid="loading">Loading…</p>
  if (state.status === 'error') {
    const e = state.error!
    const friendly =
      e.status === 403 ? 'You do not have access to this item.' :
      e.status === 404 ? 'Not found.' :
      e.status === 0 ? 'Could not reach the server. Check your connection and try again.' :
      e.status >= 500 ? 'Something went wrong on our side. Please try again in a moment.' : e.title
    const detail = e.status !== 403 && e.status !== 404 && e.status !== 0 && e.status < 500 ? e.detail : undefined
    return (
      <p className="status error" data-testid="error" role="alert">
        {friendly}{detail ? ` ${detail}` : ''}
        {onRetry && <button type="button" onClick={onRetry}>Try again</button>}
      </p>
    )
  }
  return <>{children}</>
}

function MatterList({ operator, selected, onSelect }: { operator: string; selected: number | null; onSelect: (id: number) => void }) {
  const { state, reload } = useApi<Paged<Matter>>('/api/matters?pageSize=50', operator)
  return (
    <Status state={state} onRetry={reload}>
      {state.status === 'ok' && (
        <ul className="list" data-testid="matter-list">
          {state.data.items.map((m) => (
            <li key={m.id}>
              <button className={`row ${selected === m.id ? 'active' : ''}`} onClick={() => onSelect(m.id)}>
                <div className="row-top">
                  <span className="mono">{m.number}</span>
                  <span className="row-badges">
                    {m.isRestricted && <span className="badge">restricted</span>}
                    <StatusPill status={m.status} />
                  </span>
                </div>
                <div className="row-title">{m.title}</div>
                <div className="row-meta">{m.clientName} · {m.areaOfLaw} · {m.documentCount} docs · {m.emailCount} emails{m.responsibleCode && <> · <span className="mono">{m.responsibleCode}</span></>}</div>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Status>
  )
}

function MatterDetail({ operator, id }: { operator: string; id: number }) {
  const [tab, setTab] = useState<'documents' | 'emails'>('documents')
  const [docsVersion, setDocsVersion] = useState(0)
  const matterQ = useApi<Matter & { clientNumber: string; canEdit: boolean }>(`/api/matters/${id}`, operator)
  const docsQ = useApi<Paged<Document>>(`/api/matters/${id}/documents`, operator, docsVersion)
  const emailsQ = useApi<Paged<Email>>(`/api/matters/${id}/emails`, operator)
  const matter = matterQ.state
  const docs = docsQ.state
  const emails = emailsQ.state

  return (
    <Status state={matter} onRetry={matterQ.reload}>
      {matter.status === 'ok' && (
        <div data-testid="matter-detail">
          <div className="detail-head">
            <div className="row-top">
              <span className="mono">{matter.data.number}</span>
              <span className="row-badges">
                {matter.data.isRestricted && <span className="badge">restricted</span>}
                <StatusPill status={matter.data.status} />
              </span>
            </div>
            <h2>{matter.data.title}</h2>
            <div className="row-meta">
              {matter.data.clientName} · {matter.data.areaOfLaw} · opened {fmt(matter.data.openedUtc)}
              {matter.data.responsibleCode && <> · responsible <span className="mono">{matter.data.responsibleCode}</span></>} · {matter.data.canEdit ? 'can edit' : 'read only'}
            </div>
          </div>
          <div className="tabs" role="tablist">
            <button role="tab" aria-selected={tab === 'documents'} onClick={() => setTab('documents')}>Documents</button>
            <button role="tab" aria-selected={tab === 'emails'} onClick={() => setTab('emails')}>Email</button>
          </div>
          {tab === 'documents' ? (
            <Status state={docs} onRetry={docsQ.reload}>
              {docs.status === 'ok' && (
                <>
                {matter.data.canEdit && <AddDocument operator={operator} matterId={id} matterNumber={matter.data.number} onCreated={() => setDocsVersion((v) => v + 1)} />}
                <table className="grid" data-testid="documents">
                  <thead><tr><th>Title</th><th>Type</th><th>Author</th><th>Modified</th><th>Ver.</th></tr></thead>
                  <tbody>
                    {docs.data.items.map((d) => (
                      <tr key={d.id}><td>{d.title}<div className="row-meta">{d.keywords}</div></td><td>{d.documentType}</td><td className="mono">{d.authorCode}</td><td>{fmt(d.modifiedUtc)}</td><td>{d.versionCount}</td></tr>
                    ))}
                  </tbody>
                </table>
                </>
              )}
            </Status>
          ) : (
            <Status state={emails} onRetry={emailsQ.reload}>
              {emails.status === 'ok' && (
                <table className="grid" data-testid="emails">
                  <thead><tr><th>Subject</th><th>From</th><th>Received</th><th>Filed by</th><th>Marker</th></tr></thead>
                  <tbody>
                    {emails.data.items.length === 0 && <tr><td colSpan={5} className="hint">No email profiled to this matter yet.</td></tr>}
                    {emails.data.items.map((e) => (
                      <tr key={e.id}><td>{e.subject}<div className="row-meta">{e.bodyPreview}</div></td><td>{e.fromAddress}</td><td>{fmt(e.receivedUtc)}</td><td className="mono">{e.profiledByCode}</td><td>{e.markerSet ? '✓' : '—'}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Status>
          )}
        </div>
      )}
    </Status>
  )
}

function SearchResults({ operator, query, onOpen }: { operator: string; query: string; onOpen: (matterId: number) => void }) {
  const { state, reload } = useApi<Paged<SearchHit>>(`/api/search?q=${encodeURIComponent(query)}`, operator)
  return (
    <div data-testid="search-results">
      <h2>Search: “{query}”</h2>
      <Status state={state} onRetry={reload}>
        {state.status === 'ok' && (
          state.data.total === 0
            ? <p className="hint" data-testid="no-results">No results.</p>
            : (
              <ul className="list">
                {state.data.items.map((h) => (
                  <li key={`${h.kind}-${h.id}`}>
                    <button className="row" onClick={() => onOpen(h.matterId)}>
                      <div className="row-top"><span className="badge kind">{h.kind}</span><span className="mono">{h.matterNumber}</span></div>
                      <div className="row-title">{h.title}</div>
                      {h.snippet && <div className="row-meta">{h.snippet}</div>}
                    </button>
                  </li>
                ))}
              </ul>
            )
        )}
      </Status>
    </div>
  )
}

/** Profiles a new document onto the open matter; the result is confirmed as a toast and, two seconds later, as an Activity row from the server. */
function AddDocument({ operator, matterId, matterNumber, onCreated }: { operator: string; matterId: number; matterNumber: string; onCreated: () => void }) {
  const [title, setTitle] = useState('')
  const [type, setType] = useState('Correspondence')
  const [busy, setBusy] = useState(false)
  const toast = useToast()

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!title.trim() || busy) return
    setBusy(true)
    try {
      const d = await api<Document>(`/api/matters/${matterId}/documents`, operator, {
        method: 'POST',
        body: JSON.stringify({ title: title.trim(), documentType: type, storagePath: `web/${matterNumber}/${title.trim()}.docx` }),
      })
      toast('ok', `WEB · ${operator} · document.create on ${matterNumber} → 201 Created ("${d.title}")`)
      setTitle('')
      onCreated()
    } catch (err) {
      const status = err instanceof ApiError ? err.status : 0
      const why = err instanceof ApiError ? err.title : 'Network error'
      toast(status === 403 ? 'denied' : status === 409 ? 'conflict' : 'error', `WEB · ${operator} · document.create on ${matterNumber} → ${status || 'failed'} ${why}`)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="add-doc" onSubmit={submit} data-testid="add-document">
      <input data-testid="add-document-title" placeholder="Profile a new document… (title)" value={title} onChange={(e) => setTitle(e.target.value)} />
      <select value={type} onChange={(e) => setType(e.target.value)} aria-label="Document type">
        {['Correspondence', 'Pleading', 'Agreement', 'Discovery', 'Note'].map((t) => <option key={t}>{t}</option>)}
      </select>
      <button type="submit" disabled={busy || !title.trim()}>Add</button>
    </form>
  )
}
