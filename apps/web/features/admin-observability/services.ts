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

  /** Idempotent by effect: marking twice keeps the first review. */
  markReviewed: (id: string) =>
    unwrap(
      api.POST("/api/admin/observability/review/{id}", {
        params: { path: { id } },
      }),
    ),
};
