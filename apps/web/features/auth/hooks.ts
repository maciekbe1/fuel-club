"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
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

export function useLogout() {
  const queryClient = useQueryClient();
  const router = useRouter();
  return useMutation({
    mutationFn: authService.logout,
    onSuccess: () => {
      queryClient.clear();
      clearPendingOperations();
      router.replace("/login");
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
