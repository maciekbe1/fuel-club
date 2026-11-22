import {
  expect,
  test as base,
  request as playwrightRequest,
  type APIRequestContext,
  type Page,
} from "@playwright/test";
import { CASHIER } from "./accounts";

export const PASSWORD = "E2e-Password-123!";

async function csrf(request: APIRequestContext) {
  const response = await request.get("/api/auth/csrf");
  return ((await response.json()) as { token: string }).token;
}

/** Registers a fresh customer and logs in through the login form. */
export async function newCustomer(page: Page, displayName: string) {
  const email = `e2e-${crypto.randomUUID()}@example.test`;
  const register = await page.request.post("/api/auth/register", {
    headers: { "X-CSRF-TOKEN": await csrf(page.request) },
    data: { email, password: PASSWORD, displayName },
  });
  expect(register.status()).toBe(201);

  await page.goto("/login");
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Hasło", { exact: true }).fill(PASSWORD);
  await page.getByRole("button", { name: "Zaloguj się" }).click();
  await page.waitForURL("/");
  return { email };
}

/** Logs in an existing account through the login form. */
export async function loginAs(page: Page, email: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Hasło", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Zaloguj się" }).click();
  await page.waitForURL("/");
}

/** Issues a card for the customer logged in on `page` and returns its code. */
export async function issueCard(page: Page) {
  const response = await page.request.post("/api/me/loyalty-card", {
    headers: {
      "X-CSRF-TOKEN": await csrf(page.request),
      "Idempotency-Key": crypto.randomUUID(),
    },
  });
  expect(response.status()).toBe(201);
  return ((await response.json()) as { code: string }).code;
}

const warsawToday = () =>
  new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Warsaw",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());

/** The bootstrap cashier, logged in through the API; registers purchases like the POS would. */
export async function cashier(baseURL: string) {
  const request = await playwrightRequest.newContext({ baseURL });
  const login = await request.post("/api/auth/login", {
    headers: { "X-CSRF-TOKEN": await csrf(request) },
    data: { email: CASHIER.email, password: CASHIER.password },
  });
  expect(login.ok()).toBe(true);
  const token = await csrf(request);

  return {
    async purchase(cardCode: string, amount: number, liters = 10) {
      const response = await request.post("/api/cashier/fuel-purchases", {
        headers: {
          "X-CSRF-TOKEN": token,
          "Idempotency-Key": crypto.randomUUID(),
        },
        data: {
          cardCode,
          amount,
          liters,
          saleDate: warsawToday(),
          receiptNumber: `E2E/${crypto.randomUUID()}`.toUpperCase(),
        },
      });
      expect(response.status(), await response.text()).toBe(201);
    },
    dispose: () => request.dispose(),
  };
}

export const test = base.extend<{ customer: { email: string } }>({
  customer: async ({ page }, provide) => {
    await provide(await newCustomer(page, "Anna"));
  },
});

export { expect };
