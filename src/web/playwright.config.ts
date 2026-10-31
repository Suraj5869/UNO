import { defineConfig, devices } from '@playwright/test';

const backendUrl = 'http://127.0.0.1:7137';
const frontendUrl = 'http://127.0.0.1:5173';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    baseURL: frontendUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      command: 'dotnet run --project ../backend/Uno.Api/Uno.Api.csproj --no-launch-profile --urls http://127.0.0.1:7137',
      url: backendUrl + '/swagger/index.html',
      cwd: process.cwd(),
      env: { ASPNETCORE_ENVIRONMENT: 'Development' },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: 'npm run dev -- --host 127.0.0.1',
      url: frontendUrl,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
});
