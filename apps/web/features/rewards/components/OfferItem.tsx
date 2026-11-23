import { Ticket } from "lucide-react";
import { Button } from "@/components/ui/button";
import { benefitTerms, formatPoints } from "@/lib/format";
import type { Reward } from "../services";

export function OfferItem({
  offer,
  confirming,
  confirmed,
  locked,
  pending,
  onSelect,
  onCancel,
  onConfirm,
}: {
  offer: Reward;
  confirming: boolean;
  /** Snapshot of the terms being confirmed: the request sends exactly this version. */
  confirmed: Reward | null;
  locked: boolean;
  pending: boolean;
  onSelect: () => void;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const headingId = `offer-${offer.id}`;
  return (
    <li
      aria-labelledby={headingId}
      data-testid="offer"
      data-offer-id={offer.id}
      className="flex flex-col gap-3 rounded-2xl border bg-card p-4"
    >
      <div className="flex items-start gap-3">
        <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-brand-orange/15 text-primary">
          <Ticket className="size-5" aria-hidden />
        </span>
        <div className="flex flex-1 flex-col gap-1">
          <h2 id={headingId} className="font-semibold">
            {offer.name}
          </h2>
          {offer.description && (
            <p className="text-sm text-muted-foreground">{offer.description}</p>
          )}
          <p className="text-sm">
            {benefitTerms(offer)
              ? `Rabat ${benefitTerms(offer)}`
              : "Nagroda do odbioru w kasie"}
          </p>
        </div>
        <span className="shrink-0 font-semibold" data-testid="offer-cost">
          {formatPoints(offer.pointsCost)}
        </span>
      </div>

      {!confirming ? (
        <Button
          variant="outline"
          className="self-start"
          onClick={onSelect}
          disabled={locked}
        >
          Wymień punkty
        </Button>
      ) : (
        <div
          role="group"
          aria-label="Potwierdzenie wymiany"
          className="flex flex-col gap-3 rounded-xl bg-muted p-3"
        >
          <p className="text-sm">
            Wymienisz <strong>{formatPoints(confirmed!.pointsCost)}</strong> na{" "}
            {benefitTerms(confirmed!)
              ? `kupon „${confirmed!.name}”: rabat ${benefitTerms(confirmed!)} przy jednym tankowaniu.`
              : `nagrodę „${confirmed!.name}”, do odbioru w kasie.`}
          </p>
          <div className="flex flex-wrap gap-2">
            <Button onClick={onConfirm} disabled={locked} autoFocus>
              {pending ? "Wymieniam…" : "Potwierdź wymianę"}
            </Button>
            <Button variant="outline" onClick={onCancel} disabled={locked}>
              Anuluj
            </Button>
          </div>
        </div>
      )}
    </li>
  );
}
