import {
  litersFormat as liters,
  moneyFormat as money,
  numberFormat as plain,
} from "@/lib/format";
import type { PointsAwarded } from "./services";

/** Toast text: how many points and what for (fuel, liters, paid amount, coupon discount). */
export function pointsMessage(event: PointsAwarded): {
  title: string;
  description: string;
} {
  const paid =
    event.discount > 0
      ? `zapłacono ${money.format(event.paidAmount)} po rabacie ${money.format(event.discount)}`
      : money.format(event.paidAmount);
  const purchase = `Tankowanie ${liters.format(event.liters)} l, ${paid}, stacja ${event.stationCode}.`;
  if (event.points > 0)
    return {
      title: `+${plain.format(event.points)} pkt za tankowanie`,
      description: purchase,
    };
  return {
    title: "Zakup zarejestrowany, bez punktów",
    description: `${purchase} ${
      event.paidAmount === 0
        ? "Rabat pokrył cały zakup."
        : "Punkty przysługują za każdy pełny 1 zł."
    }`,
  };
}
