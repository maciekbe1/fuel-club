import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type Wallet = components["schemas"]["WalletResponse"];

export const walletService = {
  get: () => unwrap(api.GET("/api/me/wallet")),
};
