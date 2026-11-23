import type { Page } from "@playwright/test";
import { buyCoupon, expect, newCustomer, test } from "./fixtures";

const CONFIRM = "**/api/chat/confirmations";

async function openChat(page: Page) {
  await page.goto("/");
  await page.getByRole("link", { name: /Asystent/ }).click();
  await page.waitForURL("/czat");
}

async function ask(page: Page, message: string) {
  await page.getByLabel("Wiadomość do asystenta").fill(message);
  await page.getByRole("button", { name: "Wyślij" }).click();
  return page.getByTestId("chat-turn").last();
}

async function couponStatus(page: Page, couponId: string) {
  const response = await page.request.get(`/api/me/coupons/${couponId}`);
  return ((await response.json()) as { status: string }).status;
}

test("a rules question is answered from the rules with its source; an uncovered one offers an agent", async ({
  page,
  customer,
}) => {
  void customer;
  await openChat(page);

  const covered = await ask(page, "Czy punkty wygasają?");

  await expect(covered).toHaveAttribute("data-status", "done");
  await expect(covered.getByTestId("chat-answer")).toContainText(
    "Punkty nie wygasają.",
  );
  await expect(covered.getByTestId("chat-answer")).toContainText(
    "Regulamin, §6.1",
  );
  await expect(
    covered.getByRole("list", { name: "Użyte narzędzia" }),
  ).toHaveText("Regulamin: sprawdzono");

  const uncovered = await ask(
    page,
    "Co regulamin mówi o płatności kartą kredytową?",
  );

  await expect(uncovered).toHaveAttribute("data-status", "done");
  await expect(uncovered.getByTestId("chat-answer")).toContainText(
    "Regulamin programu tego nie opisuje",
  );
  await expect(
    uncovered.getByRole("button", { name: "Porozmawiaj z konsultantem" }),
  ).toBeVisible();
});

test("balance question streams an answer with the tool used and the demo label", async ({
  page,
  customer,
}) => {
  void customer;
  await openChat(page);

  const turn = await ask(page, "Ile mam punktów?");

  await expect(turn).toHaveAttribute("data-status", "done");
  await expect(turn.getByTestId("chat-answer")).toContainText("Masz 0 pkt.");
  await expect(turn.getByRole("list", { name: "Użyte narzędzia" })).toHaveText(
    "Saldo punktów: sprawdzono",
  );
  await expect(page.getByTestId("chat-demo")).toBeVisible();
  // Enter sends too; the composer is free again after the answer.
  await page.getByLabel("Wiadomość do asystenta").fill("Jakie są oferty?");
  await page.getByLabel("Wiadomość do asystenta").press("Enter");
  await expect(page.getByTestId("chat-turn")).toHaveCount(2);
  await expect(page.getByTestId("chat-turn").last()).toHaveAttribute(
    "data-status",
    "done",
  );
});

test("activation needs the button: the proposal changes nothing, the button activates", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { couponId, name } = await buyCoupon(page, staff, baseURL!);
  await openChat(page);

  const turn = await ask(page, "Aktywuj mój kupon");
  const card = turn.getByTestId("chat-confirmation");
  await expect(card).toContainText(`Aktywować kupon „${name}”?`);
  await expect(card).toContainText("0,20 zł/l, do 50 l (maks. 10,00 zł)");
  await expect(turn.getByTestId("chat-answer")).toContainText(
    "bez potwierdzenia nic się nie zmieni",
  );
  expect(await couponStatus(page, couponId)).toBe("Purchased");

  await card.getByRole("button", { name: "Aktywuj kupon" }).click();
  await expect(card.getByRole("status")).toContainText("Kupon jest aktywny");
  expect(await couponStatus(page, couponId)).toBe("Active");

  // The code is shown on the coupons screen, never in the chat.
  const stored = await page.request.get(`/api/me/coupons/${couponId}`);
  const code = ((await stored.json()) as { code: string }).code;
  expect(await page.getByRole("log").textContent()).not.toContain(code);
  await card.getByRole("link", { name: "Moje kupony" }).click();
  await expect(
    page
      .getByTestId("coupon")
      .filter({ hasText: name, visible: true })
      .getByTestId("coupon-status"),
  ).toHaveText("Aktywny");
});

