import { devices, type Browser, type Page } from "@playwright/test";
import path from "node:path";
import { ADMIN, SUPPORT } from "./accounts";
import { expect, loginAs, newCustomer, offerName, test } from "./fixtures";
import { openRegister } from "./register";

/**
 * Not a test of behaviour: walks the demo scenario and saves screenshots for a presentation.
 * Run only on demand: npm run e2e:screenshots (the default E2E run ignores this file).
 * Output: <repo>/.presentation/screenshots (git-ignored).
 */
const OUTPUT_DIR = path.resolve(
  __dirname,
  "../../../.presentation/screenshots",
);

const shot = (page: Page, name: string) =>
  page.screenshot({
    path: path.join(OUTPUT_DIR, `${name}.png`),
    fullPage: true,
  });

const openPhone = async (browser: Browser, baseURL: string) => {
  const context = await browser.newContext({
    ...devices["Pixel 7"],
    baseURL,
    locale: "pl-PL",
    timezoneId: "Europe/Warsaw",
  });
  return { context, page: await context.newPage() };
};

const askAssistant = async (page: Page, text: string) => {
  await page.getByLabel("Wiadomość do asystenta").fill(text);
  await page.getByRole("button", { name: "Wyślij" }).click();
  const turn = page.getByTestId("chat-turn").last();
  await expect(turn).toHaveAttribute("data-status", "done");
  return turn;
};

const fillSale = async (
  till: Page,
  card: string,
  amount: string,
  liters: string,
  coupon?: string,
) => {
  await till.getByLabel("Kod karty klienta").fill(card);
  await till.getByLabel("Kwota (zł)").fill(amount);
  await till.getByLabel("Litry").fill(liters);
  await till
    .getByLabel("Numer paragonu")
    .fill(`DEMO/${crypto.randomUUID().slice(0, 8)}`.toUpperCase());
  if (coupon) await till.getByLabel("Kod kuponu").fill(coupon);
};

