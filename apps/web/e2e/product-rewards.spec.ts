import type { Browser, Page } from "@playwright/test";
import { CASHIER } from "./accounts";
import {
  cashier,
  csrf,
  expect,
  issueCard,
  loginAs,
  offerName,
  test,
  type Staff,
} from "./fixtures";

const HANDOVERS = "**/api/cashier/reward-handovers";

/** Product rewards exchanged and activated through the API: their codes and the customer's card. */
async function activeRewards(
  page: Page,
  staff: Staff,
  baseURL: string,
  names: string[],
) {
  const card = await issueCard(page);
  const pos = await cashier(baseURL);
  await pos.purchase(card, 150 * names.length);
  await pos.dispose();
  const codes: string[] = [];
  for (const name of names) {
    const offer = await staff.createOffer({
      name,
      pointsCost: 150,
      kind: "Product",
    });
    const headers = async () => ({
      "X-CSRF-TOKEN": await csrf(page.request),
      "Idempotency-Key": crypto.randomUUID(),
    });
    const exchange = await page.request.post(
      `/api/rewards/${offer.id}/exchange`,
      { headers: await headers(), data: { version: offer.version } },
    );
    expect(exchange.status(), await exchange.text()).toBe(201);
    const { coupon } = (await exchange.json()) as { coupon: { id: string } };
    const activate = await page.request.post(
      `/api/me/coupons/${coupon.id}/activate`,
      { headers: await headers() },
    );
    expect(activate.status(), await activate.text()).toBe(200);
    codes.push(((await activate.json()) as { code: string }).code);
  }
  return { card, codes };
}

async function till(browser: Browser, baseURL: string) {
  const context = await browser.newContext({ baseURL, locale: "pl-PL" });
  const page = await context.newPage();
  await loginAs(page, CASHIER.email, CASHIER.password);
  await page.goto("/kasa");
  return { context, page };
}

test("a product reward: exchange, activate pickup, the cashier hands it over once", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Kawa");
  await staff.createOffer({ name, pointsCost: 150, kind: "Product" });
  const card = await issueCard(page);
  const pos = await cashier(baseURL!);
  await pos.purchase(card, 200);
  await pos.dispose();

  await page.goto("/oferty");
  const rewards = page.getByRole("list", { name: "Nagrody" });
  const item = rewards.getByTestId("offer").filter({ hasText: name });
  await expect(item).toContainText("Nagroda do odbioru w kasie");
  await item.getByRole("button", { name: "Wymień punkty" }).click();
  await expect(item).toContainText(`nagrodę „${name}”, do odbioru w kasie`);
  await item.getByRole("button", { name: "Potwierdź wymianę" }).click();
  await expect(
    page
      .getByRole("status")
      .filter({ hasText: "Wymieniono punkty na nagrodę" }),
  ).toBeVisible();

  await page.goto("/kupony");
  await expect(
    page.getByRole("heading", { name: "Moje kupony i nagrody" }),
  ).toBeVisible();
  const reward = page.getByTestId("coupon").filter({ hasText: name });
  await expect(reward).toContainText("Nagroda do odbioru w kasie");
  await expect(reward).toContainText("150 pkt");
  await reward.getByRole("button", { name: "Aktywuj odbiór" }).click();
  await expect(reward.getByTestId("coupon-status")).toHaveText("Aktywny");
  const code = (await reward
    .getByTestId("coupon-code")
    .getAttribute("data-code"))!;

  const context = await browser.newContext({ baseURL, locale: "pl-PL" });
  const register = await context.newPage();
  await loginAs(register, CASHIER.email, CASHIER.password);
  await register.goto("/kasa");
  await register.getByLabel("Kod karty klienta").fill(card);
  await register.getByLabel("Kod kuponu").fill(code);
  const handover = register.getByTestId("reward-handover");
  await expect(handover).toContainText(`${name}: nagroda do wydania`);
  await handover.getByRole("button", { name: "Wydaj nagrodę" }).click();
  await expect(
    register
      .getByRole("status")
      .filter({ hasText: `Wydano nagrodę „${name}”` }),
  ).toBeVisible();
  await expect(register.getByLabel("Kod kuponu")).toHaveValue("");

  // The same code again: already handed over.
  await register.getByLabel("Kod kuponu").fill(code);
  await expect(
    register
      .getByRole("alert")
      .filter({ hasText: "Ten kupon został już wykorzystany" }),
  ).toBeVisible();
  await context.close();

  await page.reload();
  await expect(reward.getByTestId("coupon-status")).toHaveText("Wykorzystany");
  await expect(reward.getByTestId("coupon-code")).toHaveCount(0);
});

