export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number; hasMore: boolean }
export type Matter = { id: number; number: string; title: string; areaOfLaw: string; clientName: string; isRestricted: boolean; openedUtc: string; documentCount: number; emailCount: number }
export type Document = { id: number; matterId: number; matterNumber: string; title: string; documentType: string; authorCode: string; keywords: string | null; modifiedUtc: string; versionCount: number; version: number }
export type Email = { id: number; matterId: number; subject: string; fromAddress: string; receivedUtc: string; bodyPreview: string | null; profiledByCode: string; markerSet: boolean }
export type ActivityEvent = { id: number; occurredUtc: string; operatorCode: string; channel: 'web' | 'cli' | 'mcp'; action: string; target: string | null; outcome: 'ok' | 'denied' | 'conflict' | 'error'; httpStatus: number; summary: string }
export type SearchHit = { kind: 'matter' | 'document' | 'email'; id: number; matterId: number; matterNumber: string; title: string; snippet: string | null; whenUtc: string }

export class ApiError extends Error {
  status: number
  title: string
  detail?: string
  constructor(status: number, title: string, detail?: string) {
    super(title)
    this.status = status
    this.title = title
    this.detail = detail
  }
}

/**
 * Dev/test authentication is the X-Operator-Code header (see Program.cs). In production this
 * becomes an Authorization: Bearer token from the OIDC sign-in; nothing else in this client changes.
 */
export async function api<T>(path: string, operator: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    ...init,
    headers: { 'X-Operator-Code': operator, 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
  })
  if (res.ok) return (res.status === 204 ? (undefined as T) : await res.json()) as T

  let title = res.statusText || `HTTP ${res.status}`
  let detail: string | undefined
  try {
    const problem = await res.json()
    title = problem.title ?? title
    detail = problem.detail
  } catch {
    /* no problem+json body */
  }
  throw new ApiError(res.status, title, detail)
}

export const operators = ['LES', 'JDU', 'PAR'] as const
export type Operator = (typeof operators)[number]
