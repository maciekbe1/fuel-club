"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError } from "@/lib/api/problem";
import { clearPendingOperations } from "@/lib/idempotency";
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
    mutationFn: authService.login,
    onSuccess: () => {
      queryClient.clear();
      clearPendingOperations();
    },
  });
}

/**
 * A full page load, not a client navigation: Next keeps visited screens hidden in the DOM (with
 * their data, e.g. coupon codes), and the next person on this device must not get them.
 */
export function useLogout() {
  const queryClient = useQueryClient();
  const wipe = () => {
    queryClient.clear();
    clearPendingOperations();
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
    onSuccess: () => {
      queryClient.clear();
      clearPendingOperations();
    },
  });
}
