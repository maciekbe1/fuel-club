import { cn } from "@/lib/utils";
import type { AnomalyResult } from "../services";
import { RULES, number, percent } from "./labels";

export function Rule({ rule }: { rule: AnomalyResult }) {
  const rate = rule.rule === "ai_errors";
  const value = rate ? percent.format(rule.value) : number.format(rule.value);
  const threshold = rate
    ? percent.format(rule.threshold)
    : number.format(rule.threshold);
  return (
    <li
      data-testid="anomaly-rule"
      data-rule={rule.rule}
      data-triggered={rule.triggered}
      className="flex flex-wrap items-center justify-between gap-2 p-3 text-sm"
    >
      <span className="font-medium">
        {RULES[rule.rule] ?? rule.rule}
        {rule.severity === "critical" && (
          <span className="ml-2 rounded bg-destructive/15 px-1.5 py-0.5 text-xs text-destructive">
            krytyczna
          </span>
        )}
      </span>
      <span
        className={cn(
          "tabular-nums",
          rule.triggered && "font-semibold text-destructive",
        )}
      >
        {!rule.enabled
          ? "wyłączona"
          : rate && rule.sample < rule.minSample
            ? `za mała próbka (${rule.sample}/${rule.minSample})`
            : `${value} / próg ${threshold}`}{" "}
        <span className="text-muted-foreground">
          w {rule.windowMinutes} min
        </span>
      </span>
    </li>
  );
}
