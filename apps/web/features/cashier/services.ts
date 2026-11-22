import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type CashierStation = components["schemas"]["CashierStationResponse"];
export type FuelPurchaseBody = components["schemas"]["FuelPurchaseRequest"];
export type FuelPurchaseResult = components["schemas"]["FuelPurchaseResponse"];
export type CouponPreviewBody = components["schemas"]["CouponPreviewRequest"];
export type CouponPreview = components["schemas"]["CouponPreviewResponse"];
export type RewardHandoverBody = components["schemas"]["RewardHandoverRequest"];
export type RewardHandoverResult =
  components["schemas"]["RewardHandoverResponse"];

export const cashierService = {
  station: () => unwrap(api.GET("/api/cashier/station")),

  /** Read-only and limited per cashier (429): called on a complete code or an explicit "Przelicz". */
  previewCoupon: (body: CouponPreviewBody) =>
    unwrap(api.POST("/api/cashier/coupons/preview", { body })),

  /** A product reward handed over at the till: no purchase, no points (ADR 0013). */
  handOverReward: (body: RewardHandoverBody, key: string) =>
    unwrap(
      api.POST("/api/cashier/reward-handovers", {
        body,
        params: { header: { "Idempotency-Key": key } },
      }),
    ),

  purchase: (body: FuelPurchaseBody, key: string) =>
    unwrap(
      api.POST("/api/cashier/fuel-purchases", {
        body,
        params: { header: { "Idempotency-Key": key } },
      }),
    ),
};
