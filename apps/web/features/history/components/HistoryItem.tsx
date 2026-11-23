import { Fuel, Ticket } from "lucide-react";
import { cn } from "@/lib/utils";
import type { Transaction } from "../services";
import {
  TYPE_LABELS,
  dateTime,
  liters,
  money,
  plain,
  points,
  saleDate,
} from "./historyFormat";

export function HistoryItem({ item }: { item: Transaction }) {
  const purchase = item.fuelPurchase;
  const exchange = item.couponExchange;
  const positive = item.delta > 0;
  const Icon = item.type === "CouponExchange" ? Ticket : Fuel;

  return (
    <li
      data-testid="history-item"
      data-id={item.id}
      className="flex gap-4 rounded-2xl border bg-card p-4"
    >
      <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-orange/15 text-primary">
        <Icon className="size-5" aria-hidden />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex items-baseline justify-between gap-3">
          <span className="font-medium">
            {TYPE_LABELS[item.type] ?? "Operacja"}
          </span>
          <span
            data-testid="history-delta"
            className={cn(
              "font-semibold tabular-nums",
              positive
                ? "text-emerald-700 dark:text-emerald-400"
                : "text-destructive",
            )}
          >
            {points.format(item.delta)} pkt
          </span>
        </div>
        {purchase && (
          <p className="text-sm text-muted-foreground">
            {[
              `Stacja ${purchase.stationCode}`,
              saleDate.format(new Date(`${purchase.saleDate}T00:00:00Z`)),
              money.format(purchase.grossAmount),
              `${liters.format(purchase.liters)} l`,
            ].join(" · ")}
          </p>
        )}
        {purchase && purchase.discount > 0 && (
          <p data-testid="history-discount" className="text-sm">
            Rabat z kuponu {money.format(purchase.discount)} · zapłacono{" "}
            {money.format(purchase.paidAmount)}
          </p>
        )}
        {exchange && (
          <p className="text-sm text-muted-foreground">{exchange.offerName}</p>
        )}
        <div className="flex flex-wrap items-baseline justify-between gap-x-3 text-xs text-muted-foreground">
          <time dateTime={item.createdAt}>
            {dateTime.format(new Date(item.createdAt))}
          </time>
          <span>
            Saldo po:{" "}
            <span className="tabular-nums">
              {plain.format(item.balanceAfter)} pkt
            </span>
          </span>
        </div>
      </div>
    </li>
  );
}
