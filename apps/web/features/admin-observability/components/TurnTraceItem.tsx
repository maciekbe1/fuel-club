import type { TurnTrace } from "../services";
import { OUTCOMES, dateTime, ms, number, usd } from "./labels";
import { ReasonBadges } from "./ReasonBadges";

/** A turn: timing, outcome, model steps, usage and the tools with their share of the time. */
export function TurnTraceItem({
  turn,
  index,
}: {
  turn: TurnTrace;
  index: number;
}) {
  const tokens =
    turn.inputTokens == null
      ? "brak danych"
      : `${number.format(turn.inputTokens)} we / ${number.format(turn.outputTokens ?? 0)} wy`;
  const cost =
    turn.costUsd != null
      ? usd.format(turn.costUsd)
      : turn.provider === "demo"
        ? "nie dotyczy (demo)"
        : "nieznany";
  return (
    <li
      data-testid="turn-trace"
      data-id={turn.id}
      className="flex flex-col gap-2 rounded-2xl border p-4 text-sm"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-medium">
          {index}. {dateTime.format(new Date(turn.startedAt))} ·{" "}
          {OUTCOMES[turn.outcome] ?? turn.outcome}
          {turn.errorCode && ` · ${turn.errorCode}`}
          {turn.handoffProposed && " · propozycja konsultanta"}
        </span>
        <ReasonBadges reasons={turn.reasons} />
      </div>
      <dl className="grid gap-x-4 gap-y-1 text-muted-foreground sm:grid-cols-2">
        <div>
          <dt className="inline">Pierwsze zdarzenie / całość: </dt>
          <dd className="inline">
            {ms(turn.firstEventMs)} / {ms(turn.totalMs)}
          </dd>
        </div>
        <div>
          <dt className="inline">Kroki modelu: </dt>
          <dd className="inline">{turn.modelSteps ?? "brak danych"}</dd>
        </div>
        <div>
          <dt className="inline">Tokeny: </dt>
          <dd className="inline">{tokens}</dd>
        </div>
        <div>
          <dt className="inline">Koszt (szacunek): </dt>
          <dd className="inline">{cost}</dd>
        </div>
        <div>
          <dt className="inline">Model: </dt>
          <dd className="inline">
            {turn.provider}/{turn.model} ({turn.promptVersion})
          </dd>
        </div>
        <div>
          <dt className="inline">Korelacja: </dt>
          <dd className="inline font-mono">{turn.correlationId}</dd>
        </div>
      </dl>
      {turn.tools.length > 0 && (
        <ol aria-label="Narzędzia" className="flex flex-col gap-1">
          {turn.tools.map((tool, i) => (
            <li key={i} className="flex items-center gap-2">
              <span
                aria-hidden="true"
                className={`h-2 rounded-full ${tool.status === "ok" ? "bg-emerald-500" : "bg-amber-500"}`}
                // Width: the tool's share of the whole turn, at least visible.
                style={{
                  width: `${Math.max(4, Math.min(100, (tool.durationMs / Math.max(1, turn.totalMs)) * 100))}%`,
                  maxWidth: "12rem",
                }}
              />
              <span className="font-mono text-xs">
                {tool.name} · {tool.status} · {ms(tool.durationMs)}
              </span>
            </li>
          ))}
        </ol>
      )}
    </li>
  );
}
