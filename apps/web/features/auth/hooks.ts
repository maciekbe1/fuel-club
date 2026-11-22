"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError } from "@/lib/api/problem";
import { retainPendingOperations } from "@/lib/idempotency";
import { queryKeys } from "@/lib/query-keys";
import { authService, type Me, type RegisterInput } from "./services";

export function useMe(initialData?: Me) {
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: authService.me,
    initialData,
  });
}

export function useLogin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: Parameters<typeof authService.login>[0]) => {
      await authService.login(input);
      // Unfinished operations of this account survive signing in again (expired session);
      // another account's are dropped. Not reading the account is not a failed login: the
      // entries stay bound to their owners anyway.
      try {
        retainPendingOperations((await authService.me()).id);
      } catch {
        // Kept; readPending still returns them only to their owner.
      }
    },
    onSuccess: () => queryClient.clear(),
  });
}

/**
 * A full page load, not a client navigation: Next keeps visited screens hidden in the DOM (with
 * their data, e.g. coupon codes), and the next person on this device must not get them.
 */
export function useLogout() {
  const queryClient = useQueryClient();
  // Unfinished operations stay bound to this account (another account never reads them, and the
  // next sign-in of another account drops them): logging out must not lose a key whose outcome
  // is unknown.
  const wipe = () => {
    queryClient.clear();
    window.location.replace("/login");
  };
  return useMutation({
    mutationFn: authService.logout,
    onSuccess: wipe,
    // An expired session (401) is logged out already; the screens still have to go.
    onError: (error) => {
      if (error instanceof ApiError && error.status === 401) wipe();
    },
  });
}

export class AutoLoginError extends Error {}

export function useRegister() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterInput) => {
      await authService.register(input);
      try {
        await authService.login({
          email: input.email,
          password: input.password,
        });
      } catch (error) {
        throw new AutoLoginError("Account created, auto-login failed", {
          cause: error,
        });
      }
    },
    onSuccess: async () => {
      queryClient.clear();
      try {
        retainPendingOperations((await authService.me()).id);
      } catch {
        // A new account has nothing pending; another account's entries cannot be read by it.
      }
    },
  });
}
