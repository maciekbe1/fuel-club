import {
  HttpTransportType,
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import type { components } from "@/lib/api/schema";

export type PointsAwarded =
  components["schemas"]["LoyaltyHubContract"]["pointsAwarded"];

/** How long the toast stays (ms); the customer may close it earlier. */
export const POINTS_TOAST_MS = 10_000;

/**
 * The loyalty hub (ADR 0021): the customer's own purchases, server to client only. Same origin
 * through the /api rewrite, authenticated by the session cookie. Reconnects by itself; an event
 * missed while disconnected is only a missed toast - balance and history are read by the API.
 */
export function createLoyaltyHub(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl("/api/loyalty/hub", {
      transport:
        HttpTransportType.WebSockets |
        HttpTransportType.ServerSentEvents |
        HttpTransportType.LongPolling,
      withCredentials: true,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.None)
    .build();
}
