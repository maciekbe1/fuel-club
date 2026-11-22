import {
  buyCoupon,
  cashier,
  csrf,
  expect,
  newCustomer,
  test,
} from "./fixtures";
import { decodeQr } from "./qr";

const ACTIVATE = "**/api/me/coupons/*/activate";
const COUPON_CODE = /^K[0-7][0-9A-HJKMNP-TV-Z]{25}$/;

test("activation shows the code and its QR; redemption at the till marks the coupon used", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { couponId, card, name } = await buyCoupon(page, staff, baseURL!);

  await page.getByRole("link", { name: /Moje kupony/ }).click();
  const coupon = page.getByTestId("coupon").filter({ hasText: name });
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Do aktywacji");
  await expect(coupon).toContainText("0,20 zł/l, do 50 l (maks. 10,00 zł)");
  await expect(coupon.getByTestId("coupon-qr")).toHaveCount(0);

  await coupon.getByRole("button", { name: "Aktywuj kupon" }).click();
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Aktywny");
  const code = (await coupon
    .getByTestId("coupon-code")
    .getAttribute("data-code"))!;
  expect(code).toMatch(COUPON_CODE);
  expect(await decodeQr(coupon.getByTestId("coupon-qr"))).toBe(code);

  // The same code after a reload (GET), and the API agrees.
  await page.reload();
  await expect(coupon.getByTestId("coupon-code")).toHaveAttribute(
    "data-code",
    code,
  );
  const stored = await page.request.get(`/api/me/coupons/${couponId}`);
  expect(((await stored.json()) as { code: string }).code).toBe(code);

  const pos = await cashier(baseURL!);
  const sale = await pos.purchase(card, 100, 40, code);
  await pos.dispose();
  expect(sale.discount).toBe(8);

  await page.reload();
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Wykorzystany");
  await expect(coupon).toContainText("wykorzystany");
  await expect(coupon.getByTestId("coupon-qr")).toHaveCount(0);
  await expect(coupon.getByTestId("coupon-code")).toHaveCount(0);
  await expect(coupon.getByRole("button")).toHaveCount(0);
});

test("lost activation response: the retry reuses the key and shows the committed code", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { couponId, name } = await buyCoupon(page, staff, baseURL!);

  const keys: string[] = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/activate"))
      keys.push(request.headers()["idempotency-key"]);
  });
  let committed: string | undefined;
  await page.route(ACTIVATE, async (route) => {
    const response = await route.fetch();
    committed = ((await response.json()) as { code: string }).code;
    await route.abort("failed");
  });

  await page.goto("/kupony");
  const coupon = page.getByTestId("coupon").filter({ hasText: name });
  await coupon.getByRole("button", { name: "Aktywuj kupon" }).click();
  // The list is read again after the failed attempt, so the committed activation shows up.
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Aktywny");
  expect(committed).toMatch(COUPON_CODE);
  await expect(coupon.getByTestId("coupon-code")).toHaveAttribute(
    "data-code",
    committed!,
  );
  // The GET settled the unknown outcome: no "unknown" warning next to the code.
  await expect(coupon.getByRole("alert")).toHaveCount(0);
  expect(await decodeQr(coupon.getByTestId("coupon-qr"))).toBe(committed);
  await page.unroute(ACTIVATE);

  // The stored coupon has the committed code; only one activation request was sent.
  const again = await page.request.get(`/api/me/coupons/${couponId}`);
  expect(((await again.json()) as { code: string }).code).toBe(committed);
  expect(keys).toHaveLength(1);
});

test("two customers see only their own coupons", async ({
  page,
  browser,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const mine = await buyCoupon(page, staff, baseURL!);
  await using otherContext = await browser.newContext({ baseURL });
  const other = await otherContext.newPage();
  await newCustomer(other, "Bartek");
  const theirs = await buyCoupon(other, staff, baseURL!);

  await page.goto("/kupony");
  await other.goto("/kupony");
  await expect(page.getByTestId("coupon")).toHaveCount(1);
  await expect(page.getByTestId("coupon")).toContainText(mine.name);
  await expect(other.getByTestId("coupon")).toHaveCount(1);
  await expect(other.getByTestId("coupon")).toContainText(theirs.name);

  // Someone else's coupon by id: 404, and activating it does nothing.
  const read = await page.request.get(`/api/me/coupons/${theirs.couponId}`);
  expect(read.status()).toBe(404);
  const activate = await page.request.post(
    `/api/me/coupons/${theirs.couponId}/activate`,
    {
      headers: {
        "X-CSRF-TOKEN": await csrf(page.request),
        "Idempotency-Key": crypto.randomUUID(),
      },
    },
  );
  expect(activate.status()).toBe(404);
  await other.reload();
  await expect(other.getByTestId("coupon-status")).toHaveText("Do aktywacji");
});

test("empty list links to the offers", async ({ page, customer }) => {
  void customer;
  await page.goto("/kupony");
  await expect(
    page.getByRole("heading", { name: "Nie masz jeszcze kuponów" }),
  ).toBeVisible();
  await expect(
    page.getByRole("link", { name: "Zobacz oferty" }),
  ).toHaveAttribute("href", "/oferty");
});

test("activation failing before the server: retry with the same key activates once", async ({
  page,
  customer,
  staff,
  baseURL,
}) => {
  void customer;
  const { name } = await buyCoupon(page, staff, baseURL!);
  const keys: string[] = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/activate"))
      keys.push(request.headers()["idempotency-key"]);
  });
  await page.route(ACTIVATE, (route) => route.abort("failed"), { times: 1 });

  await page.goto("/kupony");
  const coupon = page.getByTestId("coupon").filter({ hasText: name });
  await coupon.getByRole("button", { name: "Aktywuj kupon" }).click();
  await expect(coupon.getByRole("alert")).toContainText(
    "nie wiadomo, czy kupon został aktywowany",
  );
  await coupon.getByRole("button", { name: "Ponów aktywację" }).click();
  await expect(coupon.getByTestId("coupon-status")).toHaveText("Aktywny");
  expect(keys).toHaveLength(2);
  expect(keys[1]).toBe(keys[0]);
});
