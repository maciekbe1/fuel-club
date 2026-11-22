import { expect, type Browser } from "@playwright/test";
import { CASHIER } from "./accounts";
import { loginAs } from "./fixtures";

/** The cashier's register in its own browser context (Warsaw time zone, Polish locale). */
export async function openRegister(
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
