export const points = new Intl.NumberFormat("pl-PL", { signDisplay: "always" });

export const plain = new Intl.NumberFormat("pl-PL");

export const money = new Intl.NumberFormat("pl-PL", {
  style: "currency",
  currency: "PLN",
});

export const liters = new Intl.NumberFormat("pl-PL", {
  maximumFractionDigits: 3,
});

export const dateTime = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});

export const saleDate = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "medium",
  timeZone: "UTC",
});

export const TYPE_LABELS: Record<string, string> = {
  FuelPurchase: "Tankowanie",
  CouponExchange: "Wymiana na kupon",
};
