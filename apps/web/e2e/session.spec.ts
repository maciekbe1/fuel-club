import { test as anonymous } from "@playwright/test";
import { expect, test } from "./fixtures";

anonymous("without a session cookie the visitor goes to login", async ({ page }) => {
  await page.goto("/");
  await page.waitForURL("/login?next=%2F");
  await expect(page.getByText("Za każdy pełny 1 zł wydany na paliwo dostajesz 1 punkt.")).toBeAttached();
});

anonymous("an invalid session cookie (401 from the API) goes to login", async ({ page, context }) => {
  await context.addCookies([{ name: ".AspNetCore.Identity.Application", value: "invalid", url: "http://localhost:3100" }]);
  await page.goto("/");
  await page.waitForURL("/login");
});

for (const fault of ["503", "drop"]) {
  test(`API failure (${fault}) shows a retryable error, not a logout`, async ({ page, context, customer }) => {
    void customer;
    await context.addCookies([{ name: "e2e-fault", value: fault, url: "http://localhost:3100" }]);

    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Serwis chwilowo niedostępny" })).toBeVisible();
    expect(new URL(page.url()).pathname).toBe("/");

    await context.clearCookies({ name: "e2e-fault" });
    await page.getByRole("button", { name: "Spróbuj ponownie" }).click();
    await expect(page.getByRole("heading", { name: "Cześć, Anna" })).toBeVisible();
  });
}
