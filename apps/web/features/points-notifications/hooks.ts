"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { toast } from "sonner";
import { queryKeys } from "@/lib/query-keys";
import { pointsMessage } from "./points-message";
import {
  createLoyaltyHub,
  POINTS_TOAST_MS,
  type PointsAwarded,
} from "./services";

/** What a purchase can change for the customer: balance, history and a redeemed coupon. */
const PURCHASE_KEYS = [
  queryKeys.wallet,
  queryKeys.transactions,
  queryKeys.coupons,
] as const;

/**
 * While mounted: a toast for each purchase the cashier registers for this customer, and a fresh
 * balance, history and coupons (the event says only what changed, the API stays the source of
 * truth). Keeps the connection up like the support hub: retries the first start, starts over when
 * automatic reconnection gives up, and re-reads after a reconnection (an event may have been missed).
 */
export function usePointsAwardedToasts() {
  const client = useQueryClient();
  useEffect(() => {
    const hub = createLoyaltyHub();
    const refresh = () => {
      for (const queryKey of PURCHASE_KEYS)
        void client.invalidateQueries({ queryKey });
    };
    hub.on("pointsAwarded", (event: PointsAwarded) => {
      const { title, description } = pointsMessage(event);
      toast.success(title, {
        id: event.purchaseId,
        description,
        duration: POINTS_TOAST_MS,
        closeButton: true,
      });
      refresh();
    });
    hub.onreconnected(refresh);
    let stopped = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    hub.onclose(() => {
      if (!stopped) retry = setTimeout(start, 5000);
    });
    const start = () =>
      hub.start().catch(() => {
        if (!stopped) retry = setTimeout(start, 5000);
      });
    void start();
    return () => {
      stopped = true;
      clearTimeout(retry);
      void hub.stop();
    };
  }, [client]);
}
