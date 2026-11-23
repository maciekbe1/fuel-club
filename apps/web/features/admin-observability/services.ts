import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type ObservabilitySummary =
  components["schemas"]["ObservabilitySummary"];
export type AnomalyResult = components["schemas"]["AnomalyResult"];
export type ReviewItem = components["schemas"]["ReviewItem"];
export type TurnMetrics = components["schemas"]["TurnMetrics"];
export type CostMetrics = components["schemas"]["CostMetrics"];
export type ConversationMetrics = components["schemas"]["ConversationMetrics"];
export type FilterOptions = components["schemas"]["FilterOptions"];

export type Traffic = "real" | "synthetic" | "all";
/** Unset fields are not sent: the API counts all values of that field. */
export type SummaryFilters = {
  traffic: Traffic;
  runId?: string;
  model?: string;
  promptVersion?: string;
};

export const observabilityService = {
  summary: (hours: number, filters: SummaryFilters) =>
    unwrap(
      api.GET("/api/admin/observability", {
        params: { query: { hours, ...filters } },
      }),
    ),

  queue: () => unwrap(api.GET("/api/admin/observability/review")),

  /** Idempotent by effect: marking twice keeps the first review. */
  markReviewed: (id: string) =>
    unwrap(
      api.POST("/api/admin/observability/review/{id}", {
        params: { path: { id } },
      }),
    ),
};
