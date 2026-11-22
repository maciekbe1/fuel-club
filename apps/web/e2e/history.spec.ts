import { cashier, expect, issueCard, newCustomer, test } from "./fixtures";

test("pages through the history with 'Pokaż więcej' without gaps or duplicates", async ({
  page,
  customer,
  baseURL,
}) => {
  void customer;
  const code = await issueCard(page);
  const pos = await cashier(baseURL!);
  // 25 purchases, amounts 10..34 zł -> deltas 10..34, written in this order.
  for (let i = 0; i < 25; i++) await pos.purchase(code, 10 + i + 0.5, 12.345);
  await pos.dispose();

  await page.getByRole("link", { name: /Historia punktów/ }).click();
  const items = page.getByTestId("history-item");
  await expect(items).toHaveCount(20);

  const newest = items.first();
  await expect(newest.getByTestId("history-delta")).toHaveText("+34 pkt");
  await expect(newest).toContainText("Tankowanie");
  await expect(newest).toContainText("Stacja E2E-01");
  await expect(newest).toContainText("34,50 zł");
  await expect(newest).toContainText("12,345 l");
  await expect(newest).toContainText("Saldo po: 550 pkt"); // 10 + 11 + ... + 34

  await page.getByRole("button", { name: "Pokaż więcej" }).click();
  await expect(items).toHaveCount(25);
  await expect(page.getByRole("button", { name: "Pokaż więcej" })).toBeHidden();

  const deltas = await page.getByTestId("history-delta").allTextContents();
  expect(deltas).toEqual(
    Array.from({ length: 25 }, (_, i) => `+${34 - i} pkt`),
  );
  const ids = await items.evaluateAll((els) =>
    els.map((el) => el.getAttribute("data-id")),
  );
  expect(new Set(ids).size).toBe(25);
  await expect(items.last()).toContainText("Saldo po: 10 pkt");
});

test("two customers see only their own history", async ({
  page,
  browser,
  customer,
  baseURL,
}) => {
  void customer;
  const otherContext = await browser.newContext({ baseURL });
  const other = await otherContext.newPage();
  await newCustomer(other, "Bartek");

  const pos = await cashier(baseURL!);
  await pos.purchase(await issueCard(page), 111);
  await pos.purchase(await issueCard(other), 222);
  await pos.dispose();

  await page.goto("/historia");
  await other.goto("/historia");
  await expect(page.getByTestId("history-delta")).toHaveText(["+111 pkt"]);
  await expect(other.getByTestId("history-delta")).toHaveText(["+222 pkt"]);

  await page.goto("/");
  await expect(page.getByRole("region", { name: "Twoje saldo" })).toContainText(
    "111 pkt",
  );
  await otherContext.close();
});

test("empty history, also after a purchase worth 0 points", async ({
  page,
  customer,
  baseURL,
}) => {
  void customer;
  const pos = await cashier(baseURL!);
  await pos.purchase(await issueCard(page), 0.99, 0.5);
  await pos.dispose();

  await page.goto("/historia");
  await expect(
    page.getByRole("heading", { name: "Nie masz jeszcze żadnych operacji" }),
  ).toBeVisible();
  await expect(page.getByRole("link", { name: "Pokaż kartę" })).toHaveAttribute(
    "href",
    "/karta",
  );
});

test("loading and error states, then recovery", async ({ page, customer }) => {
  void customer;
  // One handler: the first request is held (loading state), then every request fails until `healthy`.
  let release!: () => void;
  let healthy = false;
  const held = new Promise<void>((intercepted) => {
    let first = true;
    void page.route("**/api/me/transactions**", async (route) => {
      if (healthy) return route.continue();
      if (first) {
        first = false;
        await new Promise<void>((r) => {
          release = r;
          intercepted();
        });
      }
      await route.abort("failed");
    });
  });

  await page.goto("/historia");
  await held;
  await expect(page.getByText("Ładowanie historii…")).toBeAttached();
  release();

  const alert = page.getByRole("main").getByRole("alert");
  await expect(alert).toContainText("Brak połączenia z serwerem", {
    timeout: 15_000,
  });

  healthy = true;
  await page.getByRole("button", { name: "Spróbuj ponownie" }).click();
  await expect(
    page.getByRole("heading", { name: "Nie masz jeszcze żadnych operacji" }),
  ).toBeVisible();
});
