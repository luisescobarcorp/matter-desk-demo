import { expect, test } from '@playwright/test'

const api = 'http://localhost:5080'

test.describe('activity panel shows every surface live', () => {
  test('a search from the browser appears as a web row with a toast-ready summary', async ({ page }) => {
    await page.goto('/')
    const panel = page.getByTestId('activity-panel')
    await expect(panel).toBeVisible()

    const probe = `Acme-${Date.now().toString().slice(-5)}`
    await page.getByTestId('search').fill(probe)

    const row = panel.getByTestId('activity-row').filter({ hasText: `search "${probe}"` }).first()
    await expect(row).toBeVisible({ timeout: 10_000 })
    await expect(row).toHaveAttribute('data-channel', 'web')
    await expect(row.locator('.pill.channel')).toHaveText('web')
    await expect(row).toContainText('LES')
    await expect(row).toContainText('0 results')
  })

  test('an MCP tools/call made as JDU shows up as an mcp row and a toast while the page is open', async ({ page, request }) => {
    await page.goto('/')
    await expect(page.getByTestId('activity-panel')).toBeVisible()
    // Let the panel take its first snapshot so the next event is "new" and toasts.
    await page.waitForTimeout(2500)

    const probe = `Falcon${Date.now().toString().slice(-4)}`
    const r = await request.post(`${api}/mcp/JDU`, {
      data: { jsonrpc: '2.0', id: 7, method: 'tools/call', params: { name: 'search_matters', arguments: { query: probe } } },
    })
    expect(r.ok()).toBeTruthy()
    expect((await r.json()).result.structuredContent.operator).toBe('JDU')

    const row = page.getByTestId('activity-row').filter({ hasText: `search_matters "${probe}"` }).first()
    await expect(row).toBeVisible({ timeout: 10_000 })
    await expect(row).toHaveAttribute('data-channel', 'mcp')
    await expect(row.locator('.pill.channel')).toHaveText('mcp')
    await expect(row).toContainText('JDU')

    await expect(page.getByTestId('toasts')).toContainText(`MCP · JDU · search_matters "${probe}"`)
  })

  test('prompts/list is served to MCP clients and a prompts/get is logged', async ({ request }) => {
    const list = await request.post(`${api}/mcp`, { headers: { 'X-Operator-Code': 'LES' }, data: { jsonrpc: '2.0', id: 1, method: 'prompts/list' } })
    const names = (await list.json()).result.prompts.map((p: { name: string }) => p.name)
    expect(names).toEqual(['demo', 'find_matter', 'file_check'])

    const get = await request.post(`${api}/mcp`, { headers: { 'X-Operator-Code': 'LES' }, data: { jsonrpc: '2.0', id: 2, method: 'prompts/get', params: { name: 'demo' } } })
    expect((await get.json()).result.messages[0].content.text).toContain('Activity panel')

    const feed = await request.get(`${api}/api/activity?take=5`, { headers: { 'X-Operator-Code': 'LES' } })
    expect((await feed.json()).some((e: { action: string }) => e.action === 'prompt.demo')).toBeTruthy()
  })
})
