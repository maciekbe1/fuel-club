"use client";

import { QueryError } from "@/components/query/QueryError";
import { Skeleton } from "@/components/ui/skeleton";
import { useConversationTrace } from "../hooks";
import { END_REASONS, dateTime } from "./labels";
import { ReasonBadges } from "./ReasonBadges";
import { TurnTraceItem } from "./TurnTraceItem";

/** One conversation as a sequence of turn traces - metadata only, never the text. */
export function ConversationTraceView({ id }: { id: string }) {
  const trace = useConversationTrace(id);

  if (trace.isPending)
    return (
      <Skeleton role="status" aria-busy="true" className="h-64 rounded-2xl">
        <span className="sr-only">Ładowanie rozmowy…</span>
      </Skeleton>
    );
  if (trace.data === undefined)
    return (
      <QueryError
        error={trace.error}
        fallback="Nie udało się pobrać rozmowy. Spróbuj ponownie."
        retrying={trace.isFetching}
        onRetry={() => trace.refetch()}
      />
    );

  const { conversation, turns } = trace.data;
  return (
    <div className="flex flex-col gap-6">
      <section
        aria-label="Rozmowa"
        className="flex flex-col gap-2 rounded-2xl border p-4 text-sm"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="font-medium">
            {dateTime.format(new Date(conversation.startedAt))} ·{" "}
            {conversation.endReason
              ? (END_REASONS[conversation.endReason] ?? conversation.endReason)
              : "otwarta"}
          </span>
          <ReasonBadges reasons={conversation.reasons} />
        </div>
        <dl className="grid gap-x-4 gap-y-1 text-muted-foreground sm:grid-cols-2">
          <div>
            <dt className="inline">Użytkownik (pseudonim): </dt>
            <dd className="inline font-mono">
              {conversation.userPseudonym.slice(0, 12)}…
            </dd>
          </div>
          <div>
            <dt className="inline">Ostatnia aktywność: </dt>
            <dd className="inline">
              {dateTime.format(new Date(conversation.lastActivityAt))}
            </dd>
          </div>
          <div>
            <dt className="inline">Konsultant: </dt>
            <dd className="inline">
              {conversation.hasCase
                ? "zgłoszenie otwarte"
                : conversation.handoffProposed
                  ? "zaproponowany, bez zgłoszenia"
                  : "nie"}
            </dd>
          </div>
          <div>
            <dt className="inline">Ruch: </dt>
            <dd className="inline">
              {conversation.synthetic
                ? `syntetyczny${conversation.runId ? ` (${conversation.runId})` : ""}`
                : "rzeczywisty"}
            </dd>
          </div>
        </dl>
      </section>

      <section aria-labelledby="turns" className="flex flex-col gap-3">
        <h2 id="turns" className="text-lg font-semibold">
          Tury ({turns.length})
        </h2>
        {turns.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Brak zapisanych tur (mogły wygasnąć według retencji).
          </p>
        ) : (
          <ol aria-label="Tury rozmowy" className="flex flex-col gap-3">
            {turns.map((turn, index) => (
              <TurnTraceItem key={turn.id} turn={turn} index={index + 1} />
            ))}
          </ol>
        )}
      </section>
    </div>
  );
}
