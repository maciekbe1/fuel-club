import type { Page } from "@playwright/test";
import { expect, offerName, test } from "./fixtures";
import { openRegister } from "./register";

/** One sale at the till, everything typed into the form like a cashier would. */
async function sell(
  till: Page,
  card: string,
  amount: string,
  liters: string,
  coupon?: string,
) {
  await till.getByLabel("Kod karty klienta").fill(card);
  await till.getByLabel("Kwota (zł)").fill(amount);
  await till.getByLabel("Litry").fill(liters);
  await till
    .getByLabel("Numer paragonu")
    .fill(`DEMO/${crypto.randomUUID().slice(0, 8)}`.toUpperCase());
  if (coupon) await till.getByLabel("Kod kuponu").fill(coupon);
}

/**
 * The demo scenario end to end, only through the UI: the customer and the cashier in separate
 * browser contexts. Card -> purchase -> points -> exchange -> activation confirmed in the chat ->
 * preview and one redemption at the till -> history. A second redemption of the same coupon is refused.
 */
test("demo: purchase, points, exchange, chat activation, one redemption, history", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Demo");
  await staff.createOffer({
    name,
    pointsCost: 500,
    discountPerLiter: 0.2,
    maxLiters: 50,
  });

  // Customer: card on the phone.
  await page.goto("/karta");
  await page.getByRole("button", { name: "Wydaj kartę" }).click();
  const card = (await page.getByTestId("card-code").getAttribute("data-code"))!;

  // Cashier: the purchase awards points.
  const register = await openRegister(browser, baseURL!);
  const till = register.page;
  await sell(till, card, "600", "100");
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(
    till.getByRole("status").filter({ hasText: "Zakup zapisany" }),
  ).toContainText("Przyznano 600 pkt");

  // Customer: balance, then the exchange with an explicit confirmation.
  await page.goto("/");
  await expect(page.getByRole("region", { name: "Twoje saldo" })).toContainText(
    "600 pkt",
  );
  await page.getByRole("link", { name: /Oferty/ }).click();
  const offer = page.getByTestId("offer").filter({ hasText: name });
  await offer.getByRole("button", { name: "Wymień punkty" }).click();
  await offer
    .getByRole("group", { name: "Potwierdzenie wymiany" })
    .getByRole("button", { name: "Potwierdź wymianę" })
    .click();
  await expect(
    page.getByRole("status").filter({ hasText: `Kupiono kupon „${name}”` }),
  ).toBeVisible();

  // Customer: the assistant proposes the activation, only the button activates.
  await page.goto("/");
  await page.getByRole("link", { name: /Asystent/ }).click();
  await page.getByLabel("Wiadomość do asystenta").fill("Aktywuj mój kupon");
  await page.getByRole("button", { name: "Wyślij" }).click();
  const confirmation = page.getByTestId("chat-confirmation");
  await expect(confirmation).toContainText(`Aktywować kupon „${name}”?`);
  await confirmation.getByRole("button", { name: "Aktywuj kupon" }).click();
  await expect(confirmation.getByRole("status")).toContainText(
    "Kupon jest aktywny",
  );
  await confirmation.getByRole("link", { name: "Moje kupony" }).click();
  const coupon = page
    .getByTestId("coupon")
    .filter({ hasText: name, visible: true });
  const code = (await coupon
    .getByTestId("coupon-code")
    .getAttribute("data-code"))!;

  // Cashier: preview (nothing is used), then one redemption with the purchase.
  await sell(till, card, "100", "40", code);
  await expect(till.getByTestId("coupon-terms")).toContainText(name);
  await till.getByRole("button", { name: "Przelicz rabat" }).click();
  await expect(till.getByTestId("coupon-calculation")).toContainText("8,00 zł");
  await page.reload();
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Aktywny");
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByTestId("purchase-result")).toHaveText(
    "Rabat z kuponu 8,00 zł · do zapłaty 92,00 zł",
  );

  // The same coupon again: refused, nothing saved.
  await sell(till, card, "50", "20", code);
  await expect(till.getByRole("main").getByRole("alert")).toContainText(
    "Ten kupon został już wykorzystany.",
  );
  await till.getByRole("button", { name: "Zapisz zakup" }).click();
  await expect(till.getByTestId("purchase-result")).toHaveCount(0);

  // Customer: used coupon, balance and history with the discount.
  await page.reload();
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Wykorzystany");
  await page.goto("/historia");
  const items = page.getByTestId("history-item");
  await expect(items).toHaveCount(3); // two purchases and the exchange
  const withCoupon = items.filter({ hasText: "Tankowanie" }).first();
  await expect(withCoupon.getByTestId("history-delta")).toHaveText("+92 pkt");
  await expect(withCoupon.getByTestId("history-discount")).toHaveText(
    "Rabat z kuponu 8,00 zł · zapłacono 92,00 zł",
  );
  await expect(
    items.filter({ hasText: "Wymiana na kupon" }).getByTestId("history-delta"),
  ).toHaveText("-500 pkt");
  await page.goto("/");
  await expect(page.getByRole("region", { name: "Twoje saldo" })).toContainText(
    "192 pkt",
  );
  await register.context.close();
});
