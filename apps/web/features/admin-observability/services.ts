import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type ObservabilitySummary =
  components["schemas"]["ObservabilitySummary"];
export type AnomalyResult = components["schemas"]["AnomalyResult"];
export type ReviewItem = components["schemas"]["ReviewItem"];

export const observabilityService = {
  summary: (hours: number) =>
    unwrap(
      api.GET("/api/admin/observability", { params: { query: { hours } } }),
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
