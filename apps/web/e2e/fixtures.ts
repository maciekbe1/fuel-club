import {
  expect,
  test as base,
  request as playwrightRequest,
  type APIRequestContext,
  type Page,
} from "@playwright/test";
import { ADMIN, CASHIER } from "./accounts";

export const PASSWORD = "E2e-Password-123!";

export async function csrf(request: APIRequestContext) {
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
    async purchase(
      cardCode: string,
      amount: number,
      liters = 10,
      couponCode?: string,
    ) {
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
          couponCode,
        },
      });
      expect(response.status(), await response.text()).toBe(201);
      return (await response.json()) as {
        discount: number;
        paidAmount: number;
        pointsAwarded: number;
      };
    },
    dispose: () => request.dispose(),
  };
}

export type OfferTerms = {
  name: string;
  description?: string;
  pointsCost: number;
  /** "Product" for a reward handed over at the till; no per-liter terms then. */
  kind?: "FuelDiscount" | "Product";
  discountPerLiter?: number;
  maxLiters?: number;
  minLiters?: number;
  isActive?: boolean;
};

/** The bootstrap admin, logged in through the API; manages offers like the admin panel would. */
export async function admin(baseURL: string) {
  const request = await playwrightRequest.newContext({ baseURL });
  const login = await request.post("/api/auth/login", {
    headers: { "X-CSRF-TOKEN": await csrf(request) },
    data: { email: ADMIN.email, password: ADMIN.password },
  });
  expect(login.ok()).toBe(true);
  const token = await csrf(request);
  const body = (terms: OfferTerms) => ({
    description: "Na dowolne paliwo",
    isActive: true,
    ...terms,
  });

  const created = new Map<string, OfferTerms & { version: number }>();

  return {
    async createOffer(terms: OfferTerms) {
      const response = await request.post("/api/admin/rewards", {
        headers: {
          "X-CSRF-TOKEN": token,
          "Idempotency-Key": crypto.randomUUID(),
        },
        data: body(terms),
      });
      expect(response.status(), await response.text()).toBe(201);
      const offer = (await response.json()) as { id: string; version: number };
      created.set(offer.id, { ...terms, version: offer.version });
      return offer;
    },
    async updateOffer(id: string, version: number, terms: OfferTerms) {
      const response = await request.put(`/api/admin/rewards/${id}`, {
        headers: { "X-CSRF-TOKEN": token },
        data: { ...body(terms), version },
      });
      expect(response.status(), await response.text()).toBe(200);
      const offer = (await response.json()) as { id: string; version: number };
      if (created.has(id))
        created.set(id, { ...terms, version: offer.version });
      return offer;
    },
    /** Deactivates the offers this test created, so the shared e2e catalog does not grow. */
    async retire() {
      for (const [id, offer] of created)
        await request.put(`/api/admin/rewards/${id}`, {
          headers: { "X-CSRF-TOKEN": token },
          data: { ...body(offer), isActive: false, version: offer.version },
        });
      await request.dispose();
    },
  };
}

export type Staff = Awaited<ReturnType<typeof admin>>;

/** A unique offer name, so parallel tests find their own offer in the shared catalog. */
export const offerName = (label: string) =>
  `${label} ${crypto.randomUUID().slice(0, 8)}`;

export const test = base.extend<{
  customer: { email: string };
  staff: Staff;
}>({
  customer: async ({ page }, provide) => {
    await provide(await newCustomer(page, "Anna"));
  },
  /** The admin for this test; its offers are deactivated afterwards. */
  staff: async ({ baseURL }, provide) => {
    const staff = await admin(baseURL!);
    await provide(staff);
    await staff.retire();
  },
});

export { expect };

/**
 * A purchased (not yet active) coupon for the customer on `page`: points from a real purchase, then
 * the exchange through the API. Returns the coupon id and the customer's card code.
 */
export async function buyCoupon(
  page: Page,
  staff: Staff,
  baseURL: string,
  name = offerName("Kupon"),
  terms: Partial<OfferTerms> = {},
) {
  const offer = await staff.createOffer({
    name,
    pointsCost: 500,
    discountPerLiter: 0.2,
    maxLiters: 50,
    ...terms,
  });
  const card = await issueCard(page);
  const pos = await cashier(baseURL);
  await pos.purchase(card, 500);
  await pos.dispose();
  const response = await page.request.post(
    `/api/rewards/${offer.id}/exchange`,
    {
      headers: {
        "X-CSRF-TOKEN": await csrf(page.request),
        "Idempotency-Key": crypto.randomUUID(),
      },
      data: { version: offer.version },
    },
  );
  expect(response.status(), await response.text()).toBe(201);
  const body = (await response.json()) as { coupon: { id: string } };
  return { couponId: body.coupon.id, card, name };
}
