import { useState } from 'react'
import type { Document, Email, Matter, Operator, Paged, SearchHit } from './api'
import { operators } from './api'
import { useApi } from './useApi'

const fmt = (iso: string) => new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })

export default function App() {
  const [operator, setOperator] = useState<Operator>('LES')
  const [selected, setSelected] = useState<number | null>(null)
  const [query, setQuery] = useState('')

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark">MD</span>
          <div>
            <div className="brand-title">MatterDesk</div>
            <div className="brand-sub">matters · documents · profiled email</div>
          </div>
        </div>
        <SearchBox value={query} onChange={setQuery} />
        <label className="operator">
          Operator
          <select data-testid="operator" value={operator} onChange={(e) => { setOperator(e.target.value as Operator); setSelected(null) }}>
            {operators.map((o) => <option key={o} value={o}>{o}</option>)}
          </select>
        </label>
      </header>

      <main className="layout">
        <section className="pane">
          <h2>Matters</h2>
          <MatterList operator={operator} selected={selected} onSelect={(id) => { setQuery(''); setSelected(id) }} />
        </section>
        <section className="pane wide">
          {query.trim().length >= 2
            ? <SearchResults operator={operator} query={query.trim()} onOpen={(id) => { setQuery(''); setSelected(id) }} />
            : selected
              ? <MatterDetail key={`${operator}-${selected}`} operator={operator} id={selected} />
              : <p className="hint">Select a matter, or search across matters, documents and email.</p>}
        </section>
      </main>
    </div>
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

function Status({ state, children }: { state: { status: string; error?: { status: number; title: string; detail?: string } }; children: React.ReactNode }) {
  if (state.status === 'loading') return <p className="status" data-testid="loading">Loading…</p>
  if (state.status === 'error') {
    const e = state.error!
    const friendly = e.status === 403 ? 'You do not have access to this matter.' : e.status === 404 ? 'Not found.' : e.title
    return <p className="status error" data-testid="error" role="alert">{friendly}{e.detail ? ` ${e.detail}` : ''}</p>
  }
  return <>{children}</>
}

function MatterList({ operator, selected, onSelect }: { operator: string; selected: number | null; onSelect: (id: number) => void }) {
  const state = useApi<Paged<Matter>>('/api/matters?pageSize=50', operator)
  return (
    <Status state={state}>
      {state.status === 'ok' && (
        <ul className="list" data-testid="matter-list">
          {state.data.items.map((m) => (
            <li key={m.id}>
              <button className={`row ${selected === m.id ? 'active' : ''}`} onClick={() => onSelect(m.id)}>
                <div className="row-top">
                  <span className="mono">{m.number}</span>
                  {m.isRestricted && <span className="badge">restricted</span>}
                </div>
                <div className="row-title">{m.title}</div>
                <div className="row-meta">{m.clientName} · {m.areaOfLaw} · {m.documentCount} docs · {m.emailCount} emails</div>
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
  const matter = useApi<Matter & { clientNumber: string; canEdit: boolean }>(`/api/matters/${id}`, operator)
  const docs = useApi<Paged<Document>>(`/api/matters/${id}/documents`, operator)
  const emails = useApi<Paged<Email>>(`/api/matters/${id}/emails`, operator)

  return (
    <Status state={matter}>
      {matter.status === 'ok' && (
        <div data-testid="matter-detail">
          <div className="detail-head">
            <span className="mono">{matter.data.number}</span>
            <h2>{matter.data.title}</h2>
            <div className="row-meta">{matter.data.clientName} · {matter.data.areaOfLaw} · opened {fmt(matter.data.openedUtc)} · {matter.data.canEdit ? 'can edit' : 'read only'}</div>
          </div>
          <div className="tabs" role="tablist">
            <button role="tab" aria-selected={tab === 'documents'} onClick={() => setTab('documents')}>Documents</button>
            <button role="tab" aria-selected={tab === 'emails'} onClick={() => setTab('emails')}>Email</button>
          </div>
          {tab === 'documents' ? (
            <Status state={docs}>
              {docs.status === 'ok' && (
                <table className="grid" data-testid="documents">
                  <thead><tr><th>Title</th><th>Type</th><th>Author</th><th>Modified</th><th>Ver.</th></tr></thead>
                  <tbody>
                    {docs.data.items.map((d) => (
                      <tr key={d.id}><td>{d.title}<div className="row-meta">{d.keywords}</div></td><td>{d.documentType}</td><td className="mono">{d.authorCode}</td><td>{fmt(d.modifiedUtc)}</td><td>{d.versionCount}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Status>
          ) : (
            <Status state={emails}>
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
  const state = useApi<Paged<SearchHit>>(`/api/search?q=${encodeURIComponent(query)}`, operator)
  return (
    <div data-testid="search-results">
      <h2>Search: “{query}”</h2>
      <Status state={state}>
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
