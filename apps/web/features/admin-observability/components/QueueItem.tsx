import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { ReviewItem } from "../services";
import { REASONS, dateTime, ms } from "./labels";

export function QueueItem({
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
            <Badge
              key={reason}
              className="bg-amber-100 font-normal text-amber-950 dark:bg-amber-900 dark:text-amber-50"
            >
              {REASONS[reason] ?? reason}
            </Badge>
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
