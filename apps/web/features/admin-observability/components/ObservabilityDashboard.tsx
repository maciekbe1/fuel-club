"use client";

import { AlertTriangle, CheckCircle2 } from "lucide-react";
import { useState } from "react";
import { LoginAgainLink } from "@/components/auth/LoginAgainLink";
import { Button } from "@/components/ui/button";
import { RetryButton } from "@/components/ui/retry-button";
import { ApiError, readErrorMessage } from "@/lib/api/problem";
import { cn } from "@/lib/utils";
import {
  useMarkReviewed,
  useObservabilitySummary,
  useReviewQueue,
} from "../hooks";
import type { AnomalyResult, ReviewItem } from "../services";

const WINDOWS = [
  { hours: 1, label: "1 h" },
  { hours: 24, label: "24 h" },
  { hours: 168, label: "7 dni" },
] as const;

const RULES: Record<string, string> = {
  access_denied: "Odmowy dostępu (403)",
  login_failed: "Nieudane logowania",
  idempotency_conflict: "Konflikty kluczy idempotencji",
  repeated_redemption: "Ponowne użycie odebranego kuponu",
  rate_limited: "Przekroczone limity (429)",
  tool_limit: "Limit narzędzi w turze",
  rejected_tools: "Tury z odrzuconym narzędziem",
  ai_errors: "Udział błędów asystenta",
};

const SECURITY: Record<string, string> = {
  AccessDenied: "Odmowy dostępu",
  LoginFailed: "Nieudane logowania",
  IdempotencyConflict: "Konflikty kluczy",
  RepeatedRedemption: "Ponowny odbiór kuponu",
  RateLimited: "Limity (429)",
};

const REASONS: Record<string, string> = {
  error: "błąd tury",
  rejected_tool: "odrzucone narzędzie",
  tool_failure: "awaria narzędzia",
  repeated_errors: "powtarzające się błędy użytkownika",
};

const dateTime = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "short",
  timeStyle: "medium",
  timeZone: "Europe/Warsaw",
});
const number = new Intl.NumberFormat("pl-PL", { maximumFractionDigits: 1 });
const percent = new Intl.NumberFormat("pl-PL", {
  style: "percent",
  maximumFractionDigits: 1,
});

const ms = (value: number | null | undefined) =>
  value == null ? "brak danych" : `${number.format(value)} ms`;

function LoadError({
  error,
  retry,
  busy,
}: {
  error: unknown;
  retry: () => void;
  busy: boolean;
}) {
  return (
    <div
      role="alert"
      className="flex flex-col items-start gap-3 rounded-2xl border p-5"
    >
      <p>
        {readErrorMessage(
          error,
          "Nie udało się pobrać danych. Spróbuj ponownie.",
        )}
      </p>
      {error instanceof ApiError && error.status === 401 ? (
        <LoginAgainLink />
      ) : (
        <RetryButton busy={busy} onClick={retry} />
      )}
    </div>
  );
}

/** Metadata only: counts, times, rules and pseudonymous turns - never conversation content. */
export function ObservabilityDashboard() {
  const [hours, setHours] = useState<number>(24);
  return (
    <div className="flex flex-col gap-8">
      <div role="group" aria-label="Okno czasu" className="flex gap-2">
        {WINDOWS.map((window) => (
          <Button
            key={window.hours}
            type="button"
            size="sm"
            variant={hours === window.hours ? "default" : "outline"}
            aria-pressed={hours === window.hours}
            onClick={() => setHours(window.hours)}
          >
            {window.label}
          </Button>
        ))}
      </div>
      <Summary hours={hours} />
      <Queue />
    </div>
  );
}

