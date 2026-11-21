import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type Transaction = components["schemas"]["TransactionItem"];
export type TransactionsPage = components["schemas"]["TransactionsResponse"];

export const HISTORY_PAGE_SIZE = 20;

export const historyService = {
  page: (cursor?: string) =>
    unwrap(api.GET("/api/me/transactions", { params: { query: { limit: HISTORY_PAGE_SIZE, cursor } } })),
};
