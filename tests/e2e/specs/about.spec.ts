import { expect, test } from '@playwright/test'

test.describe('about page and MCP surface', () => {
  test('the About view opens from the nav and from #about, and returns to matters', async ({ page }) => {
    await page.goto('/')
    await page.getByTestId('nav-about').click()
    await expect(page.getByTestId('about')).toContainText('Model Context Protocol')
    await expect(page).toHaveURL(/#about$/)

    await page.getByTestId('nav-matters').click()
    await expect(page.getByTestId('matter-list')).toBeVisible()

    await page.goto('/#about')
    await expect(page.getByTestId('about')).toContainText('Microsoft Graph')
  })

  test('the MCP endpoint answers tools/list and enforces the operator in tools/call', async ({ request }) => {
    const api = 'http://localhost:5080'
    const list = await request.post(`${api}/mcp`, { headers: { 'X-Operator-Code': 'LES' }, data: { jsonrpc: '2.0', id: 1, method: 'tools/list' } })
    expect(list.ok()).toBeTruthy()
    const names = (await list.json()).result.tools.map((t: { name: string }) => t.name)
    expect(names).toEqual(['search_matters', 'get_matter', 'list_documents'])

    const call = (op: string) => request.post(`${api}/mcp`, {
      headers: { 'X-Operator-Code': op },
      data: { jsonrpc: '2.0', id: 2, method: 'tools/call', params: { name: 'search_matters', arguments: { query: 'Falcon' } } },
    })
    expect((await (await call('LES')).json()).result.structuredContent.total).toBe(0)
    expect((await (await call('JDU')).json()).result.structuredContent.total).toBeGreaterThan(0)
  })
})
