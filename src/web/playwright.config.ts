import { defineConfig, devices } from '@playwright/test';

const backendUrl = 'http://localhost:7137';
const frontendUrl = 'http://localhost:5173';

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
      command: 'dotnet run --project ../backend/Uno.Api/Uno.Api.csproj --no-launch-profile --urls http://localhost:7137',
      url: backendUrl + '/swagger/index.html',
      cwd: process.cwd(),
      env: { ASPNETCORE_ENVIRONMENT: 'Development' },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: 'npm run dev -- --host 0.0.0.0',
      env: { VITE_API_URL: backendUrl, VITE_SIGNALR_URL: backendUrl + '/hubs/uno-room' },
      url: frontendUrl,
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
});
