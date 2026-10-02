import { defineConfig, devices } from '@playwright/test'

const API_PORT = 5080
const WEB_PORT = 5173

/**
 * Boots the real API (SQLite, seeded) and the Vite dev server, then drives the React screen in Chrome.
 * Runs locally with `npm test` and in CI via azure-pipelines.yml.
 */
export default defineConfig({
  testDir: './specs',
  timeout: 30_000,
  expect: { timeout: 7_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: `http://localhost:${WEB_PORT}`,
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chrome',
      use: {
        ...devices['Desktop Chrome'],
        channel: 'chrome',
        launchOptions: process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {},
      },
    },
  ],
  webServer: [
    {
      command: `dotnet run --project ../../src/MatterDesk.Api --urls http://localhost:${API_PORT}`,
      url: `http://localhost:${API_PORT}/healthz`,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: { ASPNETCORE_ENVIRONMENT: 'Development', ConnectionStrings__Sqlite: 'Data Source=e2e.db' },
    },
    {
      command: 'npm run dev -- --port ' + WEB_PORT,
      cwd: '../../src/MatterDesk.Web',
      url: `http://localhost:${WEB_PORT}`,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
})
