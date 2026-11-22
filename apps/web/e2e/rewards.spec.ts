import type { Page } from "@playwright/test";
import {
  cashier,
  expect,
  issueCard,
  loginAs,
  offerName,
  PASSWORD,
  test,
} from "./fixtures";

const EXCHANGE = "**/api/rewards/*/exchange";

/** Gives the customer on `page` points through a real purchase at the cashier. */
async function earn(page: Page, baseURL: string, points: number) {
  const pos = await cashier(baseURL);
  await pos.purchase(await issueCard(page), points);
  await pos.dispose();
}

async function couponCount(page: Page) {
  const response = await page.request.get("/api/me/coupons");
  return ((await response.json()) as { items: unknown[] }).items.length;
}

const balance = (page: Page) =>
  page.getByRole("region", { name: "Twoje saldo" });

const offer = (page: Page, name: string) =>
  page.getByTestId("offer").filter({ hasText: name });

test("exchange shows the terms, buys one coupon and refreshes balance and history", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Rabat 20 gr");
  await staff.createOffer({
    name,
    pointsCost: 500,
    discountPerLiter: 0.2,
    maxLiters: 50,
  });
  await earn(page, baseURL!, 600);
  // The home page cached the balance before the purchase; a reload reads it again.
  await page.reload();

  await page.getByRole("link", { name: /Oferty/ }).click();
  await expect(balance(page)).toContainText("600 pkt");
  const item = offer(page, name);
  await expect(item).toContainText("0,20 zł/l, do 50 l (maks. 10,00 zł)");
  await expect(item.getByTestId("offer-cost")).toHaveText("500 pkt");

  await item.getByRole("button", { name: "Wymień punkty" }).click();
  const confirmation = item.getByRole("group", {
    name: "Potwierdzenie wymiany",
  });
  await expect(confirmation).toContainText("Wymienisz 500 pkt");
  await confirmation.getByRole("button", { name: "Potwierdź wymianę" }).click();

  await expect(
    page.getByRole("status").filter({ hasText: `Kupiono kupon „${name}”` }),
  ).toBeVisible();
  await expect(balance(page)).toContainText("100 pkt");
  expect(await couponCount(page)).toBe(1);

  await page.goto("/historia");
  const newest = page.getByTestId("history-item").first();
  await expect(newest).toContainText("Wymiana na kupon");
  await expect(newest.getByTestId("history-delta")).toHaveText("-500 pkt");
});

test("not enough points: nothing is charged and no coupon is created", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Za drogi");
  await staff.createOffer({
    name,
    pointsCost: 500,
    discountPerLiter: 0.1,
    maxLiters: 30,
  });
  await earn(page, baseURL!, 499);

  await page.goto("/oferty");
  const item = offer(page, name);
  await item.getByRole("button", { name: "Wymień punkty" }).click();
  await item.getByRole("button", { name: "Potwierdź wymianę" }).click();

  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "Masz za mało punktów",
  );
  await expect(balance(page)).toContainText("499 pkt");
  expect(await couponCount(page)).toBe(0);
});

test("changed terms close the confirmation; the new terms need a new confirmation", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Zmienny");
  const created = await staff.createOffer({
    name,
    pointsCost: 500,
    discountPerLiter: 0.2,
    maxLiters: 50,
  });
  await earn(page, baseURL!, 600);

  await page.goto("/oferty");
  const item = offer(page, name);
  await item.getByRole("button", { name: "Wymień punkty" }).click();
  await expect(item).toContainText("Wymienisz 500 pkt");

  // The admin edits the offer while the customer is looking at the old terms.
  await staff.updateOffer(created.id, created.version, {
    name,
    pointsCost: 450,
    discountPerLiter: 0.15,
    maxLiters: 40,
  });
  await item.getByRole("button", { name: "Potwierdź wymianę" }).click();

  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "Warunki oferty zmieniły się",
  );
  await expect(item.getByTestId("offer-cost")).toHaveText("450 pkt");
  await expect(item).toContainText("0,15 zł/l, do 40 l");
  await expect(
    item.getByRole("group", { name: "Potwierdzenie wymiany" }),
  ).toBeHidden();
  await expect(balance(page)).toContainText("600 pkt");
  expect(await couponCount(page)).toBe(0);

  await item.getByRole("button", { name: "Wymień punkty" }).click();
  await expect(item).toContainText("Wymienisz 450 pkt");
  await item.getByRole("button", { name: "Potwierdź wymianę" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Kupiono kupon" }),
  ).toBeVisible();
  await expect(balance(page)).toContainText("150 pkt");
  expect(await couponCount(page)).toBe(1);
});

