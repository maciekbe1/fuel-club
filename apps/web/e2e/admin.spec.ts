import type { Browser, Page } from "@playwright/test";
import { ADMIN, CASHIER } from "./accounts";
import { expect, loginAs, offerName, test } from "./fixtures";

const ADMIN_REWARDS = "**/api/admin/rewards";
const ADMIN_REWARD = "**/api/admin/rewards/*";

async function adminPage(browser: Browser, baseURL: string) {
  const context = await browser.newContext({ baseURL, locale: "pl-PL" });
  const page = await context.newPage();
  await loginAs(page, ADMIN.email, ADMIN.password);
  return { context, page };
}

async function fillOffer(
  page: Page,
  values: { name: string; cost: string; rate: string; liters: string },
) {
  await page.getByLabel("Nazwa").fill(values.name);
  await page.getByLabel("Koszt (pkt)").fill(values.cost);
  await page.getByLabel("Rabat (zł/l)").fill(values.rate);
  await page.getByLabel("Limit litrów").fill(values.liters);
}

/** Creates an offer through the form and opens its edit page. */
async function createOffer(page: Page, name: string) {
  await page.goto("/admin/oferty/nowa");
  await fillOffer(page, { name, cost: "300", rate: "0,15", liters: "40" });
  await page.getByRole("button", { name: "Utwórz ofertę" }).click();
  await page.waitForURL("/admin/oferty");
  const row = page.getByTestId("admin-offer").filter({ hasText: name });
  await expect(row).toContainText("300 pkt");
  await row.getByRole("link", { name: `Edytuj ofertę ${name}` }).click();
  await expect(page.getByText("Wersja 1.")).toBeVisible();
}

/** Deactivates the offer through the API so the shared e2e catalog does not grow. */
async function retire(page: Page, name: string) {
  const list = await page.request.get("/api/admin/rewards");
  const items = (
    (await list.json()) as {
      items: {
        id: string;
        name: string;
        version: number;
        description: string;
        pointsCost: number;
        discountPerLiter: number;
        maxLiters: number;
        isActive: boolean;
      }[];
    }
  ).items.filter((item) => item.name === name && item.isActive);
  const csrf = (await (await page.request.get("/api/auth/csrf")).json()) as {
    token: string;
  };
  for (const item of items)
    await page.request.put(`/api/admin/rewards/${item.id}`, {
      headers: { "X-CSRF-TOKEN": csrf.token },
      data: { ...item, isActive: false },
    });
}

test("admin creates an offer, customers see it, deactivating hides it", async ({
  page,
  browser,
  customer,
  baseURL,
}) => {
  void customer;
  const admin = await adminPage(browser, baseURL!);
  await admin.page.getByRole("link", { name: /Administracja ofert/ }).click();
  await admin.page.waitForURL("/admin/oferty");
  const name = offerName("Admin");

  // Client-side validation mirrors the server's precision rule.
  await admin.page.getByRole("link", { name: "Nowa oferta" }).click();
  await fillOffer(admin.page, {
    name,
    cost: "300",
    rate: "0,205",
    liters: "40",
  });
  await admin.page.getByRole("button", { name: "Utwórz ofertę" }).click();
  await expect(
    admin.page.getByText("maks. 2 miejsca po przecinku"),
  ).toBeVisible();
  await admin.page.getByLabel("Rabat (zł/l)").fill("0,15");
  await admin.page.getByRole("button", { name: "Utwórz ofertę" }).click();
  await admin.page.waitForURL("/admin/oferty");
  const row = admin.page.getByTestId("admin-offer").filter({ hasText: name });
  await expect(row.getByTestId("admin-offer-status")).toHaveText("Aktywna");
  await expect(row).toContainText("0,15 zł/l, do 40 l (maks. 6,00 zł)");

  await page.goto("/oferty");
  await expect(page.getByTestId("offer").filter({ hasText: name })).toHaveCount(
    1,
  );

  await row.getByRole("link", { name: `Edytuj ofertę ${name}` }).click();
  await admin.page
    .getByLabel("Aktywna (widoczna w katalogu klientów)")
    // Next keeps the previously visited "Nowa oferta" screen hidden in the DOM.
    .filter({ visible: true })
    .uncheck();
  await admin.page.getByRole("button", { name: "Zapisz zmiany" }).click();
  await expect(admin.page.getByRole("status")).toContainText(
    "Zapisano (wersja 2)",
  );
  await admin.page.getByRole("link", { name: "Wróć do listy" }).click();
  await expect(row.getByTestId("admin-offer-status")).toHaveText("Wyłączona");

  await page.reload();
  await expect(page.getByTestId("offer").filter({ hasText: name })).toHaveCount(
    0,
  );
  await admin.context.close();
});

