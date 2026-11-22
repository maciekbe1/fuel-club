import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type AdminReward = components["schemas"]["AdminRewardResponse"];
export type CreateRewardBody = components["schemas"]["CreateRewardRequest"];
export type UpdateRewardBody = components["schemas"]["UpdateRewardRequest"];

export const adminRewardsService = {
  list: () => unwrap(api.GET("/api/admin/rewards")),

  get: (id: string) =>
    unwrap(api.GET("/api/admin/rewards/{id}", { params: { path: { id } } })),

  /** Idempotent: the same key for a retry of the same form after a lost response. */
  create: (body: CreateRewardBody, key: string) =>
    unwrap(
      api.POST("/api/admin/rewards", {
        body,
        params: { header: { "Idempotency-Key": key } },
      }),
    ),

  /** Not idempotent; guarded by `version` (409 rewards.offer_version_conflict when stale). */
  update: (id: string, body: UpdateRewardBody) =>
    unwrap(
      api.PUT("/api/admin/rewards/{id}", {
        body,
        params: { path: { id } },
      }),
    ),
};
