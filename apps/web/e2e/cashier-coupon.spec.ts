import { chromium, type Page } from "@playwright/test";
import { TRACK_STREAMS, writeQrVideo } from "./camera";
import { buyCoupon, cashier, expect, test, type Staff } from "./fixtures";
import { openRegister } from "./register";

const PURCHASES = "**/api/cashier/fuel-purchases";
const PREVIEW = "**/api/cashier/coupons/preview";

/** A purchased coupon activated on the customer's phone; returns the card and coupon codes. */
async function activeCoupon(page: Page, staff: Staff, baseURL: string) {
  const { name, card } = await buyCoupon(page, staff, baseURL);
  await page.goto("/kupony");
  const coupon = page.getByTestId("coupon").filter({ hasText: name });
  await coupon.getByRole("button", { name: "Aktywuj kupon" }).click();
  const code = (await coupon
    .getByTestId("coupon-code")
    .getAttribute("data-code"))!;
  return { name, card, code, coupon };
}

async function fillSale(register: Page, amount: string, liters: string) {
  await register.getByLabel("Kwota (zł)").fill(amount);
  await register.getByLabel("Litry").fill(liters);
  await register
    .getByLabel("Numer paragonu")
    .fill(`E2E/${crypto.randomUUID().slice(0, 8)}`.toUpperCase());
}

test("coupon at the till: terms on the full code, discount on Przelicz, one redemption, discount in history", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { name, card, code, coupon } = await activeCoupon(
    page,
    staff,
    baseURL!,
  );
  const register = await openRegister(browser, baseURL!, () => {
    const sent: unknown[] = [];
    (window as unknown as { __sent: unknown[] }).__sent = sent;
    const original = window.fetch.bind(window);
    window.fetch = async (input, init) => {
      if (
        input instanceof Request &&
        input.method === "POST" &&
        input.url.endsWith("/fuel-purchases")
      )
        sent.push(JSON.parse(await input.clone().text()));
      return original(input, init);
    };
  });
  const till = register.page;
  const previews: string[] = [];
  till.on("request", (r) => {
    if (r.url().endsWith("/coupons/preview")) previews.push(r.url());
  });

  await till.getByLabel("Kod karty klienta").fill(card);
  await fillSale(till, "100", "40");
  // Typed key by key: only the complete code asks the server, and nothing is computed yet.
  await till.getByLabel("Kod kuponu").pressSequentially(code.toLowerCase());
  const terms = till.getByTestId("coupon-terms");
  await expect(terms).toContainText(name);
  await expect(terms).toContainText("0,20 zł/l, do 50 l (maks. 10,00 zł)");
  await expect(till.getByTestId("coupon-calculation")).toHaveCount(0);
  expect(previews).toHaveLength(1);

  await till.getByRole("button", { name: "Przelicz rabat" }).click();
  const calc = till.getByTestId("coupon-calculation");
  await expect(calc).toContainText("8,00 zł");
  await expect(calc).toContainText("92,00 zł");
  await expect(calc.getByTestId("calc-points")).toHaveText("92");
  expect(previews).toHaveLength(2);

  // A new amount marks the calculation outdated without asking the server.
  await till.getByLabel("Litry").fill("60");
  await expect(calc).toHaveAttribute("data-outdated", "true");
  await expect(till.getByText("wyliczenie jest nieaktualne")).toBeVisible();
  expect(previews).toHaveLength(2);
  await till.getByRole("button", { name: "Przelicz rabat" }).click();
  await expect(calc).toHaveAttribute("data-outdated", "false");
  await expect(calc).toContainText("10,00 zł");

  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByTestId("purchase-result")).toHaveText(
    "Rabat z kuponu 10,00 zł · do zapłaty 90,00 zł",
  );
  await expect(
    till.getByRole("status").filter({ hasText: "Zakup zapisany" }),
  ).toContainText("Przyznano 90 pkt");
  // The code goes to the server, never the computed discount.
  const bodies = await till.evaluate(
    () => (window as unknown as { __sent: Record<string, unknown>[] }).__sent,
  );
  expect(bodies).toHaveLength(1);
  expect(bodies[0].couponCode).toBe(code);
  expect(bodies[0]).not.toHaveProperty("discount");
  // The form and the preview are cleared for the next customer.
  await expect(till.getByLabel("Kod kuponu")).toHaveValue("");
  await expect(terms).toHaveCount(0);

  await page.reload();
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Wykorzystany");
  await page.goto("/historia");
  // The newest purchase (the earning one from buyCoupon is older).
  const item = page
    .getByTestId("history-item")
    .filter({ hasText: "Tankowanie" })
    .first();
  await expect(item.getByTestId("history-delta")).toHaveText("+90 pkt");
  await expect(item).toContainText("100,00 zł");
  await expect(item.getByTestId("history-discount")).toHaveText(
    "Rabat z kuponu 10,00 zł · zapłacono 90,00 zł",
  );
  await register.context.close();
});

