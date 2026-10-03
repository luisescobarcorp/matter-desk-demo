import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import type { ActivityEvent } from './api'
import { api } from './api'

// ---------- Toasts ----------------------------------------------------------------------------------------------

export type Toast = { id: number; tone: 'ok' | 'denied' | 'conflict' | 'error' | 'info'; text: string }

const ToastContext = createContext<(tone: Toast['tone'], text: string) => void>(() => {})

/** `useToast()` gives any screen a one-liner to confirm its own actions; the Activity panel uses the same channel for remote ones. */
export const useToast = () => useContext(ToastContext)

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const seq = useRef(0)

  const push = useCallback((tone: Toast['tone'], text: string) => {
    const id = ++seq.current
    setToasts((t) => [...t.slice(-4), { id, tone, text }])
    window.setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 6000)
  }, [])

  return (
    <ToastContext.Provider value={push}>
      {children}
      <div className="toasts" aria-live="polite" data-testid="toasts">
        {toasts.map((t) => (
          <div key={t.id} className={`toast ${t.tone}`} role="status">
            <span className={`dot ${t.tone}`} />{t.text}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

// ---------- Activity panel ---------------------------------------------------------------------------------------

const POLL_MS = 2000

export const toastText = (e: ActivityEvent) => `${e.channel.toUpperCase()} · ${e.operatorCode} · ${e.summary}`

/**
 * Polls /api/activity every two seconds with an `after` cursor and shows what every surface — browser,
 * local executable, AI client over MCP — just did as the same operator-governed log. New rows also flash
 * a toast so a tool call made from Claude or Cursor is confirmed on screen the moment it lands.
 */
export function ActivityPanel({ operator }: { operator: string }) {
  const [events, setEvents] = useState<ActivityEvent[]>([])
  const [error, setError] = useState<string | null>(null)
  const newest = useRef(0)
  const primed = useRef(false)
  const toast = useToast()

  useEffect(() => {
    let cancelled = false
    let timer: number | undefined

    const tick = async () => {
      try {
        const path = newest.current ? `/api/activity?after=${newest.current}&take=50` : '/api/activity?take=50'
        const rows = await api<ActivityEvent[]>(path, operator)
        if (cancelled) return
        setError(null)
        if (rows.length) {
          newest.current = Math.max(newest.current, ...rows.map((r) => r.id))
          setEvents((prev) => [...rows, ...prev].slice(0, 50))
          if (primed.current) [...rows].reverse().forEach((r) => toast(r.outcome, toastText(r)))
        }
        primed.current = true
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Activity unavailable')
      } finally {
        if (!cancelled) timer = window.setTimeout(tick, POLL_MS)
      }
    }
    void tick()
    return () => { cancelled = true; if (timer) window.clearTimeout(timer) }
  }, [operator, toast])

  // Re-render once a second so the relative times keep moving without another request.
  const [, setNow] = useState(0)
  useEffect(() => { const t = window.setInterval(() => setNow((n) => n + 1), 1000); return () => window.clearInterval(t) }, [])

  return (
    <aside className="pane activity" data-testid="activity-panel" aria-label="Activity">
      <div className="activity-head">
        <h2>Activity</h2>
        <span className="row-meta">live · every {POLL_MS / 1000}s</span>
      </div>
      <p className="activity-hint">Everything done as any operator — from this browser, the <code>matterdesk</code> executable, or an AI client over MCP — lands here.</p>
      {error && <p className="status error">{error}</p>}
      {!error && events.length === 0 && <p className="hint" data-testid="activity-empty">Nothing yet. Search, open a matter, or call a tool from Claude or Cursor.</p>}
      <ul className="activity-list" data-testid="activity-list">
        {events.map((e) => (
          <li key={e.id} className="activity-row" data-testid="activity-row" data-channel={e.channel} data-outcome={e.outcome}>
            <div className="activity-top">
              <span className={`pill channel ${e.channel}`}>{e.channel}</span>
              <span className="mono">{e.operatorCode}</span>
              <span className={`pill outcome ${e.outcome}`}>{e.outcome}</span>
              <span className="activity-time" title={new Date(e.occurredUtc).toLocaleString()}>{relative(e.occurredUtc)}</span>
            </div>
            <div className="activity-summary">{e.summary}</div>
          </li>
        ))}
      </ul>
    </aside>
  )
}

function relative(iso: string) {
  const s = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000))
  if (s < 5) return 'just now'
  if (s < 60) return `${s}s ago`
  const m = Math.round(s / 60)
  if (m < 60) return `${m}m ago`
  const h = Math.round(m / 60)
  return h < 24 ? `${h}h ago` : `${Math.round(h / 24)}d ago`
}
