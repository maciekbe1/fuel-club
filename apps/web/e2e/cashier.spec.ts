import { chromium, type Browser, type Page } from "@playwright/test";
import { CASHIER } from "./accounts";
import {
  cameraState,
  DENY_CAMERA,
  HOLD_CAMERA,
  TRACK_STREAMS,
  writeQrVideo,
} from "./camera";
import { expect, loginAs, test } from "./fixtures";

const PURCHASES = "**/api/cashier/fuel-purchases";

async function showCard(page: Page) {
  await page.goto("/karta");
  await page.getByRole("button", { name: "Wydaj kartę" }).click();
  return (await page.getByTestId("card-code").getAttribute("data-code"))!;
}

async function openRegister(
  browser: Browser,
  baseURL: string,
  init?: () => void,
) {
  const context = await browser.newContext({
    baseURL,
    locale: "pl-PL",
    timezoneId: "Europe/Warsaw",
  });
  const page = await context.newPage();
  if (init) await page.addInitScript(init);
  await loginAs(page, CASHIER.email, CASHIER.password);
  await page.getByRole("link", { name: /Kasa/ }).click();
  await expect(page.getByTestId("station-code")).toHaveText(CASHIER.station);
  return { context, page };
}

async function fillPurchase(page: Page, code: string) {
  await page.getByLabel("Kod karty klienta").fill(code);
  await page.getByLabel("Kwota (zł)").fill("123,45");
  await page.getByLabel("Litry").fill("40,5");
  await page
    .getByLabel("Numer paragonu")
    .fill(`E2E/${crypto.randomUUID().slice(0, 8)}`.toUpperCase());
}

test("customer shows the card, cashier registers the purchase, customer sees balance and history", async ({
  page,
  browser,
  customer,
  baseURL,
}) => {
  void customer;
  const code = await showCard(page);
  // Record the exact JSON the app sends (the body is a stream, so read it in the page).
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
  await fillPurchase(register.page, code);
  await register.page.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(
    register.page.getByRole("status").filter({ hasText: "Zakup zapisany" }),
  ).toContainText("Przyznano 123 pkt");

  // Amount and liters are JSON numbers, not the text from the inputs.
  const bodies = await register.page.evaluate(
    () => (window as unknown as { __sent: unknown[] }).__sent,
  );
  expect(bodies).toEqual([
    expect.objectContaining({ cardCode: code, amount: 123.45, liters: 40.5 }),
  ]);
  // Form ready for the next customer.
  await expect(register.page.getByLabel("Kod karty klienta")).toHaveValue("");

  await page.goto("/");
  await expect(page.getByRole("region", { name: "Twoje saldo" })).toContainText(
    "123 pkt",
  );
  await page.goto("/historia");
  const item = page.getByTestId("history-item");
  await expect(item).toHaveCount(1);
  await expect(item).toContainText("+123 pkt");
  await expect(item).toContainText("123,45 zł");
  await expect(item).toContainText("40,5 l");
  await expect(item).toContainText(`Stacja ${CASHIER.station}`);
  await register.context.close();
});

test("lost response: the retry reuses the key and points are awarded once", async ({
  page,
  browser,
  customer,
  baseURL,
}) => {
  void customer;
  const code = await showCard(page);
  const register = await openRegister(browser, baseURL!);

  const keys: string[] = [];
  register.page.on(
    "request",
    (r) =>
      r.method() === "POST" &&
      r.url().endsWith("/fuel-purchases") &&
      keys.push(r.headers()["idempotency-key"]),
  );
  let lose = true;
  await register.page.route(PURCHASES, async (route) => {
    if (!lose) return route.continue();
    await route.fetch();
    await route.abort("failed");
  });

  await fillPurchase(register.page, code);
  await register.page.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(
    register.page
      .getByRole("alert")
      .filter({ hasText: "nie wiadomo, czy zakup został zapisany" }),
  ).toBeVisible();
  // Locked: the retry must send exactly the same purchase.
  await expect(register.page.getByLabel("Kwota (zł)")).toBeDisabled();

  // Unlocking asks first and warns that the purchase may already be saved; "Wróć" keeps the lock.
  await register.page
    .getByRole("button", { name: "Odblokuj formularz…" })
    .click();
  await expect(
    register.page.getByRole("group", { name: "Potwierdzenie odblokowania" }),
  ).toContainText("Zakup mógł już zostać zapisany");
  await register.page.getByRole("button", { name: "Wróć" }).click();
  await expect(register.page.getByLabel("Kwota (zł)")).toBeDisabled();

  lose = false;
  await register.page
    .getByRole("button", { name: "Ponów zapis zakupu" })
    .click();
  await expect(
    register.page.getByRole("status").filter({ hasText: "Zakup zapisany" }),
  ).toContainText("Przyznano 123 pkt");
  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);

  await page.goto("/");
  await expect(page.getByRole("region", { name: "Twoje saldo" })).toContainText(
    "123 pkt",
  );
  await page.goto("/historia");
  await expect(page.getByTestId("history-item")).toHaveCount(1);
  await register.context.close();
});