test("used coupon: the preview says so and the purchase is refused without saving", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { card, code } = await activeCoupon(page, staff, baseURL!);
  const register = await openRegister(browser, baseURL!);
  const till = register.page;

  await till.getByLabel("Kod karty klienta").fill(card);
  await fillSale(till, "50", "20");
  await till.getByLabel("Kod kuponu").fill(code);
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByTestId("purchase-result")).toContainText(
    "Rabat z kuponu 4,00 zł",
  );

  await till.getByLabel("Kod karty klienta").fill(card);
  await fillSale(till, "50", "20");
  await till.getByLabel("Kod kuponu").fill(code);
  await expect(till.getByRole("main").getByRole("alert")).toContainText(
    "Ten kupon został już wykorzystany.",
  );
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByRole("main").getByRole("alert").first()).toContainText(
    "Ten kupon został już wykorzystany.",
  );
  await expect(till.getByTestId("purchase-result")).toHaveCount(0);

  await page.goto("/historia");
  await expect(
    page.getByTestId("history-item").filter({ hasText: "Tankowanie" }),
  ).toHaveCount(2); // the earning purchase and the one with the coupon
  await register.context.close();
});

test("lost response of a purchase with a coupon: the retry reuses the key, one redemption", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { card, code, coupon } = await activeCoupon(page, staff, baseURL!);
  const register = await openRegister(browser, baseURL!);
  const till = register.page;
  const keys: string[] = [];
  till.on("request", (r) => {
    if (r.method() === "POST" && r.url().endsWith("/fuel-purchases"))
      keys.push(r.headers()["idempotency-key"]);
  });
  await till.route(PURCHASES, async (route) => {
    await route.fetch();
    await route.abort("failed");
  });

  await till.getByLabel("Kod karty klienta").fill(card);
  await fillSale(till, "100", "40");
  await till.getByLabel("Kod kuponu").fill(code);
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByRole("main").getByRole("alert").first()).toContainText(
    "nie wiadomo, czy zakup został zapisany",
  );
  await expect(till.getByLabel("Kod kuponu")).toBeDisabled();
  await till.unroute(PURCHASES);

  await till.getByRole("button", { name: "Ponów zapis zakupu" }).click();
  await expect(till.getByTestId("purchase-result")).toHaveText(
    "Rabat z kuponu 8,00 zł · do zapłaty 92,00 zł",
  );
  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);

  await page.reload();
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Wykorzystany");
  await page.goto("/historia");
  await expect(page.getByTestId("history-discount")).toHaveCount(1);
  await register.context.close();
});

