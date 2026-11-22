import { z } from "zod";
import type { FuelPurchaseBody } from "./services";

/** Form fields are text; amounts accept a Polish decimal comma and are sent as JSON numbers. */
const decimal = (label: string, places: number) =>
  z
    .string()
    .trim()
    .min(1, `Podaj ${label}`)
    .regex(
      new RegExp(`^\\d{1,7}([.,]\\d{1,${places}})?$`),
      `Podaj ${label} jako liczbę (maks. ${places} miejsca po przecinku)`,
    );

/** Same normalization as the server: no spaces or dashes, upper case, O → 0, I/L → 1. */
export const normalizeCode = (text: string) =>
  text
    .replace(/[\s-]+/g, "")
    .toUpperCase()
    .replace(/O/g, "0")
    .replace(/[IL]/g, "1");

const CARD_CODE = /^[0-7][0-9A-HJKMNP-TV-Z]{25}$/;
const COUPON_CODE = /^K[0-7][0-9A-HJKMNP-TV-Z]{25}$/;

/** Card codes: 26 Crockford Base32 characters, the first one 0-7. Scans of anything else are ignored. */
export const looksLikeCardCode = (text: string) =>
  CARD_CODE.test(normalizeCode(text));

/** Coupon codes: "K" and a code in the card format. A card code is never a coupon code. */
export const looksLikeCouponCode = (text: string) =>
  COUPON_CODE.test(normalizeCode(text));

export const purchaseSchema = z.object({
  cardCode: z.string().trim().min(1, "Podaj lub zeskanuj kod karty"),
  couponCode: z
    .string()
    .refine(
      (value) => value.trim() === "" || looksLikeCouponCode(value),
      "Kod kuponu ma 27 znaków i zaczyna się od K",
    ),
  amount: decimal("kwotę", 2),
  liters: decimal("litry", 3),
  saleDate: z.string().min(1, "Podaj datę sprzedaży"),
  receiptNumber: z.string().trim().min(1, "Podaj numer paragonu"),
});

export type PurchaseValues = z.infer<typeof purchaseSchema>;

const toNumber = (value: string) => Number(value.trim().replace(",", "."));

/** The number in a field, or null while the text is not a valid amount with `places` decimals. */
export function parseDecimal(value: string, places: number) {
  const text = value.trim();
  return new RegExp(`^\\d{1,7}([.,]\\d{1,${places}})?$`).test(text)
    ? toNumber(text)
    : null;
}

export function toPurchaseBody(values: PurchaseValues): FuelPurchaseBody {
  return {
    cardCode: values.cardCode.replace(/\s+/g, "").toUpperCase(),
    // Empty field = purchase without a coupon (the field is left out, never sent blank).
    ...(values.couponCode.trim()
      ? { couponCode: normalizeCode(values.couponCode) }
      : {}),
    amount: toNumber(values.amount),
    liters: toNumber(values.liters),
    saleDate: values.saleDate,
    receiptNumber: values.receiptNumber.trim(),
  };
}

export const warsawToday = () =>
  new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Warsaw",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());

export const purchaseMessages: Record<string, string> = {
  "loyalty.required_field": "To pole jest wymagane.",
  "loyalty.too_long": "Wartość jest za długa.",
  "loyalty.invalid_format": "Niepoprawny format.",
  "loyalty.amount_out_of_range":
    "Kwota musi być większa od 0 i nie większa niż 5000 zł.",
  "loyalty.amount_precision":
    "Kwota może mieć najwyżej 2 miejsca po przecinku.",
  "loyalty.liters_out_of_range": "Litry muszą być większe od 0.",
  "loyalty.liters_precision":
    "Litry mogą mieć najwyżej 3 miejsca po przecinku.",
  "loyalty.sale_date_in_future": "Data sprzedaży nie może być z przyszłości.",
  "loyalty.card_not_found": "Nie znaleziono aktywnej karty o tym kodzie.",
  "loyalty.receipt_already_registered":
    "Ten paragon jest już zarejestrowany na tej kasie.",
  "rewards.required_field": "To pole jest wymagane.",
  "rewards.invalid_format": "Niepoprawny kod kuponu.",
  "rewards.coupon_not_found":
    "Nie znaleziono aktywnego kuponu tego klienta o tym kodzie.",
  "rewards.coupon_redeemed": "Ten kupon został już wykorzystany.",
  default: "Niepoprawna wartość.",
};
