import { EventSourceParserStream } from "eventsource-parser/stream";
import { getCsrfToken, CSRF_HEADER } from "./api/csrf";
import { toApiError } from "./api/problem";

export type SseMessage = { event: string; data: string; id?: string };

export async function* postSse(
  url: string,
  body: unknown,
  signal: AbortSignal,
): AsyncGenerator<SseMessage> {
  const response = await fetch(url, {
    method: "POST",
    credentials: "same-origin",
    headers: {
      "Content-Type": "application/json",
      Accept: "text/event-stream",
      [CSRF_HEADER]: await getCsrfToken(),
    },
    body: JSON.stringify(body),
    signal,
  });

  if (!response.ok || !response.body) {
    throw toApiError(response.status, await response.json().catch(() => null));
  }

  const reader = response.body
    .pipeThrough(new TextDecoderStream())
    .pipeThrough(new EventSourceParserStream())
    .getReader();

  let finished = false;
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      const message = {
        event: value.event ?? "message",
        data: value.data,
        id: value.id,
      };
      if (message.event === "done" || message.event === "error")
        finished = true;
      yield message;
    }
  } finally {
    reader.releaseLock();
  }

  if (!finished) throw new Error("Stream ended without done event");
}
