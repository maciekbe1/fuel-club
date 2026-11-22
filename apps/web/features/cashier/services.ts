import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type CashierStation = components["schemas"]["CashierStationResponse"];
export type FuelPurchaseBody = components["schemas"]["FuelPurchaseRequest"];
export type FuelPurchaseResult = components["schemas"]["FuelPurchaseResponse"];

export const cashierService = {
  station: () => unwrap(api.GET("/api/cashier/station")),

  purchase: (body: FuelPurchaseBody, key: string) =>
    unwrap(
      api.POST("/api/cashier/fuel-purchases", {
        body,
        params: { header: { "Idempotency-Key": key } },
      }),
    ),
};