test("lost response: the retry reuses the key, one coupon and one debit", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Utracony");
  await staff.createOffer({
    name,
    pointsCost: 500,
    discountPerLiter: 0.2,
    maxLiters: 50,
  });
  await earn(page, baseURL!, 600);

  const keys: string[] = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/exchange"))
      keys.push(request.headers()["idempotency-key"]);
  });
  let committed = false;
  await page.route(EXCHANGE, async (route) => {
    const response = await route.fetch();
    committed = response.status() === 201;
    await route.abort("failed");
  });

  await page.goto("/oferty");
  const item = offer(page, name);
  await item.getByRole("button", { name: "Wymień punkty" }).click();
  await item.getByRole("button", { name: "Potwierdź wymianę" }).click();

  const unknown = page.getByRole("main").getByRole("alert");
  await expect(unknown).toContainText("nie wiadomo, czy wymiana się udała");
  expect(committed).toBe(true);
  // Other exchanges stay locked until this one is resolved.
  await expect(
    item.getByRole("button", { name: "Potwierdź wymianę" }),
  ).toBeDisabled();

  await page.unroute(EXCHANGE);
  await unknown.getByRole("button", { name: "Ponów wymianę" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Kupiono kupon" }),
  ).toBeVisible();

  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);
  await expect(balance(page)).toContainText("100 pkt");
  expect(await couponCount(page)).toBe(1);
});

test("loading error of the catalog can be retried", async ({
  page,
  customer,
}) => {
  void customer;
  let healthy = false;
  await page.route("**/api/rewards", (route) =>
    healthy ? route.continue() : route.abort("failed"),
  );

  await page.goto("/oferty");
  const alert = page.getByRole("main").getByRole("alert");
  await expect(alert).toContainText("Brak połączenia z serwerem", {
    timeout: 15_000,
  });
  healthy = true;
  await alert.getByRole("button", { name: "Spróbuj ponownie" }).click();
  await expect(page.getByRole("list", { name: "Oferty" })).toBeVisible();
});

test("leaving the screen with an unknown outcome keeps the lock and the key", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Porzucony");
  await staff.createOffer({
    name,
    pointsCost: 500,
    discountPerLiter: 0.2,
    maxLiters: 50,
  });
  await earn(page, baseURL!, 600);

  const keys: string[] = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/exchange"))
      keys.push(request.headers()["idempotency-key"]);
  });
  await page.route(EXCHANGE, async (route) => {
    await route.fetch();
    await route.abort("failed");
  });

  await page.goto("/oferty");
  const item = offer(page, name);
  await item.getByRole("button", { name: "Wymień punkty" }).click();
  await item.getByRole("button", { name: "Potwierdź wymianę" }).click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "nie wiadomo, czy wymiana się udała",
  );
  await page.unroute(EXCHANGE);

  // Away and back (client navigation), then a full reload: the unfinished exchange is still there.
  await page.getByRole("link", { name: "← Saldo" }).click();
  await page.waitForURL("/");
  await page.getByRole("link", { name: /Oferty/ }).click();
  await page.waitForURL("/oferty");
  // Next keeps the screen's state on client navigation; the lock must hold either way.
  await expect(
    page.getByRole("main").getByRole("button", { name: "Ponów wymianę" }),
  ).toBeVisible();
  await expect(
    item.getByRole("button", { name: "Potwierdź wymianę" }),
  ).toBeDisabled();
  await page.reload();
  const banner = page.getByRole("main").getByRole("alert");
  await expect(banner).toContainText(
    `Wymiana na kupon „${name}” mogła się nie zakończyć`,
  );
  await expect(
    item.getByRole("button", { name: "Wymień punkty" }),
  ).toBeDisabled();

  await banner.getByRole("button", { name: "Ponów wymianę" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Kupiono kupon" }),
  ).toBeVisible();
  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);
  expect(await couponCount(page)).toBe(1);
  await expect(balance(page)).toContainText("100 pkt");
});

test("lost response, then the session ends: signing in again keeps the key, one debit", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  const name = offerName("Sesja");
  await staff.createOffer({
    name,
    pointsCost: 500,
    discountPerLiter: 0.2,
    maxLiters: 50,
  });
  await earn(page, baseURL!, 600);

  const keys: string[] = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/exchange"))
      keys.push(request.headers()["idempotency-key"]);
  });
  await page.route(EXCHANGE, async (route) => {
    await route.fetch();
    await route.abort("failed");
  });
  await page.goto("/oferty");
  const item = offer(page, name);
  await item.getByRole("button", { name: "Wymień punkty" }).click();
  await item.getByRole("button", { name: "Potwierdź wymianę" }).click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "nie wiadomo, czy wymiana się udała",
  );
  await page.unroute(EXCHANGE);

  // The session ends (expired cookie); a retry now gets 401 and must not drop the key.
  await page.context().clearCookies();
  const unknown = page
    .getByRole("main")
    .getByRole("alert")
    .filter({ has: page.getByRole("button", { name: "Ponów wymianę" }) });
  await unknown.getByRole("button", { name: "Ponów wymianę" }).click();
  await expect(unknown).toContainText("Sesja wygasła");
  // The same person signs in again in this tab.
  await loginAs(page, customer.email, PASSWORD);
  await page.goto("/oferty");
  const restored = page.getByRole("main").getByRole("alert");
  await expect(restored).toContainText(name);
  await restored.getByRole("button", { name: "Ponów wymianę" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Kupiono kupon" }),
  ).toBeVisible();

  // Lost, 401 after the session ended, then the retry after signing in: one key throughout.
  expect(keys).toHaveLength(3);
  expect(new Set(keys).size).toBe(1);
  await expect(balance(page)).toContainText("100 pkt");
  expect(await couponCount(page)).toBe(1);
});
