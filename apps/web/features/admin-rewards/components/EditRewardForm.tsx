"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { FormAlert } from "@/components/form/FormField";
import { Button, buttonVariants } from "@/components/ui/button";
import { RetryButton } from "@/components/ui/retry-button";
import {
  ApiError,
  applyFieldErrors,
  readErrorMessage,
} from "@/lib/api/problem";
import { benefitTerms } from "@/lib/format";
import {
  useAdminReward,
  useFetchCurrentReward,
  useUpdateReward,
} from "../hooks";
import {
  rewardMessages,
  rewardSchema,
  sameOffer,
  toRewardBody,
  toRewardValues,
  type RewardValues,
} from "../schemas";
import type { AdminReward } from "../services";
import { RewardFields } from "./RewardFields";

const FIELDS = [
  "name",
  "description",
  "pointsCost",
  "kind",
  "discountPerLiter",
  "maxLiters",
  "isActive",
] as const;

const isConflict = (error: unknown) =>
  error instanceof ApiError && error.code === "rewards.offer_version_conflict";

/** Validation (400), conflict (409), missing offer (404) and 401 are final; anything else is unknown. */
const isFinal = (error: unknown) =>
  error instanceof ApiError && [400, 401, 403, 404, 409].includes(error.status);

export function EditRewardForm({ id }: { id: string }) {
  const offer = useAdminReward(id);

  if (offer.isPending)
    return (
      <div
        role="status"
        aria-busy="true"
        className="h-96 animate-pulse rounded-3xl bg-muted"
      >
        <span className="sr-only">Ładowanie oferty…</span>
      </div>
    );

  if (offer.data === undefined)
    return (
      <div
        role="alert"
        className="flex flex-col items-start gap-3 rounded-2xl border p-5"
      >
        <p>
          {offer.error instanceof ApiError && offer.error.status === 404
            ? "Nie ma takiej oferty."
            : readErrorMessage(
                offer.error,
                "Nie udało się pobrać oferty. Spróbuj ponownie.",
              )}
        </p>
        <RetryButton busy={offer.isFetching} onClick={() => offer.refetch()} />
      </div>
    );

  // Loaded once: later refetches (after saving) must not reset what the admin is typing.
  return <EditLoaded initial={offer.data} />;
}

type Problem =
  | { kind: "conflict"; current: AdminReward | null }
  | { kind: "unknown"; checking: boolean }
  | { kind: "not-saved" };

function EditLoaded({ initial }: { initial: AdminReward }) {
  const update = useUpdateReward(initial.id);
  const fetchCurrent = useFetchCurrentReward();
  // The version the form is based on: sent with PUT, changed only by a known outcome or a reload.
  const [base, setBase] = useState(initial);
  const [problem, setProblem] = useState<Problem | null>(null);
  const [saved, setSaved] = useState(false);
  const form = useForm<RewardValues>({
    resolver: zodResolver(rewardSchema),
    defaultValues: toRewardValues(initial),
  });

  const loadCurrent = async () => {
    try {
      return await fetchCurrent(initial.id);
    } catch {
      return null;
    }
  };

  const save = async (values: RewardValues, version: number) => {
    setSaved(false);
    setProblem(null);
    try {
      const stored = await update.mutateAsync({
        ...toRewardBody(values),
        version,
      });
      setBase(stored);
      form.reset(toRewardValues(stored));
      setSaved(true);
    } catch (error) {
      if (isConflict(error)) {
        setProblem({ kind: "conflict", current: await loadCurrent() });
        return;
      }
      if (!isFinal(error)) {
        setProblem({ kind: "unknown", checking: false });
        return;
      }
      if (applyFieldErrors(error, form.setError, rewardMessages, FIELDS))
        return;
      form.setError("root", {
        message:
          error instanceof ApiError && error.status === 404
            ? "Oferta nie istnieje."
            : error instanceof ApiError && error.status === 401
              ? "Sesja wygasła. Zaloguj się ponownie."
              : "Nie udało się zapisać oferty.",
      });
    }
  };

  /** After an unknown outcome: read the stored offer and decide, never overwrite blindly. */
  const check = async () => {
    setProblem({ kind: "unknown", checking: true });
    const current = await loadCurrent();
    if (!current) {
      setProblem({ kind: "unknown", checking: false });
      return;
    }
    const values = form.getValues();
    if (current.version === base.version + 1 && sameOffer(current, values)) {
      setBase(current);
      form.reset(toRewardValues(current));
      setProblem(null);
      setSaved(true);
    } else if (current.version === base.version) {
      setProblem({ kind: "not-saved" });
    } else {
      setProblem({ kind: "conflict", current });
    }
  };

  const locked =
    update.isPending ||
    problem?.kind === "unknown" ||
    problem?.kind === "conflict";

  return (
    <form
      onSubmit={form.handleSubmit((values) => save(values, base.version))}
      noValidate
      className="flex flex-col gap-5"
    >
      <p className="text-sm text-muted-foreground">
        Wersja {base.version}. Zmiana warunków nie zmienia kupionych kuponów.
      </p>
      <FormAlert message={form.formState.errors.root?.message} />

      {saved && !form.formState.isDirty && (
        <p
          role="status"
          className="rounded-2xl border border-emerald-600/30 bg-emerald-50 p-4 text-sm font-medium text-emerald-900 dark:bg-emerald-950 dark:text-emerald-100"
        >
          Zapisano (wersja {base.version}).
        </p>
      )}

      {problem?.kind === "unknown" && (
        <div
          role="alert"
          className="flex flex-col items-start gap-3 rounded-2xl border border-amber-500/50 bg-amber-50 p-4 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-50"
        >
          <p>
            Wynik zapisu nieznany - zmiana mogła zostać zapisana. Sprawdź
            aktualny stan oferty, zanim zapiszesz ponownie.
          </p>
          <Button type="button" onClick={check} disabled={problem.checking}>
            {problem.checking ? "Sprawdzanie…" : "Sprawdź aktualny stan"}
          </Button>
        </div>
      )}

      {problem?.kind === "not-saved" && (
        <p role="status" className="rounded-2xl border p-4 text-sm">
          Zmiana nie została zapisana (oferta ma nadal wersję {base.version}).
          Możesz zapisać ponownie.
        </p>
      )}

      {problem?.kind === "conflict" && (
        <ConflictPanel
          current={problem.current}
          onReload={(current) => {
            setBase(current);
            form.reset(toRewardValues(current));
            setProblem(null);
          }}
          onOverwrite={(current) => {
            setBase(current);
            setProblem(null);
            void save(form.getValues(), current.version);
          }}
          onRetryRead={async () =>
            setProblem({ kind: "conflict", current: await loadCurrent() })
          }
        />
      )}

      <fieldset
        disabled={locked}
        className="flex flex-col gap-5 disabled:opacity-70"
      >
        <legend className="sr-only">Edycja oferty</legend>
        <RewardFields form={form} kindLocked />
        <div className="flex flex-wrap gap-2">
          <Button type="submit" size="lg" className="h-12">
            {update.isPending ? "Zapisywanie…" : "Zapisz zmiany"}
          </Button>
          <Link
            href="/admin/oferty"
            className={buttonVariants({ variant: "outline", size: "lg" })}
          >
            Wróć do listy
          </Link>
        </div>
      </fieldset>
    </form>
  );
}

/** Someone else saved first: show their version; the admin decides, nothing is overwritten automatically. */
function ConflictPanel({
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
