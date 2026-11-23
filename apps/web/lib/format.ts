// Shared Polish formatters; times in the station's zone (Europe/Warsaw).
export const moneyFormat = new Intl.NumberFormat("pl-PL", {
  style: "currency",
  currency: "PLN",
});
export const numberFormat = new Intl.NumberFormat("pl-PL");
export const litersFormat = new Intl.NumberFormat("pl-PL", {
  maximumFractionDigits: 3,
});
/** "14:05" */
export const timeFormat = new Intl.DateTimeFormat("pl-PL", {
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});
/** "23.11.2026, 14:05" */
export const shortDateTimeFormat = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "short",
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});
/** "23 lis 2026, 14:05" */
export const dateTimeFormat = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});

const money = moneyFormat;
const liters = litersFormat;
const points = numberFormat;

export const formatPoints = (value: number) => `${points.format(value)} pkt`;

/** "0,20 zł/l, do 50 l (maks. 10,00 zł)" - the same terms the server will apply. */
export function formatTerms(discountPerLiter: number, maxLiters: number) {
  // Integers (grosz x ml), rounded half away from zero like the server's decimal Math.Round:
  // float multiplication would turn 0,15 x 50,5 = 7,575 into 7,5749… and show 7,57.
  const product =
    Math.round(discountPerLiter * 100) * Math.round(maxLiters * 1000);
  const max = Math.floor((product + 500) / 1000) / 100;
  return `${money.format(discountPerLiter)}/l, do ${liters.format(maxLiters)} l (maks. ${money.format(max)})`;
}

type Benefit = {
  kind?: string | null;
  discountPerLiter?: number | null;
  maxLiters?: number | null;
};

/** A product reward (ADR 0013) is handed over at the till; it has no per-liter terms. */
export const isProduct = (benefit: { kind?: string | null }) =>
  benefit.kind === "Product";

/** Per-liter terms of a fuel discount; null for a product reward. */
export function benefitTerms(benefit: Benefit) {
  return isProduct(benefit) ||
    benefit.discountPerLiter == null ||
    benefit.maxLiters == null
    ? null
    : formatTerms(benefit.discountPerLiter, benefit.maxLiters);
}
