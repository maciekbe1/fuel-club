"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useState, useSyncExternalStore } from "react";
import { useForm } from "react-hook-form";
import { FormAlert } from "@/components/form/FormField";
import { Button } from "@/components/ui/button";
import { ApiError, applyFieldErrors, NetworkError } from "@/lib/api/problem";
import { keepsIdempotencyKey, readPending } from "@/lib/idempotency";
import { formatPoints, formatTerms } from "@/lib/format";
import { CREATE_OPERATION, useCreateReward } from "../hooks";
import {
  emptyReward,
  rewardMessages,
  rewardSchema,
  toRewardBody,
  type RewardValues,
} from "../schemas";
import type { CreateRewardBody } from "../services";
import { RewardFields } from "./RewardFields";

const FIELDS = [
  "name",
  "description",
  "pointsCost",
  "discountPerLiter",
  "maxLiters",
  "isActive",
] as const;

const noSubscription = () => () => {};

/** The create left unfinished before this screen was (re)mounted, read after hydration. */
function usePendingCreate() {
  const raw = useSyncExternalStore(
    noSubscription,
    () =>
      JSON.stringify(
        readPending<CreateRewardBody>(CREATE_OPERATION)?.vars ?? null,
      ),
    () => "null",
  );
  return JSON.parse(raw) as CreateRewardBody | null;
}

/**
 * POST with an Idempotency-Key. After an unknown outcome the form is locked and only the same
 * request can be retried (same key): editing it would send a new key and could create a duplicate.
 * An unfinished create from an earlier visit (sessionStorage) locks the form the same way.
 */
export function CreateRewardForm() {
  const router = useRouter();
  const create = useCreateReward();
  const stored = usePendingCreate();
  // Cleared once its retry got a final answer (the hook removes it from storage).
  const [settled, setSettled] = useState(false);
  const restored = create.isIdle && !settled ? stored : null;
  const form = useForm<RewardValues>({
    resolver: zodResolver(rewardSchema),
    defaultValues: emptyReward,
  });
  const unknown =
    create.isError && keepsIdempotencyKey(create.error) ? create.error : null;
  const locked = create.isPending || unknown !== null || restored !== null;

  const send = async (body: CreateRewardBody) => {
    try {
      await create.mutateAsync(body);
      router.push("/admin/oferty");
    } catch (error) {
      if (keepsIdempotencyKey(error as Error)) return;
      setSettled(true);
      if (applyFieldErrors(error, form.setError, rewardMessages, FIELDS))
        return;
      form.setError("root", {
        message:
          error instanceof ApiError && error.status === 401
            ? "Sesja wygasła. Zaloguj się ponownie."
            : "Nie udało się utworzyć oferty.",
      });
    }
  };
  const submit = form.handleSubmit((values) => send(toRewardBody(values)));

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      <FormAlert message={form.formState.errors.root?.message} />
      {restored && (
        <div
          role="alert"
          className="flex flex-col items-start gap-3 rounded-2xl border border-amber-500/50 bg-amber-50 p-4 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-50"
        >
          <p>
            Tworzenie oferty „{restored.name}” (
            {formatPoints(restored.pointsCost ?? 0)},{" "}
            {formatTerms(
              restored.discountPerLiter ?? 0,
              restored.maxLiters ?? 0,
            )}
            ) mogło się nie zakończyć. Ponów: oferta nie zostanie utworzona dwa
            razy.
          </p>
          <Button type="button" onClick={() => send(restored)}>
            Ponów utworzenie
          </Button>
        </div>
      )}
      {unknown && (
        <div
          role="alert"
          className="flex flex-col items-start gap-3 rounded-2xl border border-amber-500/50 bg-amber-50 p-4 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-50"
        >
          <p>
            {unknown instanceof NetworkError
              ? "Nie dotarła odpowiedź serwera - nie wiadomo, czy oferta została utworzona."
              : "Nie wiadomo, czy oferta została utworzona."}{" "}
            Ponów: oferta nie zostanie utworzona dwa razy.
          </p>
          <Button type="submit" disabled={create.isPending}>
            Ponów utworzenie
          </Button>
        </div>
      )}
      <fieldset
        disabled={locked}
        className="flex flex-col gap-5 disabled:opacity-70"
      >
        <legend className="sr-only">Nowa oferta</legend>
        <RewardFields form={form} />
        <Button type="submit" size="lg" className="h-12">
          {create.isPending ? "Zapisywanie…" : "Utwórz ofertę"}
        </Button>
      </fieldset>
    </form>
  );
}
