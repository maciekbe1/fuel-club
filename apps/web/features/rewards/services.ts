import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type Reward = components["schemas"]["RewardResponse"];
export type ExchangeResult = components["schemas"]["ExchangeResponse"];

/** The offer and the version whose terms the customer confirmed; the name is only for messages. */
export type ExchangeVars = { id: string; version: number; name: string };

export const rewardsService = {
  list: () => unwrap(api.GET("/api/rewards")),

  exchange: ({ id, version }: ExchangeVars, key: string) =>
    unwrap(
      api.POST("/api/rewards/{id}/exchange", {
        params: { path: { id }, header: { "Idempotency-Key": key } },
        body: { version },
      }),
    ),
};
