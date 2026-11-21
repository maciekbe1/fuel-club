import { api, unwrap } from "@/lib/api/client";
import { ApiError } from "@/lib/api/problem";
import type { components } from "@/lib/api/schema";

export type LoyaltyCard = components["schemas"]["LoyaltyCardResponse"];

const idempotent = (key: string) => ({ params: { header: { "Idempotency-Key": key } } });

export const loyaltyCardService = {
  /** Current card, or null when none was issued (404 loyalty.card_missing). Never creates one. */
  async get(): Promise<LoyaltyCard | null> {
    try {
      return await unwrap(api.GET("/api/me/loyalty-card"));
    } catch (error) {
      if (error instanceof ApiError && error.code === "loyalty.card_missing") return null;
      throw error;
    }
  },

  issue: (key: string) => unwrap(api.POST("/api/me/loyalty-card", idempotent(key))),

  rotate: (key: string) => unwrap(api.POST("/api/me/loyalty-card/rotate", idempotent(key))),
};

/** Display form: groups of four characters. Copy and scanning use the raw code. */
export const formatCardCode = (code: string) => code.match(/.{1,4}/g)?.join(" ") ?? code;
