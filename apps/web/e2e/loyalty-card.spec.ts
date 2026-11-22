import { expect, test } from "./fixtures";
import { decodeQr } from "./qr";

const ROTATE = "**/api/me/loyalty-card/rotate";
const CODE = /^[0-7][0-9A-HJKMNP-TV-Z]{25}$/;

test("issue, read and rotate the card; a retry after a lost response reuses the key", async ({
  page,
  customer,
}) => {
  void customer;
  const external: string[] = [];
  page.on("request", (request) => {
    if (new URL(request.url()).hostname !== "localhost")
      external.push(request.url());
  });

  await page.getByRole("link", { name: /Karta/ }).click();
  await expect(
    page.getByRole("heading", { name: "Nie masz jeszcze karty" }),
  ).toBeVisible();

  // Issue.
  await page.getByRole("button", { name: "Wydaj kartę" }).click();
  const code = page.getByTestId("card-code");
  await expect(code).toBeVisible();
  const first = (await code.getAttribute("data-code"))!;
  expect(first).toMatch(CODE);

  // Read: a fresh page load shows the same card (GET, not the mutation response).
  await page.reload();
  await expect(code).toHaveAttribute("data-code", first);
  expect(await decodeQr(page.getByTestId("card-qr"))).toBe(first);

  // Rotate, losing the first response after the server has committed it.
  const keys: string[] = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/api/me/loyalty-card/rotate"))
      keys.push(request.headers()["idempotency-key"]);
  });
  let committed: string | undefined;
  await page.route(ROTATE, async (route) => {
    const response = await route.fetch();
    committed = ((await response.json()) as { code: string }).code;
    await route.abort("failed");
  });

  await page.getByRole("button", { name: "Wymień kod" }).click();
  await page.getByRole("button", { name: "Tak, wymień" }).click();
  await expect(
    page.getByRole("region", { name: "Wymiana kodu" }).getByRole("alert"),
  ).toContainText("nie wiemy, czy operacja się udała");
  expect(committed).toMatch(CODE);
  expect(committed).not.toBe(first);

  await page.unroute(ROTATE);
  await page.getByRole("button", { name: "Ponów wymianę" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Nowy kod jest aktywny." }),
  ).toBeVisible();

  // Same key on the retry, and the server replayed the first rotation instead of rotating again.
  expect(keys).toHaveLength(2);
  expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/);
  expect(keys[1]).toBe(keys[0]);
  await expect(code).toHaveAttribute("data-code", committed!);

  expect(await decodeQr(page.getByTestId("card-qr"))).toBe(committed);

  await page.reload();
  await expect(code).toHaveAttribute("data-code", committed!);
  expect(await decodeQr(page.getByTestId("card-qr"))).toBe(committed);

  // The QR is computed in the browser; nothing (in particular the card code) leaves for another host.
  expect(external).toEqual([]);
});

test("an unreadable response is an unknown outcome: retry only, with the same key", async ({
  page,
  customer,
}) => {
  void customer;
  await page.goto("/karta");
  await page.getByRole("button", { name: "Wydaj kartę" }).click();
  const code = page.getByTestId("card-code");
  await expect(code).toBeVisible();

  const keys: string[] = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/api/me/loyalty-card/rotate"))
      keys.push(request.headers()["idempotency-key"]);
  });
  let committed: string | undefined;
  // The server commits, but the browser gets 200 with a body that is not JSON.
  await page.route(ROTATE, async (route) => {
    const response = await route.fetch();
    committed = ((await response.json()) as { code: string }).code;
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: "{",
    });
  });

  await page.getByRole("button", { name: "Wymień kod" }).click();
  await page.getByRole("button", { name: "Tak, wymień" }).click();
  const rotation = page.getByRole("region", { name: "Wymiana kodu" });
  await expect(rotation.getByRole("alert")).toContainText(
    "Nie wiemy, czy operacja się udała",
  );
  await expect(rotation.getByRole("button", { name: "Anuluj" })).toHaveCount(0);

  await page.unroute(ROTATE);
  await rotation.getByRole("button", { name: "Ponów wymianę" }).click();
  await expect(code).toHaveAttribute("data-code", committed!);
  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);
});

