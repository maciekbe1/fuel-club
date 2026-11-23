import { Button } from "@/components/ui/button";
import { benefitTerms } from "@/lib/format";
import type { AdminReward } from "../services";

/** Someone else saved first: show their version; the admin decides, nothing is overwritten automatically. */
export function ConflictPanel({
  current,
  onReload,
  onOverwrite,
  onRetryRead,
}: {
  current: AdminReward | null;
  onReload: (current: AdminReward) => void;
  onOverwrite: (current: AdminReward) => void;
  onRetryRead: () => void;
}) {
  return (
    <div
      role="alert"
      data-testid="offer-conflict"
      className="flex flex-col items-start gap-3 rounded-2xl border border-amber-500/50 bg-amber-50 p-4 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-50"
    >
      <p className="font-medium">
        Oferta została zmieniona w międzyczasie. Twoje zmiany nie zostały
        zapisane - są nadal w formularzu.
      </p>
      {current ? (
        <>
          <dl className="grid gap-1">
            <dt className="font-medium">Aktualna wersja {current.version}:</dt>
            <dd data-testid="offer-current">
              {current.name} · {current.pointsCost} pkt ·{" "}
              {benefitTerms(current) ?? "nagroda"} ·{" "}
              {current.isActive ? "aktywna" : "wyłączona"}
            </dd>
          </dl>
          <div className="flex flex-wrap gap-2">
            <Button type="button" onClick={() => onReload(current)}>
              Wczytaj aktualną wersję
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOverwrite(current)}
            >
              Zapisz moje zmiany na wersji {current.version}
            </Button>
          </div>
        </>
      ) : (
        <>
          <p>Nie udało się odczytać aktualnej wersji.</p>
          <Button type="button" variant="outline" onClick={onRetryRead}>
            Odczytaj ponownie
          </Button>
        </>
      )}
    </div>
  );
}
