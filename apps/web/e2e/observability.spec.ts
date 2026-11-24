import { ADMIN } from "./accounts";
import { expect, loginAs, test } from "./fixtures";

test("admin sees chat metrics and rules, and clears a suspicious turn from the review queue", async ({
  page,
  customer,
  browser,
  baseURL,
}) => {
  // A customer's turn with a rejected tool (a coupon that is not theirs).
  await page.goto("/czat");
  const turnResponse = page.waitForResponse(
    (r) => r.url().endsWith("/api/chat") && r.request().method() === "POST",
  );
  await page
    .getByLabel("Wiadomość do asystenta")
    .fill(`Aktywuj kupon ${crypto.randomUUID()}`);
  await page.getByRole("button", { name: "Wyślij" }).click();
  const correlationId = (await turnResponse).headers()["x-correlation-id"];
  await expect(page.getByTestId("chat-turn")).toHaveAttribute(
    "data-status",
    "done",
  );

  const context = await browser.newContext({ baseURL });
  const admin = await context.newPage();
  await loginAs(admin, ADMIN.email, ADMIN.password);
  await admin.getByRole("link", { name: /Obserwowalność/ }).click();
  await admin.waitForURL("/admin/obserwowalnosc");

  const metrics = admin.getByTestId("chat-metrics");
  await expect(metrics).toContainText("Tury");
  await expect(admin.getByTestId("anomaly-rule")).toHaveCount(8);
  // P16c: conversations counted on their own, demo cost is "not applicable", never zero.
  await expect(admin.getByTestId("conversation-metrics")).toContainText(
    "Odsetek rozmów bez przekazania",
  );
  // The shared E2E database may hold older non-demo turns: narrow to the demo model.
  await admin.getByLabel("Model").selectOption("demo-rules");
  await expect(admin.getByText("nie dotyczy (demo)")).toBeVisible();
  await expect(admin.getByTestId("demo-only")).toContainText(
    "nie jest pomiar wydajności",
  );
  await admin.getByLabel("Model").selectOption("");
  // Synthetic traffic is a separate view; the run list follows it.
  await admin.getByRole("button", { name: "Syntetyczny" }).click();
  await expect(
    admin.getByRole("button", { name: "Syntetyczny" }),
  ).toHaveAttribute("aria-pressed", "true");
  await expect(metrics.getByText("Tury", { exact: true })).toBeVisible();
  await admin.getByRole("button", { name: "Rzeczywisty" }).click();
  await admin.getByRole("button", { name: "1 h" }).click();
  await expect(admin.getByRole("button", { name: "1 h" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );

  const item = admin
    .getByTestId("review-item")
    .filter({ hasText: correlationId });
  await expect(item).toContainText("odrzucone narzędzie");
  await expect(item).toContainText("propose_coupon_activation (rejected)");
  // No identity: the queue shows a pseudonym, never the customer's e-mail.
  expect(await admin.getByRole("main").textContent()).not.toContain(
    customer.email,
  );

  await item.getByRole("button", { name: "Oznacz jako przejrzane" }).click();
  await expect(item).toHaveCount(0);
  await context.close();
});

test("admin goes from a suspicious turn to its whole conversation and back to the list", async ({
  page,
  customer,
  browser,
  baseURL,
}) => {
  await page.goto("/czat");
  const turnResponse = page.waitForResponse(
    (r) => r.url().endsWith("/api/chat") && r.request().method() === "POST",
  );
  await page
    .getByLabel("Wiadomość do asystenta")
    .fill(`Aktywuj kupon ${crypto.randomUUID()}`);
  await page.getByRole("button", { name: "Wyślij" }).click();
  const correlationId = (await turnResponse).headers()["x-correlation-id"];
  await expect(page.getByTestId("chat-turn")).toHaveAttribute(
    "data-status",
    "done",
  );

  const context = await browser.newContext({ baseURL });
  const admin = await context.newPage();
  await loginAs(admin, ADMIN.email, ADMIN.password);
  await admin.goto("/admin/obserwowalnosc");
  const item = admin
    .getByTestId("review-item")
    .filter({ hasText: correlationId });
  await item.getByRole("link", { name: "Cała rozmowa →" }).click();
  await admin.waitForURL(/\/admin\/obserwowalnosc\/rozmowy\/[0-9a-f-]{36}$/);
  const conversationId = admin.url().split("/").pop()!;

  // The trace: the turn with its correlation id and the rejected tool, metadata only.
  const turn = admin
    .getByTestId("turn-trace")
    .filter({ hasText: correlationId });
  await expect(turn).toContainText("propose_coupon_activation · rejected");
  await expect(turn).toContainText("odrzucone narzędzie");
  expect(await admin.getByRole("main").textContent()).not.toContain(
    customer.email,
  );

  // The list, only suspicious ones: the same conversation, a link back to the trace.
  await admin.getByRole("link", { name: "← Rozmowy" }).click();
  await admin.waitForURL("/admin/obserwowalnosc/rozmowy");
  await admin.getByRole("checkbox", { name: "Tylko podejrzane" }).click();
  const row = admin.locator(
    `[data-testid="conversation-row"][data-id="${conversationId}"]`,
  );
  await expect(row).toContainText("odrzucone narzędzie");
  await row.getByRole("link").click();
  await admin.waitForURL(`/admin/obserwowalnosc/rozmowy/${conversationId}`);
  await context.close();
});

test("the observability screen and its API are for admins only", async ({
  page,
  customer,
}) => {
  void customer;
  await page.goto("/admin/obserwowalnosc");
  await page.waitForURL("/");
  expect((await page.request.get("/api/admin/observability")).status()).toBe(
    403,
  );
  expect(
    (await page.request.get("/api/admin/observability/conversations")).status(),
  ).toBe(403);
  await page.goto("/admin/obserwowalnosc/rozmowy");
  await page.waitForURL("/");
});