test("declining leaves the coupon untouched", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { couponId } = await buyCoupon(page, staff, baseURL!);
  await openChat(page);

  const card = (await ask(page, "aktywuj kupon")).getByTestId(
    "chat-confirmation",
  );
  await card.getByRole("button", { name: "Nie teraz" }).click();

  await expect(card).toContainText("Pominięto - kupon bez zmian.");
  await expect(card.getByRole("button")).toHaveCount(0);
  expect(await couponStatus(page, couponId)).toBe("Purchased");
});

test("lost confirmation response: the retry reuses the key and the coupon is active once", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { couponId } = await buyCoupon(page, staff, baseURL!);
  await openChat(page);
  const card = (await ask(page, "aktywuj kupon")).getByTestId(
    "chat-confirmation",
  );

  const keys: string[] = [];
  let lose = true;
  await page.route(CONFIRM, async (route) => {
    keys.push(route.request().headers()["idempotency-key"]);
    if (!lose) return route.continue();
    lose = false;
    await route.fetch(); // the server activates the coupon…
    await route.abort("failed"); // …and the answer never arrives
  });

  await card.getByRole("button", { name: "Aktywuj kupon" }).click();
  await expect(card.getByRole("alert")).toContainText(
    "nie wiadomo, czy kupon został aktywowany",
  );
  await expect(card.getByRole("button", { name: "Nie teraz" })).toHaveCount(0);
  await card.getByRole("button", { name: "Ponów aktywację" }).click();

  await expect(card.getByRole("status")).toContainText("Kupon jest aktywny");
  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);
  expect(await couponStatus(page, couponId)).toBe("Active");
});

test("someone else's coupon gets no proposal and no data", async ({
  page,
  customer,
  staff,
  baseURL,
  browser,
}) => {
  void customer;
  const { couponId, name } = await buyCoupon(page, staff, baseURL!);

  const context = await browser.newContext({ baseURL });
  const other = await context.newPage();
  await newCustomer(other, "Bartek");
  await openChat(other);
  const turn = await ask(other, `Aktywuj kupon ${couponId}`);

  await expect(turn).toHaveAttribute("data-status", "done");
  await expect(turn.getByRole("list", { name: "Użyte narzędzia" })).toHaveText(
    "Propozycja aktywacji: odrzucono",
  );
  await expect(turn.getByTestId("chat-confirmation")).toHaveCount(0);
  await expect(turn).not.toContainText(name);
  await context.close();
  expect(await couponStatus(page, couponId)).toBe("Purchased");
});

test("a stream split into small pieces is shown as it arrives", async ({
  page,
  customer,
  context,
}) => {
  void customer;
  await context.addCookies([
    { name: "e2e-fault", value: "sse-split", url: "http://localhost:3100" },
  ]);
  await openChat(page);

  const turn = await ask(page, "Ile mam punktów?");

  // The proxy holds the stream before the answer: the tool event is already visible, so nothing
  // between the API and the browser buffers the whole response.
  await expect(turn.getByRole("list", { name: "Użyte narzędzia" })).toHaveText(
    "Saldo punktów: sprawdzono",
  );
  await expect(turn).toHaveAttribute("data-status", "streaming");
  await expect(turn.getByTestId("chat-answer")).toHaveText("Asystent pisze…");
  await expect(page.getByRole("button", { name: "Zatrzymaj" })).toBeVisible();

  await expect(turn).toHaveAttribute("data-status", "done");
  // Split multi-byte characters are decoded intact.
  await expect(turn.getByTestId("chat-answer")).toContainText(
    "Asystent w trybie demo (reguły, bez AI). Masz 0 pkt.",
  );
});