test("a late calculation answer for an abandoned entry is ignored; 429 is explained", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { card, code } = await activeCoupon(page, staff, baseURL!);
  const register = await openRegister(browser, baseURL!);
  const till = register.page;

  await till.getByLabel("Kod karty klienta").fill(card);
  await fillSale(till, "100", "40");
  await till.getByLabel("Kod kuponu").fill(code);
  await expect(till.getByTestId("coupon-terms")).toBeVisible();

  // The calculation answer is held back; meanwhile the cashier clears the coupon and enters it again.
  let release!: () => void;
  const held = new Promise<void>((resolve) => (release = resolve));
  let calls = 0;
  let lateDelivered = false;
  await till.route(PREVIEW, async (route) => {
    const response = await route.fetch();
    const first = ++calls === 1;
    if (first) await held;
    await route.fulfill({ response });
    if (first) lateDelivered = true;
  });
  await till.getByRole("button", { name: "Przelicz rabat" }).click();
  await till.getByLabel("Kod kuponu").fill("");
  await till.getByLabel("Kod kuponu").fill(code);
  await expect(till.getByTestId("coupon-terms")).toBeVisible();
  release();
  await expect.poll(() => lateDelivered).toBe(true);
  await till.waitForTimeout(500);
  // The old answer belongs to the abandoned entry: no calculation until "Przelicz".
  await expect(till.getByTestId("coupon-calculation")).toHaveCount(0);
  await expect(
    till.getByRole("button", { name: "Przelicz rabat" }),
  ).toBeEnabled();
  await till.unroute(PREVIEW);

  // The per-cashier limit (mocked here: the real one is shared by all tests).
  await till.route(PREVIEW, (route) =>
    route.fulfill({
      status: 429,
      contentType: "application/problem+json",
      headers: { "Retry-After": "30" },
      body: JSON.stringify({ status: 429, code: "rate_limit.exceeded" }),
    }),
  );
  await till.reload();
  await till.getByLabel("Kod karty klienta").fill(card);
  await fillSale(till, "100", "40");
  await till.getByLabel("Kod kuponu").fill(code.toLowerCase());
  await expect(till.getByRole("main").getByRole("alert")).toContainText(
    "Limit podglądów kuponów",
  );
  // Saving still works without a preview.
  await till.unroute(PREVIEW);
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByTestId("purchase-result")).toContainText(
    "Rabat z kuponu 8,00 zł",
  );
  await register.context.close();
});

test("scanning the coupon QR fills only the coupon field and saves nothing", async ({
  page,
  customer,
  staff,
  baseURL,
}, testInfo) => {
  void customer;
  const { card, code } = await activeCoupon(page, staff, baseURL!);
  const video = testInfo.outputPath("coupon-qr.y4m");
  writeQrVideo(video, code);
  const camera = await chromium.launch({
    args: [
      "--use-fake-ui-for-media-stream",
      "--use-fake-device-for-media-stream",
      `--use-file-for-fake-video-capture=${video}`,
    ],
  });
  const register = await openRegister(camera, baseURL!, TRACK_STREAMS);
  const till = register.page;
  let posts = 0;
  till.on(
    "request",
    (r) =>
      r.method() === "POST" && r.url().endsWith("/fuel-purchases") && posts++,
  );

  await till.getByLabel("Kod karty klienta").fill(card);
  // A coupon QR held to the card scanner is ignored: it keeps scanning, the card field is unchanged.
  await till.getByRole("button", { name: "Skanuj kod QR karty" }).click();
  await expect(till.getByText("Skieruj kamerę na kod QR karty")).toBeVisible();
  // Only one scanner may hold the camera.
  await expect(
    till.getByRole("button", { name: "Skanuj kod QR kuponu" }),
  ).toBeDisabled();
  await till.waitForTimeout(2_000);
  await expect(till.getByLabel("Kod karty klienta")).toHaveValue(card);
  await expect(till.getByLabel("Kod kuponu")).toHaveValue("");
  await till.getByRole("button", { name: "Zatrzymaj kamerę" }).click();

  await till.getByRole("button", { name: "Skanuj kod QR kuponu" }).click();
  await expect(till.getByLabel("Kod kuponu")).toHaveValue(code, {
    timeout: 15_000,
  });
  await expect(till.getByLabel("Kod karty klienta")).toHaveValue(card);
  await expect(till.getByTestId("coupon-terms")).toBeVisible();
  expect(posts).toBe(0);
  await camera.close();
});

test("coupon redeemed elsewhere after the preview: the refused save clears the stale preview", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { card, code } = await activeCoupon(page, staff, baseURL!);
  const register = await openRegister(browser, baseURL!);
  const till = register.page;

  await till.getByLabel("Kod karty klienta").fill(card);
  await fillSale(till, "100", "40");
  await till.getByLabel("Kod kuponu").fill(code);
  await till.getByRole("button", { name: "Przelicz rabat" }).click();
  await expect(till.getByTestId("coupon-calculation")).toContainText("8,00 zł");

  // Another register redeems the coupon meanwhile.
  const other = await cashier(baseURL!);
  await other.purchase(card, 50, 20, code);
  await other.dispose();

  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByRole("main").getByRole("alert").first()).toContainText(
    "Ten kupon został już wykorzystany.",
  );
  await expect(till.getByTestId("coupon-calculation")).toHaveCount(0);
  await expect(till.getByTestId("coupon-terms")).toHaveCount(0);
  await expect(till.getByTestId("purchase-result")).toHaveCount(0);
  await register.context.close();
});
