import { defineConfig, devices } from "@playwright/test";
import { ADMIN, CASHIER } from "./e2e/accounts";

/**
 * E2E against the real API and a dedicated database (fuelclub_e2e on the postgres-test container).
 * Ports and the Next build dir differ from the regular dev setup, so both can run at once.
 */
const apiPort = 5281;
const webPort = 3100;
const proxyPort = 5282;
const database =
  process.env.FUELCLUB_E2E_DATABASE ??
  "Host=localhost;Port=5434;Database=fuelclub_e2e;Username=fuelclub_test;Password=fuelclub_test";

// The API migrates this database before the run: refuse anything but the dedicated e2e database.
const databaseName = /(?:^|;)\s*Database\s*=\s*([^;]+)/i
  .exec(database)?.[1]
  ?.trim();
if (databaseName !== "fuelclub_e2e")
  throw new Error(
    `E2E must run on the database 'fuelclub_e2e', got '${databaseName ?? "(none)"}'. Check FUELCLUB_E2E_DATABASE.`,
  );

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: `http://localhost:${webPort}`,
    locale: "pl-PL",
    timezoneId: "Europe/Warsaw",
    trace: "retain-on-failure",
  },
  projects: [
    { name: "mobile", use: { ...devices["Pixel 7"] } },
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
  ],
  webServer: [
    {
      // The API never migrates itself; the e2e database is migrated (and created if missing) first.
      command: `dotnet tool run dotnet-ef database update --project ../api --connection "${database}" && dotnet run --project ../api --no-launch-profile --urls http://localhost:${apiPort}`,
      url: `http://localhost:${apiPort}/openapi/v1.json`,
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ConnectionStrings__DefaultConnection: database,
        DemoAccounts__Enabled: "false",
        // Every test logs in from 127.0.0.1 and one cashier serves all tests: the limits would stop the suite.
        RateLimits__AuthPermitLimit: "100000",
        RateLimits__PurchasePermitLimit: "100000",
        Rewards__PreviewPermitLimit: "1000",
        BootstrapCashier__Email: CASHIER.email,
        BootstrapCashier__Password: CASHIER.password,
        BootstrapCashier__StationCode: CASHIER.station,
        BootstrapCashier__RegisterCode: "K1",
        BootstrapAdmin__Email: ADMIN.email,
        BootstrapAdmin__Password: ADMIN.password,
      },
      // Never reuse: a running server may skip the migration or point at another database.
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: "ignore",
    },
    {
      command: "node e2e/fault-proxy.mjs",
      port: proxyPort,
      env: {
        PROXY_PORT: String(proxyPort),
        PROXY_TARGET: `http://localhost:${apiPort}`,
      },
      // Never reuse: a running server may skip the migration or point at another database.
      reuseExistingServer: false,
    },
    {
      command: `npx next dev --port ${webPort}`,
      url: `http://localhost:${webPort}/login`,
      env: {
        NEXT_DIST_DIR: ".next-e2e",
        API_INTERNAL_URL: `http://localhost:${proxyPort}`,
      },
      // Never reuse: a running server may skip the migration or point at another database.
      reuseExistingServer: false,
      timeout: 180_000,
    },
  ],
});
