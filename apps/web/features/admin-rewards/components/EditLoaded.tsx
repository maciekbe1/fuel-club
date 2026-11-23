import { zodResolver } from "@hookform/resolvers/zod";
import Link from "next/link";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { FormAlert } from "@/components/form/FormAlert";
import { Alert } from "@/components/ui/alert";
import { Button, buttonVariants } from "@/components/ui/button";
import { ApiError, applyFieldErrors } from "@/lib/api/problem";
import { useFetchCurrentReward, useUpdateReward } from "../hooks";
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
import { ConflictPanel } from "./ConflictPanel";
import { FIELDS, isConflict, isFinal } from "./editRewardShared";

export type Problem =
  | { kind: "conflict"; current: AdminReward | null }
  | { kind: "unknown"; checking: boolean }
  | { kind: "not-saved" };

export function EditLoaded({ initial }: { initial: AdminReward }) {
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
        <Alert
          className="flex flex-col items-start gap-3 rounded-2xl p-4 text-sm"
          variant="warning"
        >
          <p>
            Wynik zapisu nieznany - zmiana mogła zostać zapisana. Sprawdź
            aktualny stan oferty, zanim zapiszesz ponownie.
          </p>
          <Button type="button" onClick={check} disabled={problem.checking}>
            {problem.checking ? "Sprawdzanie…" : "Sprawdź aktualny stan"}
          </Button>
        </Alert>
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
