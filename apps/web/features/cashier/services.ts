import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type CashierStation = components["schemas"]["CashierStationResponse"];
export type FuelPurchaseBody = components["schemas"]["FuelPurchaseRequest"];
export type FuelPurchaseResult = components["schemas"]["FuelPurchaseResponse"];
export type CouponPreviewBody = components["schemas"]["CouponPreviewRequest"];
export type CouponPreview = components["schemas"]["CouponPreviewResponse"];

export const cashierService = {
  station: () => unwrap(api.GET("/api/cashier/station")),

  /** Read-only and limited per cashier (429): called on a complete code or an explicit "Przelicz". */
  previewCoupon: (body: CouponPreviewBody) =>
    unwrap(api.POST("/api/cashier/coupons/preview", { body })),

  purchase: (body: FuelPurchaseBody, key: string) =>
    unwrap(
      api.POST("/api/cashier/fuel-purchases", {
        body,
        params: { header: { "Idempotency-Key": key } },
      }),
    ),
};