function Summary({ hours }: { hours: number }) {
  const summary = useObservabilitySummary(hours);
  if (summary.isPending)
    return (
      <div
        role="status"
        aria-busy="true"
        className="h-64 animate-pulse rounded-3xl bg-muted"
      >
        <span className="sr-only">Ładowanie metryk…</span>
      </div>
    );
  if (summary.data === undefined)
    return (
      <LoadError
        error={summary.error}
        busy={summary.isFetching}
        retry={() => summary.refetch()}
      />
    );
  const { chat, security, anomalies } = summary.data;
  const triggered = anomalies.filter((a) => a.triggered);
  return (
    <>
      <section aria-labelledby="chat-metrics" className="flex flex-col gap-3">
        <h2 id="chat-metrics" className="text-lg font-semibold">
          Czat
        </h2>
        <dl
          data-testid="chat-metrics"
          className="grid grid-cols-2 gap-3 sm:grid-cols-4"
        >
          <Tile label="Tury" value={number.format(chat.turns)} />
          <Tile label="Zakończone" value={number.format(chat.done)} />
          <Tile label="Błędy" value={number.format(chat.errors)} />
          <Tile
            label="Przerwane przez klienta"
            value={number.format(chat.cancelled)}
          />
          <Tile
            label="p95 do pierwszego zdarzenia"
            value={ms(chat.p95FirstEventMs)}
          />
          <Tile label="p95 całej tury" value={ms(chat.p95TotalMs)} />
          <Tile
            label="Tokeny (wej./wyj.)"
            value={
              chat.inputTokens == null
                ? "brak danych"
                : `${number.format(chat.inputTokens)} / ${number.format(chat.outputTokens ?? 0)}`
            }
          />
          <Tile
            label="Tury bez danych o tokenach"
            value={number.format(chat.turnsWithoutTokenData)}
          />
        </dl>
        {chat.errorCodes.length > 0 && (
          <Counts
            label="Kody błędów"
            items={chat.errorCodes.map((c) => [c.key, c.count])}
          />
        )}
        {chat.tools.length > 0 && (
          <Counts
            label="Narzędzia"
            items={chat.tools.map((t) => [`${t.name} - ${t.status}`, t.count])}
          />
        )}
      </section>

      <section aria-labelledby="security" className="flex flex-col gap-3">
        <h2 id="security" className="text-lg font-semibold">
          Zdarzenia bezpieczeństwa
        </h2>
        {security.length === 0 ? (
          <p className="text-sm text-muted-foreground">Brak w tym oknie.</p>
        ) : (
          <Counts
            label="Zdarzenia bezpieczeństwa"
            items={security.map((s) => [SECURITY[s.key] ?? s.key, s.count])}
          />
        )}
      </section>

      <section aria-labelledby="anomalies" className="flex flex-col gap-3">
        <h2 id="anomalies" className="text-lg font-semibold">
          Reguły anomalii
        </h2>
        <p className="text-sm text-muted-foreground">
          Każda reguła liczy we własnym oknie, niezależnie od okna wyżej.
        </p>
        {triggered.length > 0 && (
          <p
            role="alert"
            className="flex items-center gap-2 rounded-2xl border border-amber-500/50 bg-amber-50 p-3 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-50"
          >
            <AlertTriangle className="size-4 shrink-0" />
            Przekroczone progi:{" "}
            {triggered.map((a) => RULES[a.rule] ?? a.rule).join(", ")}.
          </p>
        )}
        <ul
          aria-label="Reguły"
          className="flex flex-col divide-y rounded-2xl border"
        >
          {anomalies.map((rule) => (
            <Rule key={rule.rule} rule={rule} />
          ))}
        </ul>
      </section>
    </>
  );
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1 rounded-2xl border p-3">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-lg font-semibold tabular-nums">{value}</dd>
    </div>
  );
}

function Counts({
  label,
  items,
}: {
  label: string;
  items: [string, number][];
}) {
  return (
    <ul aria-label={label} className="flex flex-wrap gap-2 text-sm">
      {items.map(([key, count]) => (
        <li key={key} className="rounded-full border px-3 py-1">
          {key}: <span className="font-semibold tabular-nums">{count}</span>
        </li>
      ))}
    </ul>
  );
}

