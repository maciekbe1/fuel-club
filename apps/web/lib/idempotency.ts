"use client";

import { useMutation, type UseMutationOptions } from "@tanstack/react-query";
import { useRef } from "react";
import { ApiError } from "@/lib/api/problem";

export const IDEMPOTENCY_HEADER = "Idempotency-Key";

/**
 * Keep the key when the outcome is unknown or transient: a retry must reuse it, so the server
 * replays the stored result or finishes the operation once. A final answer (a stored domain error,
 * a validation error, key reuse) ends the operation, and the next attempt is a new one.
 */
export function keepsIdempotencyKey(error: unknown) {
  if (error instanceof ApiError) return error.status >= 500 || error.code === "idempotency.in_progress";
  // NetworkError (no response, e.g. lost after the server committed) and anything unexpected.
  return true;
}

type Options<TData, TVars> = Omit<UseMutationOptions<TData, Error, TVars>, "mutationFn"> & {
  mutationFn: (vars: TVars, idempotencyKey: string) => Promise<TData>;
};

export function useIdempotentMutation<TData, TVars = void>({
  mutationFn,
  onSuccess,
  onError,
  ...options
}: Options<TData, TVars>) {
  const current = useRef<{ key: string; fingerprint: string } | null>(null);

  return useMutation<TData, Error, TVars>({
    ...options,
    mutationFn: (vars) => {
      // `?? null` so that calls without variables still get a fingerprint and a key.
      const fingerprint = JSON.stringify(vars ?? null);
      if (!current.current || current.current.fingerprint !== fingerprint) {
        current.current = { key: crypto.randomUUID(), fingerprint };
      }
      return mutationFn(vars, current.current.key);
    },
    onSuccess: (...args) => {
      current.current = null;
      return onSuccess?.(...args);
    },
    onError: (...args) => {
      if (!keepsIdempotencyKey(args[0])) current.current = null;
      return onError?.(...args);
    },
  });
}