test("presentation screenshots", async ({
  browser,
  staff,
  baseURL,
}, testInfo) => {
  test.skip(
    testInfo.project.name !== "desktop",
    "Screenshots are taken once, from the desktop project.",
  );
  test.setTimeout(180_000);

  const offer = offerName("Rabat 20 gr/l");
  await staff.createOffer({
    name: offer,
    pointsCost: 500,
    discountPerLiter: 0.2,
    maxLiters: 50,
  });

  const phone = await openPhone(browser, baseURL!);
  const customer = phone.page;
  await newCustomer(customer, "Anna");

  await customer.goto("/karta");
  await customer.getByRole("button", { name: "Wydaj kartę" }).click();
  const card = (await customer
    .getByTestId("card-code")
    .getAttribute("data-code"))!;
  await shot(customer, "02-karta-qr");

  const register = await openRegister(browser, baseURL!);
  const till = register.page;
  await fillSale(till, card, "600", "100");
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(
    till.getByRole("status").filter({ hasText: "Zakup zapisany" }),
  ).toBeVisible();
  await shot(till, "03-kasa-zakup");

  await customer.goto("/");
  await expect(
    customer.getByRole("region", { name: "Twoje saldo" }),
  ).toContainText("600 pkt");
  await shot(customer, "01-start-saldo");

  await customer.goto("/oferty");
  const offerCard = customer.getByTestId("offer").filter({ hasText: offer });
  await offerCard.getByRole("button", { name: "Wymień punkty" }).click();
  await shot(customer, "04-oferty-potwierdzenie-wymiany");
  await offerCard
    .getByRole("group", { name: "Potwierdzenie wymiany" })
    .getByRole("button", { name: "Potwierdź wymianę" })
    .click();
  await expect(
    customer.getByRole("status").filter({ hasText: "Kupiono kupon" }),
  ).toBeVisible();

  await customer.goto("/czat");
  await askAssistant(customer, "Ile mam punktów?");
  await askAssistant(customer, "Aktywuj mój kupon");
  const confirmation = customer.getByTestId("chat-confirmation");
  await expect(confirmation).toBeVisible();
  await shot(customer, "05-czat-propozycja-aktywacji");
  await confirmation.getByRole("button", { name: "Aktywuj kupon" }).click();
  await expect(confirmation.getByRole("status")).toContainText(
    "Kupon jest aktywny",
  );
  await shot(customer, "06-czat-kupon-aktywny");

  await confirmation.getByRole("link", { name: "Moje kupony" }).click();
  const coupon = customer
    .getByTestId("coupon")
    .filter({ hasText: offer, visible: true });
  const code = (await coupon
    .getByTestId("coupon-code")
    .getAttribute("data-code"))!;
  await shot(customer, "07-kupony-qr");

  await fillSale(till, card, "100", "40", code);
  await expect(till.getByTestId("coupon-terms")).toContainText(offer);
  await till.getByRole("button", { name: "Przelicz rabat" }).click();
  await expect(till.getByTestId("coupon-calculation")).toContainText("8,00 zł");
  await shot(till, "08-kasa-podglad-rabatu");
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByTestId("purchase-result")).toBeVisible();
  await fillSale(till, card, "50", "20", code);
  await expect(till.getByRole("main").getByRole("alert")).toContainText(
    "już wykorzystany",
  );
  await shot(till, "09-kasa-kupon-odrzucony");

  await customer.goto("/czat");
  await askAssistant(customer, "Czy punkty wygasają?");
  await shot(customer, "10-czat-regulamin-zrodlo");
  await askAssistant(
    customer,
    "Co regulamin mówi o płatności kartą kredytową?",
  );
  await askAssistant(customer, `Aktywuj kupon ${crypto.randomUUID()}`);
  await shot(customer, "11-czat-brak-w-regulaminie-i-cudzy-kupon");

  await askAssistant(customer, "Chcę porozmawiać z konsultantem");
  await customer
    .getByTestId("chat-handoff")
    .last()
    .getByRole("button", { name: "Porozmawiaj z konsultantem" })
    .click();
  const start = customer.getByTestId("support-start");
  await expect(start).toContainText("przechowywana 30 dni");
  await start
    .getByLabel("W czym możemy pomóc?")
    .fill("Kasjer nie naliczył mi punktów za wczorajsze tankowanie");
  await shot(customer, "12-handoff-zgoda");
  await start.getByRole("button", { name: "Połącz z konsultantem" }).click();
  await expect(customer.getByTestId("support-status")).toHaveText(
    "Czekasz na konsultanta",
  );

  const agentContext = await browser.newContext({ baseURL });
  const agent = await agentContext.newPage();
  await loginAs(agent, SUPPORT.email, SUPPORT.password);
  await agent.goto("/konsultant");
  await agent
    .getByTestId("support-queue-item")
    .filter({ hasText: "Anna" })
    .first()
    .getByRole("button", { name: "Przejmij" })
    .click();
  await agent
    .getByLabel("Odpowiedź dla klienta")
    .fill("Dzień dobry, sprawdzam paragon. Proszę o chwilę.");
  await agent.getByRole("button", { name: "Wyślij" }).click();
  await expect(customer.getByTestId("support-conversation")).toContainText(
    "sprawdzam paragon",
  );
  await shot(agent, "13-konsultant");
  await shot(customer, "14-klient-rozmowa-z-konsultantem");

  await customer.goto("/historia");
  await expect(customer.getByTestId("history-item").first()).toBeVisible();
  await shot(customer, "15-historia");

  const adminContext = await browser.newContext({ baseURL });
  const adminPage = await adminContext.newPage();
  await loginAs(adminPage, ADMIN.email, ADMIN.password);
  await adminPage.goto("/admin/obserwowalnosc");
  await expect(adminPage.getByTestId("anomaly-rule")).toHaveCount(8);
  await shot(adminPage, "16-admin-obserwowalnosc");

  await Promise.all([
    phone.context.close(),
    register.context.close(),
    agentContext.close(),
    adminContext.close(),
  ]);
});
