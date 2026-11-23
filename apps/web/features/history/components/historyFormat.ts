export {
  numberFormat as plain,
  moneyFormat as money,
  litersFormat as liters,
  dateTimeFormat as dateTime,
} from "@/lib/format";
export const points = new Intl.NumberFormat("pl-PL", { signDisplay: "always" });

export const saleDate = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "medium",
  timeZone: "UTC",
});

export const TYPE_LABELS: Record<string, string> = {
  FuelPurchase: "Tankowanie",
  CouponExchange: "Wymiana na kupon",
};
