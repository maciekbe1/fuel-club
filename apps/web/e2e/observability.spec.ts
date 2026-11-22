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
});