test("a stream cut before done is an error, never a finished answer", async ({
  page,
  customer,
  context,
}) => {
  void customer;
  await context.addCookies([
    { name: "e2e-fault", value: "sse-cut", url: "http://localhost:3100" },
  ]);
  await openChat(page);

  const turn = await ask(page, "Ile mam punktów?");

  await expect(turn).toHaveAttribute("data-status", "error");
  await expect(turn.getByRole("alert")).toContainText(
    "Połączenie zostało przerwane",
  );
  await expect(page.getByRole("button", { name: "Wyślij" })).toBeVisible();
});

test("an error event ends the turn without success", async ({
  page,
  customer,
}) => {
  void customer;
  await page.route("**/api/chat", (route) =>
    route.fulfill({
      status: 200,
      contentType: "text/event-stream",
      body:
        'event: meta\ndata: {"correlationId":"x","provider":"demo","model":"m","promptVersion":"v","demo":true}\n\n' +
        'event: delta\ndata: {"text":"Częściowa odpo"}\n\n' +
        'event: error\ndata: {"code":"assistant.timeout","message":"Odpowiedź trwała za długo. Spróbuj ponownie."}\n\n',
    }),
  );
  await openChat(page);

  const turn = await ask(page, "saldo");

  await expect(turn).toHaveAttribute("data-status", "error");
  await expect(turn.getByRole("alert")).toHaveText(
    "Odpowiedź trwała za długo. Spróbuj ponownie.",
  );
});

test("an answer is markdown without images or links, and keeps line breaks", async ({
  page,
  customer,
}) => {
  void customer;
  let fetched = false;
  await page.route("https://tracker.example/**", (route) => {
    fetched = true;
    return route.abort();
  });
  const text =
    "**Saldo**: 120 pkt\nKupony: 2\n![x](https://tracker.example/p.png?b=120) [strona](https://tracker.example/x)";
  await page.route("**/api/chat", (route) =>
    route.fulfill({
      status: 200,
      contentType: "text/event-stream",
      body:
        'event: meta\ndata: {"correlationId":"x","provider":"demo","model":"m","promptVersion":"v","demo":true}\n\n' +
        `event: delta\ndata: ${JSON.stringify({ text })}\n\n` +
        'event: done\ndata: {"correlationId":"x"}\n\n',
    }),
  );
  await openChat(page);

  const turn = await ask(page, "saldo");

  await expect(turn).toHaveAttribute("data-status", "done");
  const answer = turn.getByTestId("chat-answer");
  await expect(answer.locator("[data-streamdown=strong]")).toHaveText("Saldo");
  await expect(answer.locator("br")).toHaveCount(2);
  await expect(answer.locator("img")).toHaveCount(0);
  await expect(answer.locator("a")).toHaveCount(0);
  await expect(answer).toContainText("strona");
  expect(fetched).toBe(false);
});

test("stop cancels the turn and frees the composer", async ({
  page,
  customer,
}) => {
  void customer;
  await page.route("**/api/chat", async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 10_000));
    await route.abort().catch(() => {});
  });
  // The page's own fetch signal: Playwright's requestfailed for a held route is not reliable.
  await page.addInitScript(() => {
    const original = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      const signal =
        init?.signal ?? (input instanceof Request ? input.signal : undefined);
      const mark = () => {
        (window as unknown as { __chatAborted: boolean }).__chatAborted = true;
      };
      // Already aborted (stop before the request left) fires no event: check it too.
      if (url.endsWith("/api/chat") && signal) {
        if (signal.aborted) mark();
        else signal.addEventListener("abort", mark);
      }
      return original(input, init);
    };
  });
  await openChat(page);

  // Stop once the chat request has left (a CSRF token request goes first).
  const sent = page.waitForRequest("**/api/chat");
  const turn = await ask(page, "saldo");
  await expect(turn).toHaveAttribute("data-status", "streaming");
  await sent;
  await page.getByRole("button", { name: "Zatrzymaj" }).click();

  await expect(turn).toHaveAttribute("data-status", "cancelled");
  await expect(turn).toContainText("Zatrzymano");
  await expect(page.getByRole("button", { name: "Wyślij" })).toBeVisible();
  await expect
    .poll(() =>
      page.evaluate(
        () => (window as unknown as { __chatAborted?: boolean }).__chatAborted,
      ),
    )
    .toBe(true);
});