test("QR scan fills the card code without submitting, then stops the camera", async ({
  page,
  customer,
  baseURL,
}, testInfo) => {
  void customer;
  const code = await showCard(page);
  const video = testInfo.outputPath("card-qr.y4m");
  writeQrVideo(video, code);

  const camera = await chromium.launch({
    args: [
      "--use-fake-ui-for-media-stream",
      "--use-fake-device-for-media-stream",
      `--use-file-for-fake-video-capture=${video}`,
    ],
  });
  const register = await openRegister(camera, baseURL!, TRACK_STREAMS);
  let posts = 0;
  register.page.on(
    "request",
    (r) =>
      r.method() === "POST" && r.url().endsWith("/fuel-purchases") && posts++,
  );

  await register.page.getByRole("button", { name: "Skanuj kod QR" }).click();
  await expect(register.page.getByLabel("Kod karty klienta")).toHaveValue(
    code,
    { timeout: 15_000 },
  );

  expect(await register.page.evaluate(cameraState)).toEqual({
    opened: 1,
    live: 0,
  });
  await expect(
    register.page.getByRole("button", { name: "Skanuj kod QR" }),
  ).toBeVisible();
  expect(posts).toBe(0);
  await expect(register.page.getByText("Zakup zapisany")).toBeHidden();
  await camera.close();
});

test("leaving the register stops the camera", async ({ baseURL }) => {
  const camera = await chromium.launch({
    args: [
      "--use-fake-ui-for-media-stream",
      "--use-fake-device-for-media-stream",
    ],
  });
  const register = await openRegister(camera, baseURL!, TRACK_STREAMS);

  await register.page.getByRole("button", { name: "Skanuj kod QR" }).click();
  await expect(
    register.page.getByText("Skieruj kamerę na kod QR"),
  ).toBeVisible();
  expect(await register.page.evaluate(cameraState)).toEqual({
    opened: 1,
    live: 1,
  });

  await register.page.getByRole("link", { name: "← Start" }).click();
  await register.page.waitForURL("/");
  expect(await register.page.evaluate(cameraState)).toEqual({
    opened: 1,
    live: 0,
  });
  await camera.close();
});

test("camera permission denied: a clear message, pasting still works", async ({
  browser,
  baseURL,
}) => {
  const register = await openRegister(browser, baseURL!, DENY_CAMERA);

  await register.page.getByRole("button", { name: "Skanuj kod QR" }).click();
  await expect(
    register.page
      .getByRole("alert")
      .filter({ hasText: "Brak zgody na użycie kamery" }),
  ).toBeVisible();
  const field = register.page.getByLabel("Kod karty klienta");
  await expect(field).toBeEnabled();
  await field.fill("0ABCDEFGHJKMNPQRSTVWXYZ12");
  await expect(field).toHaveValue("0ABCDEFGHJKMNPQRSTVWXYZ12");
  await register.context.close();
});

test("a customer account has no register", async ({ page, customer }) => {
  void customer;
  await expect(page.getByRole("link", { name: /Kasa/ })).toHaveCount(0);
  await page.goto("/kasa");
  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "nie ma przypisanego stanowiska",
  );
});

