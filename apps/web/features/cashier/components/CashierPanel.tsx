"use client";

import { Alert } from "@/components/ui/alert";
import { RetryButton } from "@/components/ui/retry-button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api/problem";
import { useCashierStation } from "../hooks";
import { PurchaseForm } from "./PurchaseForm";
import { usePurchaseFlow } from "./usePurchaseFlow";

/**
 * The purchase in progress (form values, mutation with its idempotency key, the body awaiting a retry)
 * lives here, above the station views: a failed station refresh must not drop an unfinished purchase.
 */
export function CashierPanel() {
  const station = useCashierStation();
  const flow = usePurchaseFlow();

  if (station.data === undefined && station.isPending)
    return (
      <Skeleton role="status" aria-busy="true" className="h-96 rounded-3xl">
        <span className="sr-only">Ładowanie stanowiska…</span>
      </Skeleton>
    );

  if (station.data === undefined) {
    const error = station.error!;
    const forbidden =
      error instanceof ApiError &&
      (error.status === 403 || error.status === 404);
    return (
      <Alert className="flex flex-col items-start gap-3 rounded-2xl p-5">
        <p>
          {forbidden
            ? "To konto nie ma przypisanego stanowiska kasjera."
            : "Nie udało się pobrać stanowiska. Spróbuj ponownie."}
        </p>
        {!forbidden && (
          <RetryButton
            busy={station.isFetching}
            onClick={() => station.refetch()}
          />
        )}
      </Alert>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      {station.isError && (
        <Alert
          className="flex flex-col items-start gap-2 rounded-2xl p-4 text-sm"
          variant="destructive"
        >
          <p>
            Nie udało się odświeżyć stanowiska. Pokazane dane mogą być
            nieaktualne; zakup w toku nie został utracony.
          </p>
          <RetryButton
            busy={station.isFetching}
            variant="outline"
            onClick={() => station.refetch()}
          >
            Odśwież
          </RetryButton>
        </Alert>
      )}
      <dl
        className="grid grid-cols-2 gap-3 rounded-2xl border bg-card p-4 text-sm"
        aria-label="Stanowisko"
      >
        <div>
          <dt className="text-muted-foreground">Stacja</dt>
          <dd data-testid="station-code" className="font-mono font-semibold">
            {station.data.stationCode}
          </dd>
        </div>
        <div>
          <dt className="text-muted-foreground">Kasa</dt>
          <dd className="font-mono font-semibold">
            {station.data.registerCode}
          </dd>
        </div>
      </dl>
      <PurchaseForm flow={flow} />
    </div>
  );
}