test("two admins edit the same offer: the second gets a conflict, keeps the form and decides", async ({
  browser,
  baseURL,
}) => {
  const first = await adminPage(browser, baseURL!);
  const second = await adminPage(browser, baseURL!);
  const name = offerName("Konflikt");
  await createOffer(first.page, name);
  await second.page.goto(first.page.url());
  await expect(second.page.getByText("Wersja 1.")).toBeVisible();

  await first.page.getByLabel("Koszt (pkt)").fill("350");
  await first.page.getByRole("button", { name: "Zapisz zmiany" }).click();
  await expect(first.page.getByRole("status")).toContainText(
    "Zapisano (wersja 2)",
  );

  // The second form was loaded with version 1.
  await second.page.getByLabel("Koszt (pkt)").fill("400");
  await second.page.getByRole("button", { name: "Zapisz zmiany" }).click();
  const conflict = second.page.getByTestId("offer-conflict");
  await expect(conflict).toContainText("zmieniona w międzyczasie");
  await expect(conflict.getByTestId("offer-current")).toContainText("350 pkt");
  await expect(second.page.getByLabel("Koszt (pkt)")).toHaveValue("400");
  await expect(second.page.getByLabel("Koszt (pkt)")).toBeDisabled();

  // Reloading shows the stored values; saving on top of them is a conscious decision.
  await conflict
    .getByRole("button", { name: "Wczytaj aktualną wersję" })
    .click();
  await expect(second.page.getByLabel("Koszt (pkt)")).toHaveValue("350");
  await expect(second.page.getByText("Wersja 2.")).toBeVisible();
  await second.page.getByLabel("Koszt (pkt)").fill("400");
  await second.page.getByRole("button", { name: "Zapisz zmiany" }).click();
  await expect(second.page.getByRole("status")).toContainText(
    "Zapisano (wersja 3)",
  );

  // The first admin is now behind and overwrites deliberately on version 3.
  await first.page.getByLabel("Limit litrów").fill("45");
  await first.page.getByRole("button", { name: "Zapisz zmiany" }).click();
  const behind = first.page.getByTestId("offer-conflict");
  await expect(behind.getByTestId("offer-current")).toContainText("400 pkt");
  await behind
    .getByRole("button", { name: "Zapisz moje zmiany na wersji 3" })
    .click();
  await expect(first.page.getByRole("status")).toContainText(
    "Zapisano (wersja 4)",
  );
  // The first admin's form still had 350 points: the deliberate overwrite stores them.
  await expect(first.page.getByLabel("Koszt (pkt)")).toHaveValue("350");

  await retire(first.page, name);
  await first.context.close();
  await second.context.close();
});

test("lost PUT response: the outcome is checked, never retried blindly", async ({
  browser,
  baseURL,
}) => {
  const admin = await adminPage(browser, baseURL!);
  const name = offerName("Nieznany");
  await createOffer(admin.page, name);
  let puts = 0;
  admin.page.on("request", (r) => r.method() === "PUT" && puts++);
  await admin.page.route(ADMIN_REWARD, async (route) => {
    if (route.request().method() !== "PUT") return route.fallback();
    await route.fetch();
    await route.abort("failed");
  });

  await admin.page.getByLabel("Koszt (pkt)").fill("320");
  await admin.page.getByRole("button", { name: "Zapisz zmiany" }).click();
  await expect(admin.page.getByRole("main").getByRole("alert")).toContainText(
    "Wynik zapisu nieznany",
  );
  await expect(admin.page.getByLabel("Koszt (pkt)")).toBeDisabled();
  await admin.page.unroute(ADMIN_REWARD);

  await admin.page
    .getByRole("button", { name: "Sprawdź aktualny stan" })
    .click();
  await expect(admin.page.getByRole("status")).toContainText(
    "Zapisano (wersja 2)",
  );
  await expect(admin.page.getByLabel("Koszt (pkt)")).toBeEnabled();
  expect(puts).toBe(1);

  await retire(admin.page, name);
  await admin.context.close();
});