test("lost purchase response, then a failed station refresh: the retry is still safe", async ({
  page,
  browser,
  customer,
  baseURL,
}) => {
  void customer;
  const code = await showCard(page);
  const context = await browser.newContext({
    baseURL,
    locale: "pl-PL",
    timezoneId: "Europe/Warsaw",
  });
  const register = await context.newPage();
  await register.clock.install();
  await loginAs(register, CASHIER.email, CASHIER.password);
  await register.goto("/kasa");
  await expect(register.getByTestId("station-code")).toHaveText(
    CASHIER.station,
  );

  const keys: string[] = [];
  register.on(
    "request",
    (r) =>
      r.method() === "POST" &&
      r.url().endsWith("/fuel-purchases") &&
      keys.push(r.headers()["idempotency-key"]),
  );
  let networkDown = true;
  await register.route(PURCHASES, async (route) => {
    if (!networkDown) return route.continue();
    await route.fetch();
    await route.abort("failed");
  });
  await register.route("**/api/cashier/station", (route) =>
    networkDown ? route.abort("failed") : route.continue(),
  );

  await fillPurchase(register, code);
  await register.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(
    register
      .getByRole("alert")
      .filter({ hasText: "nie wiadomo, czy zakup został zapisany" }),
  ).toBeVisible();

  // The station becomes stale and is refetched when the window regains focus; that refetch fails.
  await register.clock.fastForward(60_000);
  await register.evaluate(() =>
    window.dispatchEvent(new Event("visibilitychange")),
  );
  for (let i = 0; i < 3; i++) await register.clock.fastForward(5_000);
  await expect(
    register.getByText("Nie udało się odświeżyć stanowiska"),
  ).toBeVisible();

  networkDown = false;
  await register.getByRole("button", { name: "Ponów zapis zakupu" }).click();
  await expect(
    register.getByRole("status").filter({ hasText: "Zakup zapisany" }),
  ).toContainText("Przyznano 123 pkt");
  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);

  await page.goto("/historia");
  await expect(page.getByTestId("history-item")).toHaveCount(1);
  await context.close();
});

test("stopping while the camera permission is pending: the late stream is stopped at once", async ({
  baseURL,
}) => {
  const camera = await chromium.launch({
    args: [
      "--use-fake-ui-for-media-stream",
      "--use-fake-device-for-media-stream",
    ],
  });
  const register = await openRegister(camera, baseURL!, HOLD_CAMERA);

  await register.page.getByRole("button", { name: "Skanuj kod QR" }).click();
  await expect
    .poll(() =>
      register.page.evaluate(
        () =>
          (window as unknown as { __cameraRequested: boolean })
            .__cameraRequested,
      ),
    )
    .toBe(true);
  await register.page.getByRole("button", { name: "Zatrzymaj kamerę" }).click();
  await register.page.evaluate(() =>
    (window as unknown as { __releaseCamera: () => void }).__releaseCamera(),
  );

  await expect
    .poll(() => register.page.evaluate(cameraState))
    .toEqual({ opened: 1, live: 0 });
  await expect(
    register.page.getByText("Skieruj kamerę na kod QR"),
  ).toBeHidden();
  await expect(
    register.page.getByRole("button", { name: "Skanuj kod QR" }),
  ).toBeVisible();
  await camera.close();
});

test("locking the form stops the scanner and ignores a late scan", async ({
  page,
  customer,
  baseURL,
}, testInfo) => {
  void customer;
  const code = await showCard(page);
  // The camera would show a different card: a late scan must not replace the code being saved.
  const video = testInfo.outputPath("other-card.y4m");
  writeQrVideo(video, "7ZZZZZZZZZZZZZZZZZZZZZZZZZ");
  const camera = await chromium.launch({
    args: [
      "--use-fake-ui-for-media-stream",
      "--use-fake-device-for-media-stream",
      `--use-file-for-fake-video-capture=${video}`,
    ],
  });
  const register = await openRegister(camera, baseURL!, HOLD_CAMERA);

  let release!: () => void;
  const held = new Promise<void>((r) => (release = r));
  await register.page.route(PURCHASES, async (route) => {
    await held;
    await route.continue();
  });

  await fillPurchase(register.page, code);
  await register.page.getByRole("button", { name: "Skanuj kod QR" }).click();
  await register.page.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(register.page.getByLabel("Kod karty klienta")).toBeDisabled();

  // Permission arrives while the purchase is being saved.
  await register.page.evaluate(() =>
    (window as unknown as { __releaseCamera: () => void }).__releaseCamera(),
  );
  await expect
    .poll(() => register.page.evaluate(cameraState))
    .toEqual({ opened: 1, live: 0 });
  await expect(register.page.getByLabel("Kod karty klienta")).toHaveValue(code);

  release();
  await expect(
    register.page.getByRole("status").filter({ hasText: "Zakup zapisany" }),
  ).toContainText("Przyznano 123 pkt");
  await camera.close();
});