test("a stream that ends cleanly without done is not a finished answer", async ({
  page,
  customer,
}) => {
  void customer;
  await page.route("**/api/chat", (route) =>
    route.fulfill({
      status: 200,
      contentType: "text/event-stream",
      body:
        'event: meta\ndata: {"correlationId":"x","provider":"demo","model":"m","promptVersion":"v","demo":true}\n\n' +
        'event: delta\ndata: {"text":"Masz 10"}\n\n',
    }),
  );
  await openChat(page);

  const turn = await ask(page, "saldo");

  await expect(turn).toHaveAttribute("data-status", "error");
  await expect(turn.getByTestId("chat-answer")).toHaveText("Masz 10");
  await expect(turn.getByRole("alert")).toContainText(
    "odpowiedź jest niepełna",
  );
});

test("the proposal can be confirmed only after the answer has finished", async ({
  page,
  customer,
  staff,
  baseURL,
  context,
}) => {
  void customer;
  const { couponId } = await buyCoupon(page, staff, baseURL!);
  await context.addCookies([
    { name: "e2e-fault", value: "sse-split", url: "http://localhost:3100" },
  ]);
  await openChat(page);

  const turn = await ask(page, "aktywuj kupon");
  const card = turn.getByTestId("chat-confirmation");

  // The proxy holds the stream after the confirmation event, before the answer text.
  await expect(
    card.getByRole("button", { name: "Czekam na koniec odpowiedzi…" }),
  ).toBeDisabled();
  await expect(card.getByRole("button", { name: "Nie teraz" })).toBeDisabled();
  await expect(card).not.toContainText("zapytaj ponownie");

  await expect(turn).toHaveAttribute("data-status", "done");
  await card.getByRole("button", { name: "Aktywuj kupon" }).click();
  await expect(card.getByRole("status")).toContainText("Kupon jest aktywny");
  expect(await couponStatus(page, couponId)).toBe("Active");
});

test("a suggestion keeps the typed draft", async ({ page, customer }) => {
  void customer;
  await openChat(page);
  const box = page.getByLabel("Wiadomość do asystenta");
  await box.fill("moje pytanie");

  await page.getByRole("button", { name: "Ile mam punktów?" }).click();

  await expect(page.getByTestId("chat-turn")).toHaveAttribute(
    "data-status",
    "done",
  );
  await expect(box).toHaveValue("moje pytanie");
});

test("coupon comparison and points explanation come from the server tools", async ({
  page,
  customer,
}) => {
  void customer;
  await openChat(page);

  await page
    .getByRole("button", {
      name: "Tankuję 40 l za 250 zł - który kupon wybrać?",
    })
    .click();
  const compare = page.getByTestId("chat-turn").last();
  await expect(compare).toHaveAttribute("data-status", "done");
  await expect(
    compare.getByRole("list", { name: "Użyte narzędzia" }),
  ).toHaveText("Porównanie kuponów: sprawdzono");
  await expect(compare.getByTestId("chat-answer")).toContainText(
    "Kolejność: najwyższy rabat w zł",
  );

  await page
    .getByLabel("Wiadomość do asystenta")
    .fill("Dlaczego dostałem tyle punktów?");
  await page.getByRole("button", { name: "Wyślij" }).click();
  const explain = page.getByTestId("chat-turn").last();
  await expect(explain).toHaveAttribute("data-status", "done");
  await expect(
    explain.getByRole("list", { name: "Użyte narzędzia" }),
  ).toHaveText("Rachunek punktów: sprawdzono");
});
