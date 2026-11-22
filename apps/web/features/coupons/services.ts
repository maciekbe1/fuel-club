import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type Coupon = components["schemas"]["CouponResponse"];

export const couponsService = {
  list: () => unwrap(api.GET("/api/me/coupons")),

  activate: (id: string, key: string) =>
    unwrap(
      api.POST("/api/me/coupons/{id}/activate", {
        params: { path: { id }, header: { "Idempotency-Key": key } },
      }),
    ),
};

/** Display form: "K" and groups of four characters. Copying and scanning use the raw code. */
export const formatCouponCode = (code: string) =>
  `${code[0]} ${
    code
      .slice(1)
      .match(/.{1,4}/g)
      ?.join(" ") ?? ""
  }`.trim();
