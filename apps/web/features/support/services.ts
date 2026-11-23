import {
  HttpTransportType,
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import { api, unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

type Schemas = components["schemas"];
export type SupportCase = Schemas["SupportCaseResponse"];
export type SupportMessage = Schemas["SupportMessageItem"];
export type SupportMessages = Schemas["SupportMessagesResponse"];
export type SupportHistoryItem = Schemas["SupportHistoryItem"];
export type SupportQueue = Schemas["SupportQueueResponse"];
export type SupportQueueItem = Schemas["SupportQueueItem"];
export type AgentCase = Schemas["AgentCaseResponse"];
export type SupportHubEvents = Schemas["SupportHubContract"];
export type CreateSupportCase = Schemas["CreateSupportCaseRequest"];
export type SupportConflict = Schemas["SupportConflictProblem"];

/** Who reads the conversation: the case owner or its assignee (different endpoints). */
export type SupportSide = "customer" | "agent";

/** Must match SupportRules on the API. */
export const SUPPORT_MESSAGE_MAX_LENGTH = 1000;
export const SUPPORT_PAGE_SIZE = 100;

export const OPEN_STATUSES = new Set(["Waiting", "Assigned"]);

export const supportService = {
  /** The customer's open case, or null (204). */
  current: async () =>
    (await unwrap(api.GET("/api/support/cases/current"))) ?? null,

  create: (body: CreateSupportCase, key: string) =>
    unwrap(
      api.POST("/api/support/cases", {
        body,
        params: { header: { "Idempotency-Key": key } },
      }),
    ),

  close: (id: string) =>
    unwrap(
      api.POST("/api/support/cases/{id}/close", { params: { path: { id } } }),
    ),

  messages: (side: SupportSide, id: string, after: number) =>
    side === "customer"
      ? unwrap(
          api.GET("/api/support/cases/{id}/messages", {
            params: { path: { id }, query: { after } },
          }),
        )
      : unwrap(
          api.GET("/api/support/agent/cases/{id}/messages", {
            params: { path: { id }, query: { after } },
          }),
        ),

  send: (side: SupportSide, id: string, text: string, key: string) =>
    side === "customer"
      ? unwrap(
          api.POST("/api/support/cases/{id}/messages", {
            body: { text },
            params: { path: { id }, header: { "Idempotency-Key": key } },
          }),
        )
      : unwrap(
          api.POST("/api/support/agent/cases/{id}/messages", {
            body: { text },
            params: { path: { id }, header: { "Idempotency-Key": key } },
          }),
        ),

  queue: () => unwrap(api.GET("/api/support/agent/queue")),

  claim: (id: string) =>
    unwrap(
      api.POST("/api/support/agent/cases/{id}/claim", {
        params: { path: { id } },
      }),
    ),

  release: (id: string) =>
    unwrap(
      api.POST("/api/support/agent/cases/{id}/release", {
        params: { path: { id } },
      }),
    ),

  agentClose: (id: string) =>
    unwrap(
      api.POST("/api/support/agent/cases/{id}/close", {
        params: { path: { id } },
      }),
    ),
};

/**
 * The support hub (ADR 0014): notifications and "typing" only, never message text. Same origin
 * through the /api rewrite, authenticated by the session cookie. Reconnects by itself; callers
 * read the cursor again after every (re)connection.
 */
export function createSupportHub(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl("/api/support/hub", {
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
