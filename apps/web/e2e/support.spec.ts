import { expect, test, type Page } from "@playwright/test";
import { SUPPORT } from "./accounts";
import { loginAs, newCustomer } from "./fixtures";

const PROXY = "http://localhost:5282";

async function ask(page: Page, text: string) {
  await page.getByLabel("Wiadomość do asystenta").fill(text);
  await page.getByRole("button", { name: "Wyślij" }).click();
}

/**
 * ADR 0012 + 0014: the bot proposes an agent, the customer opens a case, an agent claims it, and both
 * see messages and "typing" through the hub (no polling while connected).
 */
test("handoff: customer and agent talk live, the chat comes back after closing", async ({
  page,
  browser,
}) => {
  const name = `Klient ${crypto.randomUUID().slice(0, 8)}`;
  await newCustomer(page, name);
  await page.goto("/czat");
  await ask(page, "Chcę porozmawiać z konsultantem");
  const proposal = page.getByTestId("chat-handoff");
  await expect(proposal).toBeVisible();
  await proposal
    .getByRole("button", { name: "Porozmawiaj z konsultantem" })
    .click();

  const start = page.getByTestId("support-start");
  await expect(start).toContainText("przechowywana 30 dni");
  await start
    .getByRole("checkbox", {
      name: "Dołącz dotychczasową rozmowę z asystentem",
    })
    .click();
  await start.getByLabel("W czym możemy pomóc?").fill("Brakuje mi punktów");
  await start.getByRole("button", { name: "Połącz z konsultantem" }).click();

  const customer = page.getByTestId("support-conversation");
  await expect(customer.getByTestId("support-status")).toHaveText(
    "Czekasz na konsultanta",
  );
  // The assistant takes no questions while the case is open.
  await expect(page.getByLabel("Wiadomość do asystenta")).toHaveCount(0);

  const agentContext = await browser.newContext();
  const agentPage = await agentContext.newPage();
  await loginAs(agentPage, SUPPORT.email, SUPPORT.password);
  await agentPage.goto("/konsultant");
  const item = agentPage
    .getByTestId("support-queue-item")
    .filter({ hasText: name });
  await item.getByRole("button", { name: "Przejmij" }).click();
  const agent = agentPage.getByTestId("support-conversation");
  await expect(agent.getByTestId("support-history")).toContainText(
    "Tekst przekazany przez klienta - niepotwierdzony",
  );
  await expect(agent).toContainText("Brakuje mi punktów");

  await expect(customer.getByTestId("support-status")).toHaveText(
    "Rozmawiasz z konsultantem",
  );
  // Live, not polling.
  await expect(customer).not.toContainText("odświeżanie co kilka sekund");
  await expect(agent).not.toContainText("odświeżanie co kilka sekund");

  const agentInput = agent.getByLabel("Odpowiedź dla klienta");
  await agentInput.pressSequentially("Dzień do");
  await expect(customer.getByTestId("support-typing")).toHaveText(
    "Konsultant pisze…",
  );
  await agentInput.pressSequentially("bry, sprawdzam");
  await agent.getByRole("button", { name: "Wyślij" }).click();
  await expect(
    customer.getByTestId("support-message").filter({ hasText: "sprawdzam" }),
  ).toBeVisible();
  await expect(customer.getByTestId("support-typing")).toHaveCount(0);

  const customerInput = customer.getByLabel("Wiadomość do konsultanta");
  await customerInput.pressSequentially("Dzię");
  await expect(agent.getByTestId("support-typing")).toHaveText("Klient pisze…");
  await customerInput.pressSequentially("kuję");
  await customer.getByRole("button", { name: "Wyślij" }).click();
  await expect(
    agent.getByTestId("support-message").filter({ hasText: "Dziękuję" }),
  ).toBeVisible();

  // The WebSocket drops: the client reconnects and reads what it missed through the cursor.
  expect((await page.request.post(`${PROXY}/__e2e/drop-ws`)).status()).toBe(
    204,
  );
  // "Typing" travels only over the hub: seeing it proves both sides reconnected.
  await expect(async () => {
    await agentInput.press("x");
    await expect(customer.getByTestId("support-typing")).toBeVisible({
      timeout: 2500,
    });
  }).toPass({ timeout: 20_000 });
  await agentInput.fill("Punkty dopisane");
  await agent.getByRole("button", { name: "Wyślij" }).click();
  await expect(
    customer
      .getByTestId("support-message")
      .filter({ hasText: "Punkty dopisane" }),
  ).toBeVisible();

  // Coming back to the chat screen shows the open case again.
  await page.reload();
  await expect(
    customer.getByTestId("support-message").filter({ hasText: "sprawdzam" }),
  ).toBeVisible();
  // No empty assistant window above the case.
  await expect(
    page.getByRole("log", { name: "Rozmowa z asystentem" }),
  ).toHaveCount(0);

  await customer.getByRole("button", { name: "Zakończ rozmowę" }).click();
  await expect(customer.getByTestId("support-status")).toHaveText(
    "Rozmowa zakończona",
  );
  await expect(agent).toContainText("Klient zakończył rozmowę.");
  await customer.getByRole("button", { name: "Wróć do asystenta" }).click();
  await expect(page.getByLabel("Wiadomość do asystenta")).toBeVisible();
  await agentContext.close();
});

test("the customer asks for an agent without the bot and nobody takes it yet", async ({
  page,
}) => {
  await newCustomer(page, "Klient bez czatu");
  await page.goto("/czat");
  await page
    .getByRole("button", { name: "Porozmawiaj z konsultantem" })
    .click();
  const start = page.getByTestId("support-start");
  // Nothing to attach before the first answer.
  await expect(start.getByRole("checkbox")).toHaveCount(0);
  await start.getByRole("button", { name: "Anuluj" }).click();
  await expect(page.getByLabel("Wiadomość do asystenta")).toBeVisible();
});
