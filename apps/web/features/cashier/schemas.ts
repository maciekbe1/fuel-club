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

export const purchaseSchema = z.object({
  cardCode: z.string().trim().min(1, "Podaj lub zeskanuj kod karty"),
  amount: decimal("kwotę", 2),
  liters: decimal("litry", 3),
  saleDate: z.string().min(1, "Podaj datę sprzedaży"),
  receiptNumber: z.string().trim().min(1, "Podaj numer paragonu"),
});

export type PurchaseValues = z.infer<typeof purchaseSchema>;

const toNumber = (value: string) => Number(value.trim().replace(",", "."));

export function toPurchaseBody(values: PurchaseValues): FuelPurchaseBody {
  return {
    cardCode: values.cardCode.replace(/\s+/g, "").toUpperCase(),
    amount: toNumber(values.amount),
    liters: toNumber(values.liters),
    saleDate: values.saleDate,
    receiptNumber: values.receiptNumber.trim(),
  };
}

/** Card codes: 26 Crockford Base32 characters, the first one 0-7. Scans of anything else are ignored. */
export const looksLikeCardCode = (text: string) =>
  /^[0-7][0-9A-HJKMNP-TV-Z]{25}$/.test(text.trim().toUpperCase());

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
  default: "Niepoprawna wartość.",
};
