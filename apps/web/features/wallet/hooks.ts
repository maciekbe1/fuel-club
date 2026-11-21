"use client";

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/query-keys";
import { walletService } from "./services";

/** Balance of the logged-in customer. Disabled for staff-only accounts, which get 403 from the API. */
export function useWallet({ enabled = true }: { enabled?: boolean } = {}) {
  return useQuery({ queryKey: queryKeys.wallet, queryFn: walletService.get, enabled });
}
