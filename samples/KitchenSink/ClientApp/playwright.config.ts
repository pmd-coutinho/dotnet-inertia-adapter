import { defineConfig } from "@playwright/test";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const port = process.env.KITCHEN_SINK_PORT ?? "5271";
const ssrPort = process.env.KITCHEN_SINK_SSR_PORT ?? "5272";
const databaseDirectory = mkdtempSync(
  join(tmpdir(), "inertianet-kitchen-sink-"),
);
process.env.KITCHEN_SINK_TEST_DIRECTORY = databaseDirectory;

export default defineConfig({
  testDir: "./tests",
  workers: 1,
  retries: 0,
  timeout: 30000,
  globalTeardown: "./tests/teardown.ts",
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    viewport: { width: 1440, height: 1080 },
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    launchOptions: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE
      ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE }
      : {},
  },
  webServer: [
    {
      command: "npm run ssr",
      url: `http://127.0.0.1:${ssrPort}/health`,
      env: { SSR_PORT: ssrPort },
      timeout: 30000,
    },
    {
      command: `dotnet run --project ../KitchenSink.csproj --no-launch-profile --urls http://127.0.0.1:${port}`,
      url: `http://127.0.0.1:${port}/health`,
      timeout: 60000,
      env: {
        SSR_URL: `http://127.0.0.1:${ssrPort}`,
        SSR_ENABLED: "true",
        DATABASE_PATH: join(databaseDirectory, "test.db"),
        Logging__LogLevel__Default: "Error",
      },
    },
  ],
});