test("lost handover response, then 429 on the retry: still unknown, the same key hands it over once", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Kawa");
  const { card, codes } = await activeRewards(page, staff, baseURL!, [name]);
  const { context, page: register } = await till(browser, baseURL!);

  const keys: string[] = [];
  let call = 0;
  await register.route(HANDOVERS, async (route) => {
    keys.push(route.request().headers()["idempotency-key"]);
    call += 1;
    if (call === 1) {
      // The server commits, the answer is lost.
      await route.fetch();
      await route.abort("failed");
    } else if (call === 2) {
      // The per-cashier limit (mocked: the real one is shared by all tests).
      await route.fulfill({
        status: 429,
        contentType: "application/problem+json",
        headers: { "Retry-After": "30" },
        body: JSON.stringify({ status: 429, code: "rate_limit.exceeded" }),
      });
    } else await route.continue();
  });

  await register.getByLabel("Kod karty klienta").fill(card);
  await register.getByLabel("Kod kuponu").fill(codes[0]);
  const handover = register.getByTestId("reward-handover");
  await handover.getByRole("button", { name: "Wydaj nagrodę" }).click();
  await expect(handover.getByRole("alert")).toContainText(
    "nie wiadomo, czy nagroda została wydana",
  );

  // A 429 on the retry says nothing about the first attempt: never "nie została wydana".
  await handover.getByRole("button", { name: "Ponów wydanie" }).click();
  await expect(handover.getByRole("alert")).toContainText("spróbuj za chwilę");
  await expect(handover.getByRole("alert")).toContainText("Nie wiadomo");
  await expect(handover).not.toContainText("nie została wydana");

  await handover.getByRole("button", { name: "Ponów wydanie" }).click();
  await expect(
    register
      .getByRole("status")
      .filter({ hasText: `Wydano nagrodę „${name}”` }),
  ).toBeVisible();
  expect(keys).toHaveLength(3);
  expect(new Set(keys).size).toBe(1);
  await context.close();

  const coupons = await page.request.get("/api/me/coupons");
  const items = (await coupons.json()) as {
    items: { offerName: string; status: string }[];
  };
  expect(items.items.filter((c) => c.offerName === name)).toEqual([
    expect.objectContaining({ status: "Redeemed" }),
  ]);
});

test("the state of one reward code does not carry over to another, the banner belongs to its card", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const [first, second] = [offerName("Kawa"), offerName("Hot dog")];
  const { card, codes } = await activeRewards(page, staff, baseURL!, [
    first,
    second,
  ]);
  const { context, page: register } = await till(browser, baseURL!);
  const handover = register.getByTestId("reward-handover");
  const coupon = register.getByLabel("Kod kuponu");

  await register.getByLabel("Kod karty klienta").fill(card);
  // The second code first, so its terms are cached when the cashier comes back to it.
  await coupon.fill(codes[1]);
  await expect(handover).toContainText(second);
  await coupon.fill(codes[0]);
  await expect(handover).toContainText(first);
  await register.route(HANDOVERS, (route) => route.abort("failed"));
  await handover.getByRole("button", { name: "Wydaj nagrodę" }).click();
  await expect(handover.getByRole("alert")).toContainText("Nie dotarła");

  await coupon.fill(codes[1]);
  await expect(handover).toContainText(second);
  await expect(handover.getByRole("alert")).toHaveCount(0);
  await register.unroute(HANDOVERS);
  await handover.getByRole("button", { name: "Wydaj nagrodę" }).click();
  const banner = register
    .getByRole("status")
    .filter({ hasText: `Wydano nagrodę „${second}”` });
  await expect(banner).toBeVisible();

  // The next customer: the card field changes (cleared like after a reset), the banner goes.
  await register.getByLabel("Kod karty klienta").fill("");
  await expect(banner).toHaveCount(0);
  await context.close();
});

test("an unknown handover outcome survives a retyped code and a 401; the banner does not come back for the card", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const [first, second] = [offerName("Kawa"), offerName("Myjnia")];
  const { card, codes } = await activeRewards(page, staff, baseURL!, [
    first,
    second,
  ]);
  const { context, page: register } = await till(browser, baseURL!);
  const handover = register.getByTestId("reward-handover");
  const cardField = register.getByLabel("Kod karty klienta");
  const coupon = register.getByLabel("Kod kuponu");

  let call = 0;
  await register.route(HANDOVERS, async (route) => {
    call += 1;
    if (call === 1) {
      // Committed, the answer is lost.
      await route.fetch();
      await route.abort("failed");
    } else if (call === 2) {
      await route.fulfill({
        status: 401,
        contentType: "application/problem+json",
        body: JSON.stringify({ status: 401 }),
      });
    } else await route.continue();
  });

  await cardField.fill(card);
  await coupon.fill(codes[0]);
  await handover.getByRole("button", { name: "Wydaj nagrodę" }).click();
  await expect(handover.getByRole("alert")).toContainText("Nie dotarła");

  // A 401 on the retry keeps the outcome unknown.
  await handover.getByRole("button", { name: "Ponów wydanie" }).click();
  await expect(handover.getByRole("alert")).toContainText("Sesja wygasła");
  await expect(handover.getByRole("alert")).toContainText("Nie wiadomo");

  // A typo fix remounts the panel: the warning stays; the server says it was handed over.
  await coupon.fill(codes[0].slice(0, -1));
  await coupon.fill(codes[0]);
  await expect(handover.getByRole("alert")).toContainText("Nie wiadomo");
  await handover.getByRole("button", { name: "Ponów wydanie" }).click();
  await expect(handover.getByRole("alert")).toContainText(
    "najpewniej przy poprzedniej próbie",
  );
  await register.unroute(HANDOVERS);

  // The banner is cleared with the card, not just hidden.
  await coupon.fill(codes[1]);
  await handover.getByRole("button", { name: "Wydaj nagrodę" }).click();
  const banner = register
    .getByRole("status")
    .filter({ hasText: `Wydano nagrodę „${second}”` });
  await expect(banner).toBeVisible();
  await cardField.fill("");
  await cardField.fill(card);
  await expect(register.getByLabel("Kod karty klienta")).toHaveValue(card);
  await expect(banner).toHaveCount(0);
  await context.close();
});
