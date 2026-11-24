import { api, unwrap } from "@/lib/api/client";
import type { components, paths } from "@/lib/api/schema";

export type ObservabilitySummary =
  components["schemas"]["ObservabilitySummary"];
export type AnomalyResult = components["schemas"]["AnomalyResult"];
export type ReviewItem = components["schemas"]["ReviewItem"];
export type TurnMetrics = components["schemas"]["TurnMetrics"];
export type CostMetrics = components["schemas"]["CostMetrics"];
export type ConversationMetrics = components["schemas"]["ConversationMetrics"];
export type FilterOptions = components["schemas"]["FilterOptions"];
export type ConversationListItem =
  components["schemas"]["ConversationListItem"];
export type ConversationTrace = components["schemas"]["ConversationTrace"];
export type TurnTrace = components["schemas"]["TurnTrace"];

/** Page size of the conversation list; the API allows 1-100. */
export const CONVERSATION_PAGE_SIZE = 25;

export type ConversationFilters = {
  hours: number;
  traffic: Traffic;
  suspicious: boolean;
};

type SummaryQuery = NonNullable<
  paths["/api/admin/observability"]["get"]["parameters"]["query"]
>;
/** The values the API accepts for `traffic` (ADR 0018). */
export type Traffic = "real" | "synthetic" | "all";
/** The generated query without the window; unset fields count every value of that field. */
export type SummaryFilters = Omit<SummaryQuery, "hours" | "traffic"> & {
  traffic: Traffic;
};

export const observabilityService = {
  summary: (hours: number, filters: SummaryFilters) =>
    unwrap(
      api.GET("/api/admin/observability", {
        params: { query: { hours, ...filters } },
      }),
    ),

  queue: () => unwrap(api.GET("/api/admin/observability/review")),

  /** Keyset pages, newest first; a later row shows up after reloading the first page. */
  conversations: (filters: ConversationFilters, cursor?: string) =>
    unwrap(
      api.GET("/api/admin/observability/conversations", {
        params: {
          query: { ...filters, limit: CONVERSATION_PAGE_SIZE, cursor },
        },
      }),
    ),

  conversation: (id: string) =>
    unwrap(
      api.GET("/api/admin/observability/conversations/{id}", {
        params: { path: { id } },
      }),
    ),

  /** Idempotent by effect: marking twice keeps the first review. */
  markReviewed: (id: string) =>
    unwrap(
      api.POST("/api/admin/observability/review/{id}", {
        params: { path: { id } },
      }),
    ),
};
