import type { CostMetrics } from "../services";
import { Tile } from "./Tile";
import { number, usd } from "./labels";

/** Demo is "not applicable", missing usage or price is "unknown" - never a zero (ADR 0018). */
export function CostTile({ cost }: { cost: CostMetrics }) {
  const priced = cost.knownTurns + cost.unknownTurns;
  let value: string;
  if (priced === 0)
    value = cost.notApplicableTurns > 0 ? "nie dotyczy (demo)" : "brak tur";
  else if (cost.knownTurns === 0) value = "nieznany";
  else
    value =
      cost.unknownTurns > 0
        ? `≥ ${usd.format(cost.usd ?? 0)} (niepełny: ${number.format(cost.unknownTurns)} tur nieznanych)`
        : usd.format(cost.usd ?? 0);
  const versions =
    cost.priceVersions.length > 0
      ? `, cennik ${cost.priceVersions.join(", ")}`
      : "";
  return (
    <div className="flex flex-col gap-1">
      <dl data-testid="cost-metrics">
        <Tile label="Szacowany koszt modelu" value={value} />
      </dl>
      <p className="text-xs text-muted-foreground">
        Szacunek, nie faktura{versions}. Tury demo:{" "}
        {number.format(cost.notApplicableTurns)} (nie dotyczy).
      </p>
    </div>
  );
}