test("copying puts the raw code on the clipboard", async ({
  page,
  context,
  customer,
  browserName,
}) => {
  void customer;
  test.skip(
    browserName !== "chromium",
    "clipboard permissions are Chromium-only",
  );
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);

  await page.goto("/karta");
  await page.getByRole("button", { name: "Wydaj kartę" }).click();
  const code = (await page.getByTestId("card-code").getAttribute("data-code"))!;

  await page.getByRole("button", { name: "Kopiuj kod" }).click();
  await expect(page.getByText("Skopiowano")).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(code);
});

test("cancelling the confirmation keeps the card", async ({
  page,
  customer,
}) => {
  void customer;
  let rotations = 0;
  page.on("request", (r) => r.url().endsWith("/rotate") && rotations++);

  await page.goto("/karta");
  await page.getByRole("button", { name: "Wydaj kartę" }).click();
  const code = (await page.getByTestId("card-code").getAttribute("data-code"))!;

  await page.getByRole("button", { name: "Wymień kod" }).click();
  await page.getByRole("button", { name: "Anuluj" }).click();
  await expect(page.getByTestId("card-code")).toHaveAttribute(
    "data-code",
    code,
  );
  expect(rotations).toBe(0);
});

test("home screen links to existing card, history, offers, coupons and assistant pages", async ({
  page,
  customer,
}) => {
  void customer;
  const shortcuts = page.getByRole("navigation", { name: "Na skróty" });
  await expect(shortcuts.getByRole("link")).toHaveCount(5);
  for (const [name, href, heading] of [
    [/Karta/, "/karta", "Twoja karta"],
    [/Historia punktów/, "/historia", "Historia punktów"],
    [/Oferty/, "/oferty", "Oferty"],
    [/Moje kupony/, "/kupony", "Moje kupony i nagrody"],
    [/Asystent/, "/czat", "Asystent"],
  ] as const) {
    await expect(shortcuts.getByRole("link", { name })).toHaveAttribute(
      "href",
      href,
    );
    const response = await page.request.get(href);
    expect(response.status()).toBe(200);
    void heading;
  }
  await expect(page.getByRole("region", { name: "Twoje saldo" })).toContainText(
    "0 pkt",
  );
});

test("lost rotation response followed by a failed card read: the retry still reuses the key", async ({
  page,
  customer,
}) => {
  void customer;
  await page.goto("/karta");
  await page.getByRole("button", { name: "Wydaj kartę" }).click();
  const code = page.getByTestId("card-code");
  await expect(code).toBeVisible();

  const keys: string[] = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/api/me/loyalty-card/rotate"))
      keys.push(request.headers()["idempotency-key"]);
  });

  // Rotation commits on the server but its response is lost; card reads fail until the network is back.
  let networkDown = true;
  let committed: string | undefined;
  await page.route("**/api/me/loyalty-card/rotate", async (route) => {
    if (!networkDown) return route.continue();
    committed = ((await (await route.fetch()).json()) as { code: string }).code;
    await route.abort("failed");
  });
  await page.route("**/api/me/loyalty-card", (route) =>
    networkDown && route.request().method() === "GET"
      ? route.abort("failed")
      : route.continue(),
  );

  await page.getByRole("button", { name: "Wymień kod" }).click();
  await page.getByRole("button", { name: "Tak, wymień" }).click();
  // Wait until the card read has exhausted its retries and failed.
  await expect(page.getByText("Nie udało się odświeżyć karty")).toBeVisible({
    timeout: 15_000,
  });
  // Nothing to cancel with an unknown outcome: only the safe retry is offered.
  await expect(page.getByRole("button", { name: "Anuluj" })).toHaveCount(0);

  networkDown = false;
  await page.getByRole("button", { name: "Ponów wymianę" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Nowy kod jest aktywny." }),
  ).toBeVisible();

  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);
  await expect(code).toHaveAttribute("data-code", committed!);
  expect(await decodeQr(page.getByTestId("card-qr"))).toBe(committed);
});
