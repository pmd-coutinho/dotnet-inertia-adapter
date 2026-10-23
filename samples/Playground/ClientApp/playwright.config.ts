import { defineConfig } from '@playwright/test'

const port = process.env.PLAYGROUND_PORT ?? '5197'
const ssrPort = process.env.PLAYGROUND_SSR_PORT ?? '5198'
export default defineConfig({
  testDir: './tests', workers: 1, retries: 0, timeout: 45000,
  use: {
    baseURL: `http://127.0.0.1:${port}`, viewport: { width: 1280, height: 900 },
    trace: 'retain-on-failure', screenshot: 'only-on-failure',
    launchOptions: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE } : {},
  },
  webServer: [
    { command: 'npm run ssr', url: `http://127.0.0.1:${ssrPort}/health`, env: { SSR_PORT: ssrPort }, timeout: 30000 },
    {
      command: `dotnet run --project ../Playground.csproj --no-launch-profile --urls http://127.0.0.1:${port}`,
      url: `http://127.0.0.1:${port}/`, timeout: 60000,
      stdout: 'pipe',
      env: { ASPNETCORE_ENVIRONMENT: 'Development', SSR_URL: `http://127.0.0.1:${ssrPort}`, SSR_ENABLED: 'true', Logging__LogLevel__Default: 'Warning' },
    },
  ],
})
