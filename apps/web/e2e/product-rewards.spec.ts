import { CASHIER } from "./accounts";
import {
  cashier,
  expect,
  issueCard,
  loginAs,
  offerName,
  test,
} from "./fixtures";

test("a product reward: exchange, activate pickup, the cashier hands it over once", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const name = offerName("Kawa");
  await staff.createOffer({ name, pointsCost: 150, kind: "Product" });
  const card = await issueCard(page);
  const pos = await cashier(baseURL!);
  await pos.purchase(card, 200);
  await pos.dispose();

  await page.goto("/oferty");
  const rewards = page.getByRole("list", { name: "Nagrody" });
  const item = rewards.getByTestId("offer").filter({ hasText: name });
  await expect(item).toContainText("Nagroda do odbioru w kasie");
  await item.getByRole("button", { name: "Wymień punkty" }).click();
  await expect(item).toContainText(`nagrodę „${name}”, do odbioru w kasie`);
  await item.getByRole("button", { name: "Potwierdź wymianę" }).click();
  await expect(
    page
      .getByRole("status")
      .filter({ hasText: "Wymieniono punkty na nagrodę" }),
  ).toBeVisible();

  await page.goto("/kupony");
  await expect(
    page.getByRole("heading", { name: "Moje kupony i nagrody" }),
  ).toBeVisible();
  const reward = page.getByTestId("coupon").filter({ hasText: name });
  await expect(reward).toContainText("Nagroda do odbioru w kasie");
  await expect(reward).toContainText("150 pkt");
  await reward.getByRole("button", { name: "Aktywuj odbiór" }).click();
  await expect(reward.getByTestId("coupon-status")).toHaveText("Aktywny");
  const code = (await reward
    .getByTestId("coupon-code")
    .getAttribute("data-code"))!;

  const context = await browser.newContext({ baseURL, locale: "pl-PL" });
  const register = await context.newPage();
  await loginAs(register, CASHIER.email, CASHIER.password);
  await register.goto("/kasa");
  await register.getByLabel("Kod karty klienta").fill(card);
  await register.getByLabel("Kod kuponu").fill(code);
  const handover = register.getByTestId("reward-handover");
  await expect(handover).toContainText(`${name}: nagroda do wydania`);
  await handover.getByRole("button", { name: "Wydaj nagrodę" }).click();
  await expect(
    register
      .getByRole("status")
      .filter({ hasText: `Wydano nagrodę „${name}”` }),
  ).toBeVisible();
  await expect(register.getByLabel("Kod kuponu")).toHaveValue("");

  // The same code again: already handed over.
  await register.getByLabel("Kod kuponu").fill(code);
  await expect(
    register
      .getByRole("alert")
      .filter({ hasText: "Ten kupon został już wykorzystany" }),
  ).toBeVisible();
  await context.close();

  await page.reload();
  await expect(reward.getByTestId("coupon-status")).toHaveText("Wykorzystany");
  await expect(reward.getByTestId("coupon-code")).toHaveCount(0);
});
