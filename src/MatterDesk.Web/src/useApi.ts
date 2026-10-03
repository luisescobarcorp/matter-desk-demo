import { useEffect, useState } from 'react'
import { ApiError, api } from './api'

export type Async<T> = { status: 'loading' } | { status: 'error'; error: ApiError } | { status: 'ok'; data: T }

/** Loading / error / data in one place, with cleanup so a slow response cannot update an unmounted screen. */
export function useApi<T>(path: string | null, operator: string, version = 0): { state: Async<T>; reload: () => void } {
  const [bump, setBump] = useState(0)
  const [state, setState] = useState<Async<T>>({ status: 'loading' })

  useEffect(() => {
    if (!path) return
    let cancelled = false
    setState({ status: 'loading' })
    api<T>(path, operator)
      .then((data) => { if (!cancelled) setState({ status: 'ok', data }) })
      .catch((e: unknown) => {
        if (cancelled) return
        setState({ status: 'error', error: e instanceof ApiError ? e : new ApiError(0, 'Network error') })
      })
    return () => { cancelled = true }
  }, [path, operator, version, bump])

  return { state, reload: () => setBump((n) => n + 1) }
}
