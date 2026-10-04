import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "tests/ui",
  timeout: 60000,
  workers: 1,
  use: {
    baseURL: "http://127.0.0.1:5173",
    headless: true,
    locale: "zh-CN",
    viewport: { width: 1220, height: 850 },
    launchOptions: {
      executablePath:
        process.env.INVOICE_CHROMIUM ||
        (process.platform === "linux" ? "/usr/bin/chromium" : undefined),
    },
  },
  projects: [
    { name: "browser", testMatch: "**/*.ui.spec.ts" },
    { name: "electron", testMatch: "**/*.electron.spec.ts" },
  ],
  webServer: {
    command: "npm exec vite",
    url: "http://127.0.0.1:5173",
    reuseExistingServer: !process.env.CI,
  },
  reporter: "list",
});
