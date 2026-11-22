import { createParser } from "eventsource-parser";
import { api, unwrap } from "@/lib/api/client";
import { NetworkError, toApiError } from "@/lib/api/problem";
import type { components } from "@/lib/api/schema";

type Schemas = components["schemas"];
export type ChatStream = Schemas["ChatStreamContract"];
export type ChatEventName = keyof ChatStream;
export type ChatEvent = {
  [K in ChatEventName]: { name: K; data: ChatStream[K] };
}[ChatEventName];
export type ChatConfirmation = Schemas["ChatConfirmationEvent"];
export type ChatHistoryItem = Schemas["ChatHistoryItem"];
export type ChatToolEvent = Schemas["ChatToolEvent"];

const NAMES = new Set<string>([
  "meta",
  "tool",
  "delta",
  "confirmation",
  "done",
  "error",
]);

/** The stream ended without `done` or `error`: the answer is incomplete, never a success. */
export class StreamInterruptedError extends Error {
  constructor(cause?: unknown) {
    super("Chat stream interrupted", { cause });
    this.name = "StreamInterruptedError";
  }
}

/**
 * Reads an SSE body into typed events. Frames may arrive split across network chunks (or several in
 * one); the parser buffers until a frame is complete. Unknown events and comments (heartbeats) are
 * skipped.
 */
export async function readChatEvents(
  body: ReadableStream<Uint8Array>,
  onEvent: (event: ChatEvent) => void,
) {
  const pending: ChatEvent[] = [];
  const parser = createParser({
    onEvent: ({ event, data }) => {
      if (!event || !NAMES.has(event)) return;
      pending.push({ name: event, data: JSON.parse(data) } as ChatEvent);
    },
  });
  // stream: true keeps a multi-byte character split across chunks for the next one.
  const decoder = new TextDecoder();
  const reader = body.getReader();
  for (;;) {
    let chunk: ReadableStreamReadResult<Uint8Array>;
    try {
      chunk = await reader.read();
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError")
        throw error;
      throw new StreamInterruptedError(error);
    }
    if (chunk.done) break;
    try {
      parser.feed(decoder.decode(chunk.value, { stream: true }));
    } catch (error) {
      // A malformed frame: stop reading, so the connection (and the server's turn) ends too.
      await reader.cancel().catch(() => {});
      throw new StreamInterruptedError(error);
    }
    for (const event of pending.splice(0)) onEvent(event);
  }
}

export const assistantService = {
  /** One chat turn. Rejects before the stream with ApiError (400, 401, 429) or NetworkError. */
  async chat(
    message: string,
    history: ChatHistoryItem[],
    signal: AbortSignal,
    onEvent: (event: ChatEvent) => void,
  ) {
    const { data, error, response } = await api.POST("/api/chat", {
      body: { message, history },
      parseAs: "stream",
      signal,
    });
    if (!response.ok) throw toApiError(response.status, error);
    if (!data) throw new NetworkError(new Error("Empty chat response"));
    await readChatEvents(data as ReadableStream<Uint8Array>, onEvent);
  },

  confirm: (token: string, key: string) =>
    unwrap(
      api.POST("/api/chat/confirmations", {
        body: { token },
        params: { header: { "Idempotency-Key": key } },
      }),
    ),
};