function Rule({ rule }: { rule: AnomalyResult }) {
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

function Queue() {
  const queue = useReviewQueue();
  const mark = useMarkReviewed();
  return (
    <section aria-labelledby="review" className="flex flex-col gap-3">
      <h2 id="review" className="text-lg font-semibold">
        Do przejrzenia
      </h2>
      <p className="text-sm text-muted-foreground">
        Tury z błędem albo odrzuconym narzędziem. Tylko metadane i pseudonim -
        treść rozmów nie jest zapisywana. Przerwanie rozmowy przez klienta nie
        trafia tutaj.
      </p>
      {queue.isPending ? (
        <div
          role="status"
          aria-busy="true"
          className="h-32 animate-pulse rounded-2xl bg-muted"
        >
          <span className="sr-only">Ładowanie kolejki…</span>
        </div>
      ) : queue.data === undefined ? (
        <LoadError
          error={queue.error}
          busy={queue.isFetching}
          retry={() => queue.refetch()}
        />
      ) : queue.data.items.length === 0 ? (
        <p className="flex items-center gap-2 text-sm">
          <CheckCircle2 className="size-4 text-emerald-600" /> Nic do
          przejrzenia.
        </p>
      ) : (
        <ul aria-label="Kolejka przeglądu" className="flex flex-col gap-3">
          {queue.data.items.map((item) => (
            <QueueItem
              key={item.id}
              item={item}
              busy={mark.isPending && mark.variables === item.id}
              failed={mark.isError && mark.variables === item.id}
              onReviewed={() => mark.mutate(item.id)}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

function QueueItem({
  item,
  busy,
  failed,
  onReviewed,
}: {
  item: ReviewItem;
  busy: boolean;
  failed: boolean;
  onReviewed: () => void;
}) {
  return (
    <li
      data-testid="review-item"
      data-id={item.id}
      className="flex flex-col gap-2 rounded-2xl border p-4 text-sm"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-medium">
          {dateTime.format(new Date(item.startedAt))} · {item.outcome}
          {item.errorCode && ` · ${item.errorCode}`}
        </span>
        <span className="flex flex-wrap gap-1">
          {item.reasons.map((reason) => (
            <span
              key={reason}
              className="rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-950 dark:bg-amber-900 dark:text-amber-50"
            >
              {REASONS[reason] ?? reason}
            </span>
          ))}
        </span>
      </div>
      <dl className="grid gap-x-4 gap-y-1 text-muted-foreground sm:grid-cols-2">
        <div>
          <dt className="inline">Użytkownik (pseudonim): </dt>
          <dd className="inline font-mono">
            {item.userPseudonym.slice(0, 12)}…
          </dd>
        </div>
        <div>
          <dt className="inline">Korelacja: </dt>
          <dd className="inline font-mono">{item.correlationId}</dd>
        </div>
        <div>
          <dt className="inline">Model: </dt>
          <dd className="inline">
            {item.provider}/{item.model} ({item.promptVersion})
          </dd>
        </div>
        <div>
          <dt className="inline">Czas: </dt>
          <dd className="inline">{ms(item.totalMs)}</dd>
        </div>
      </dl>
      {item.tools.length > 0 && (
        <p className="text-muted-foreground">
          Narzędzia:{" "}
          {item.tools.map((t) => `${t.name} (${t.status})`).join(", ")}
        </p>
      )}
      {failed && (
        <p role="alert" className="text-destructive">
          Nie udało się oznaczyć. Spróbuj ponownie.
        </p>
      )}
      <Button
        type="button"
        size="sm"
        variant="outline"
        className="self-start"
        disabled={busy}
        onClick={onReviewed}
      >
        {busy ? "Zapisywanie…" : "Oznacz jako przejrzane"}
      </Button>
    </li>
  );
}
