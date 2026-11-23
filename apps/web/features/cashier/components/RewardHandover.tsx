import { useState } from "react";
import { Button } from "@/components/ui/button";
import { ApiError, NetworkError } from "@/lib/api/problem";
import { shortDateTimeFormat as clock } from "@/lib/format";
import { keepsIdempotencyKey, keepsPendingOperation } from "@/lib/idempotency";
import {
  isCertainRefusal,
  useForgetCouponPreviews,
  useHandOverReward,
} from "../hooks";
import type { CashierStation, RewardsConflictProblem } from "../services";

/**
 * Answers where this attempt certainly handed nothing over. After an unknown outcome they say nothing
 * about the earlier attempt with the same key (the rate limiter runs before idempotency), so the
 * outcome stays unknown.
 */
export function busyMessage(error: unknown) {
  if (!isCertainRefusal(error)) return null;
  if (error instanceof ApiError && error.status === 429)
    return "Limit prób na tej kasie został wyczerpany. Nagroda nie została wydana - spróbuj za chwilę.";
  if (error instanceof ApiError && error.code === "loyalty.customer_busy")
    return "Trwa inna operacja tego klienta. Nagroda nie została wydana - spróbuj za chwilę.";
  return null;
}

/**
 * The current state of a handed-over product reward: when and where (station and till) the server
 * recorded it. A 409 says the handover is registered, not that an earlier attempt failed, nor that
 * the product was or was not physically given - even on this till. Null without a usable time.
 */
export function handedOverMessage(
  error: unknown,
  own: CashierStation | undefined,
) {
  if (!(error instanceof ApiError) || error.code !== "rewards.coupon_redeemed")
    return null;
  const problem = error.problem as RewardsConflictProblem;
  if (!problem.handedOverAt) return null;
  const at = new Date(problem.handedOverAt);
  if (Number.isNaN(at.getTime())) return null;
  const when = clock.format(at);
  const here =
    own !== undefined &&
    problem.stationCode === own.stationCode &&
    problem.registerCode === own.registerCode;
  if (here)
    return `Wydanie zostało już zarejestrowane ${when} na tej kasie. Przed przekazaniem produktu sprawdź, czy klient go otrzymał.`;
  const where = problem.stationCode
    ? ` na stanowisku ${problem.stationCode}${problem.registerCode ? `, kasa ${problem.registerCode}` : ""}`
    : "";
  return `Wydanie zostało już zarejestrowane ${when}${where}.`;
}

export function handoverError(error: unknown) {
  if (error instanceof ApiError && error.code === "rewards.coupon_redeemed")
    return "Wydanie tej nagrody zostało już zarejestrowane.";
  if (error instanceof ApiError && error.code === "rewards.coupon_not_found")
    return "Nie znaleziono aktywnej nagrody tego klienta o tym kodzie.";
  if (error instanceof ApiError && error.code === "loyalty.card_not_found")
    return "Nie znaleziono aktywnej karty o tym kodzie.";
  if (error instanceof ApiError && error.status === 401)
    return "Sesja wygasła. Zaloguj się ponownie.";
  return "Nie udało się wydać nagrody.";
}

/**
 * A product reward (ADR 0013): handed over instead of a purchase - no amount, no points. After an
 * unknown outcome this panel offers "Ponów wydanie" with the same key while it stays mounted. The
 * form is not locked for it: once the panel is gone (another code, a purchase attempt, a refreshed
 * preview) a new attempt has a new key, and the server's 409 with the time, station and till settles
 * it - a reward is never handed over twice.
 */
export function RewardHandover({
  offerName,
  cardCode,
  couponCode,
  locked,
  station,
  onDone,
}: {
  offerName: string;
  cardCode: string;
  couponCode: string;
  /** A purchase in flight or with an unknown outcome. */
  locked: boolean;
  station: CashierStation | undefined;
  onDone: () => void;
}) {
  const handOver = useHandOverReward();
  const forgetPreviews = useForgetCouponPreviews();
  // An attempt in this panel ended without a known outcome: until a final answer, a 429, 503 or
  // 401 on the retry leaves it unknown. Panel state only (owner's decision): it survives a route
  // hidden by Activity; once the pair leaves the form the server's 409 says when and where.
  const [uncertain, setUncertain] = useState(false);
  const error = handOver.isError ? handOver.error : null;
  const busy = error ? busyMessage(error) : null;
  const expired = error instanceof ApiError && error.status === 401;
  const unknown =
    error !== null && (uncertain || (keepsIdempotencyKey(error) && !busy));
  const refusal = unknown ? null : busy;
  // Final: the reward is collected. Shown as its state, with no button to hand it over again.
  const collected = unknown ? null : handedOverMessage(error, station);

  const send = async () => {
    try {
      await handOver.mutateAsync({ cardCode, couponCode });
      // The form is locked while it runs, so it still holds this pair; a route hidden by Activity
      // keeps the same form, so the late result belongs there too.
      forgetPreviews();
      onDone();
    } catch (failure) {
      // Shown below from the mutation state. A final answer stays in this panel: no preview
      // refetch, which would unmount it, hide the answer and spend the per-cashier preview limit.
      if (!keepsPendingOperation(failure)) setUncertain(false);
      else if (keepsIdempotencyKey(failure) && !isCertainRefusal(failure))
        setUncertain(true);
    }
  };

  return (
    <div
      data-testid="reward-handover"
      className="flex flex-col gap-3 rounded-xl bg-muted p-3 text-sm"
    >
      {collected ? (
        <p role="status" className="font-medium">
          <span className="font-semibold">{offerName}</span>: {collected}
        </p>
      ) : (
        <p>
          <span className="font-semibold">{offerName}</span>: nagroda do
          wydania. Nie zapisuj tankowania z tym kodem.
        </p>
      )}
      {refusal && (
        <p role="alert" className="text-destructive">
          {refusal}
        </p>
      )}
      {error && !unknown && !refusal && !collected && (
        <p role="alert" className="text-destructive">
          {handoverError(error)}
        </p>
      )}
      {unknown && (
        <p role="alert" className="font-medium">
          {error instanceof NetworkError
            ? "Nie dotarła odpowiedź serwera - nie wiadomo, czy nagroda została wydana."
            : "Nie wiadomo, czy nagroda została wydana."}{" "}
          {busy && "Serwer chwilowo odmówił ponowienia - spróbuj za chwilę. "}
          {expired && "Sesja wygasła - zaloguj się ponownie i ponów. "}
          Ponów: nagroda nie zostanie wydana dwa razy.
        </p>
      )}
      {!collected && (
        <Button
          type="button"
          className="self-start"
          disabled={locked || handOver.isPending}
          onClick={send}
        >
          {handOver.isPending
            ? "Wydawanie…"
            : unknown
              ? "Ponów wydanie"
              : "Wydaj nagrodę"}
        </Button>
      )}
    </div>
  );
}
