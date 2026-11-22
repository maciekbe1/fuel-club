"use client";

import { useMutation, type UseMutationOptions } from "@tanstack/react-query";
import { useRef } from "react";
import { ApiError } from "@/lib/api/problem";

export const IDEMPOTENCY_HEADER = "Idempotency-Key";

const STORAGE_PREFIX = "fuelclub.pending:";

type Pending<TVars> = { key: string; fingerprint: string; vars: TVars };

/**
 * An operation whose outcome is not known yet, kept in sessionStorage (this tab only) so leaving the
 * screen or reloading cannot drop the key and let a new attempt repeat the operation. Storage may be
 * unavailable (private mode, blocked); then the key lives only as long as the component.
 */
export function readPending<TVars>(name: string): Pending<TVars> | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_PREFIX + name);
    return raw ? (JSON.parse(raw) as Pending<TVars>) : null;
  } catch {
    return null;
  }
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

/** Called on login and logout: a pending key belongs to the account that created it. */
export function clearPendingOperations() {
  try {
    for (const key of Object.keys(sessionStorage))
      if (key.startsWith(STORAGE_PREFIX)) sessionStorage.removeItem(key);
  } catch {
    // Storage unavailable.
  }
}

/**
 * Keep the key when the outcome is unknown or transient: a retry must reuse it, so the server
 * replays the stored result or finishes the operation once. A final answer (a stored domain error,
 * a validation error, key reuse) ends the operation, and the next attempt is a new one.
 */
export function keepsIdempotencyKey(error: unknown) {
  if (error instanceof ApiError)
    return error.status >= 500 || error.code === "idempotency.in_progress";
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
      const stored = persist ? readPending<TVars>(persist) : null;
      if (!current.current && stored?.fingerprint === fingerprint)
        current.current = { key: stored.key, fingerprint };
      if (!current.current || current.current.fingerprint !== fingerprint) {
        current.current = { key: crypto.randomUUID(), fingerprint };
      }
      if (persist) writePending(persist, { ...current.current, vars });
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