test("lost create response: the retry reuses the key, one offer", async ({
  browser,
  baseURL,
}) => {
  const admin = await adminPage(browser, baseURL!);
  const name = offerName("Utworzenie");
  const keys: string[] = [];
  admin.page.on("request", (r) => {
    if (r.method() === "POST" && r.url().endsWith("/api/admin/rewards"))
      keys.push(r.headers()["idempotency-key"]);
  });
  await admin.page.route(ADMIN_REWARDS, async (route) => {
    if (route.request().method() !== "POST") return route.fallback();
    await route.fetch();
    await route.abort("failed");
  });

  await admin.page.goto("/admin/oferty/nowa");
  await fillOffer(admin.page, { name, cost: "300", rate: "0,1", liters: "30" });
  await admin.page.getByRole("button", { name: "Utwórz ofertę" }).click();
  await expect(admin.page.getByRole("main").getByRole("alert")).toContainText(
    "nie wiadomo, czy oferta została utworzona",
  );
  await expect(admin.page.getByLabel("Nazwa")).toBeDisabled();
  await admin.page.unroute(ADMIN_REWARDS);
  // Leaving and reloading keeps the unfinished create (and its key), and the form locked.
  await admin.page.getByRole("link", { name: "← Oferty" }).click();
  await admin.page.waitForURL("/admin/oferty");
  await admin.page.goto("/admin/oferty/nowa");
  const banner = admin.page.getByRole("main").getByRole("alert");
  await expect(banner).toContainText(`Tworzenie oferty „${name}”`);
  await expect(
    admin.page.getByLabel("Nazwa").filter({ visible: true }),
  ).toBeDisabled();
  await banner.getByRole("button", { name: "Ponów utworzenie" }).click();
  await admin.page.waitForURL("/admin/oferty");

  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);
  await expect(
    admin.page.getByTestId("admin-offer").filter({ hasText: name }),
  ).toHaveCount(1);
  await retire(admin.page, name);
  await admin.context.close();
});

test("customer and cashier cannot open the admin screens", async ({
  page,
  browser,
  customer,
  baseURL,
}) => {
  void customer;
  await page.goto("/admin/oferty");
  await page.waitForURL("/");
  await expect(page.getByRole("link", { name: /Administracja/ })).toHaveCount(
    0,
  );
  expect((await page.request.get("/api/admin/rewards")).status()).toBe(403);

  const context = await browser.newContext({ baseURL });
  const till = await context.newPage();
  await loginAs(till, CASHIER.email, CASHIER.password);
  await expect(till.getByRole("link", { name: /Administracja/ })).toHaveCount(
    0,
  );
  await till.goto("/admin/oferty/nowa");
  await till.waitForURL("/");
  expect((await till.request.get("/api/admin/rewards")).status()).toBe(403);
  await context.close();
});

test("restored create fails again: the retry sends the stored offer, not the empty form", async ({
  browser,
  baseURL,
}) => {
  const admin = await adminPage(browser, baseURL!);
  const name = offerName("Ponowienie");
  const keys: string[] = [];
  admin.page.on("request", (r) => {
    if (r.method() === "POST" && r.url().endsWith("/api/admin/rewards"))
      keys.push(r.headers()["idempotency-key"]);
  });
  const lose = async (route: import("@playwright/test").Route) => {
    if (route.request().method() !== "POST") return route.fallback();
    await route.fetch();
    await route.abort("failed");
  };
  await admin.page.route(ADMIN_REWARDS, lose);

  await admin.page.goto("/admin/oferty/nowa");
  await fillOffer(admin.page, { name, cost: "300", rate: "0,1", liters: "30" });
  await admin.page.getByRole("button", { name: "Utwórz ofertę" }).click();
  const banner = admin.page.getByRole("main").getByRole("alert");
  await expect(banner).toContainText(
    "nie wiadomo, czy oferta została utworzona",
  );

  // Reload: the form is empty, the create comes back from storage; its retry is lost too.
  await admin.page.reload();
  await expect(banner).toContainText(`Tworzenie oferty „${name}”`);
  await banner.getByRole("button", { name: "Ponów utworzenie" }).click();
  await expect(banner).toContainText(
    "nie wiadomo, czy oferta została utworzona",
  );

  await admin.page.unroute(ADMIN_REWARDS);
  await banner.getByRole("button", { name: "Ponów utworzenie" }).click();
  await admin.page.waitForURL("/admin/oferty");

  expect(keys).toHaveLength(3);
  expect(new Set(keys).size).toBe(1);
  await expect(
    admin.page.getByTestId("admin-offer").filter({ hasText: name }),
  ).toHaveCount(1);
  await retire(admin.page, name);
  await admin.context.close();
});
