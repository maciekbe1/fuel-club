"use client";

import {
  useMutation,
  useQueryClient,
  type QueryClient,
  type UseMutationOptions,
} from "@tanstack/react-query";
import { useRef, useSyncExternalStore } from "react";
import { ApiError } from "@/lib/api/problem";
import type { components } from "@/lib/api/schema";
import { queryKeys } from "@/lib/query-keys";

export const IDEMPOTENCY_HEADER = "Idempotency-Key";

const STORAGE_PREFIX = "fuelclub.pending:";

type Pending<TVars> = {
  key: string;
  fingerprint: string;
  vars: TVars;
  /** The account that started it: only that account gets it back. */
  owner: string;
};

type Me = components["schemas"]["MeResponse"];

/** The signed-in account from the cache (AppShell fills it on every app screen). */
function currentOwner(queryClient: QueryClient) {
  return queryClient.getQueryData<Me>(queryKeys.me)?.id ?? null;
}

/**
 * An operation whose outcome is not known yet, kept in sessionStorage (this tab only) so leaving the
 * screen, reloading or signing in again after an expired session cannot drop the key and let a new
 * attempt repeat the operation. Bound to the account: another account in this tab never gets it.
 * Storage may be unavailable (private mode, blocked); then the key lives only as long as the component.
 */
export function readPending<TVars>(
  name: string,
  owner: string | null,
): Pending<TVars> | null {
  if (!owner) return null;
  try {
    const raw = sessionStorage.getItem(STORAGE_PREFIX + name);
    const stored = raw ? (JSON.parse(raw) as Pending<TVars>) : null;
    return stored?.owner === owner ? stored : null;
  } catch {
    return null;
  }
}

const noSubscription = () => () => {};

/** The variables of the signed-in account's unfinished operation, read after hydration. */
export function usePendingVars<TVars>(name: string): TVars | null {
  const queryClient = useQueryClient();
  const raw = useSyncExternalStore(
    noSubscription,
    () =>
      JSON.stringify(
        readPending<TVars>(name, currentOwner(queryClient))?.vars ?? null,
      ),
    () => "null",
  );
  return JSON.parse(raw) as TVars | null;
}

function writePending<TVars>(name: string, pending: Pending<TVars> | null) {
  try {
    if (pending)
      sessionStorage.setItem(STORAGE_PREFIX + name, JSON.stringify(pending));
    else sessionStorage.removeItem(STORAGE_PREFIX + name);
  } catch {
    // Storage unavailable: nothing to keep or clear.
  }
}

/**
 * Called after signing in: drops other accounts' unfinished operations and keeps this account's,
 * so a retry after an expired session still reuses the key.
 */
export function retainPendingOperations(owner: string) {
  try {
    for (const key of Object.keys(sessionStorage)) {
      if (!key.startsWith(STORAGE_PREFIX)) continue;
      let stored: Partial<Pending<unknown>> | null = null;
      try {
        stored = JSON.parse(sessionStorage.getItem(key) ?? "null");
      } catch {
        // Unreadable: drop it.
      }
      if (stored?.owner !== owner) sessionStorage.removeItem(key);
    }
  } catch {
    // Storage unavailable.
  }
}

/**
 * Keep the key when the outcome is unknown or transient: a retry must reuse it, so the server
 * replays the stored result or finishes the operation once. A final answer (a stored domain error,
 * a validation error, key reuse) ends the operation, and the next attempt is a new one. A 429 is
 * transient: the limit runs before the replay, so a retry of a committed operation can get it.
 */
export function keepsIdempotencyKey(error: unknown) {
  if (error instanceof ApiError)
    return (
      error.status >= 500 ||
      error.status === 429 ||
      error.code === "idempotency.in_progress"
    );
  // NetworkError (no response, e.g. lost after the server committed) and anything unexpected.
  return true;
}

type Options<TData, TVars> = Omit<
  UseMutationOptions<TData, Error, TVars>,
  "mutationFn"
> & {
  mutationFn: (vars: TVars, idempotencyKey: string) => Promise<TData>;
  /** Keeps the key of an unfinished operation in sessionStorage under this name (see readPending). */
  persist?: string;
};

export function useIdempotentMutation<TData, TVars = void>({
  mutationFn,
  onSuccess,
  onError,
  persist,
  ...options
}: Options<TData, TVars>) {
  const queryClient = useQueryClient();
  const current = useRef<{ key: string; fingerprint: string } | null>(null);
  const finish = () => {
    current.current = null;
    if (persist) writePending(persist, null);
  };

  return useMutation<TData, Error, TVars>({
    ...options,
    mutationFn: (vars) => {
      // `?? null` so that calls without variables still get a fingerprint and a key.
      const fingerprint = JSON.stringify(vars ?? null);
      // After a remount the unfinished operation comes back from storage with its key.
      // Unknown account (should not happen inside the app): the key is not persisted.
      const owner = persist ? currentOwner(queryClient) : null;
      const stored = owner ? readPending<TVars>(persist!, owner) : null;
      if (!current.current && stored?.fingerprint === fingerprint)
        current.current = { key: stored.key, fingerprint };
      if (!current.current || current.current.fingerprint !== fingerprint) {
        current.current = { key: crypto.randomUUID(), fingerprint };
      }
      if (persist && owner)
        writePending(persist, { ...current.current, vars, owner });
      return mutationFn(vars, current.current.key);
    },
    onSuccess: (...args) => {
      finish();
      return onSuccess?.(...args);
    },
    onError: (...args) => {
      if (!keepsIdempotencyKey(args[0])) finish();
      return onError?.(...args);
    },
  });
}
