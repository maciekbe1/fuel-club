"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useReducer, useRef } from "react";
import { ApiError } from "@/lib/api/problem";
import { useIdempotentMutation } from "@/lib/idempotency";
import { invalidates } from "@/lib/query-keys";
import {
  assistantService,
  type ChatConfirmation,
  type ChatEvent,
  type ChatHistoryItem,
  type ChatToolEvent,
} from "./services";

export const MAX_MESSAGE_LENGTH = 1000;
/** A support case is open: the chat and its confirmations are off (ADR 0012). */
export const HANDOFF_ACTIVE = "assistant.handoff_active";
/** The conversation ended (idle, or ended elsewhere) or is unknown to the API (e.g. after a restart). */
const CONVERSATION_GONE = new Set([
  "assistant.conversation_ended",
  "assistant.conversation_not_found",
]);
const MAX_HISTORY_ITEMS = 20;
const MAX_HISTORY_TEXT = 2000;

export type ToolCall = ChatToolEvent;

export type Turn = {
  id: number;
  question: string;
  tools: ToolCall[];
  text: string;
  confirmations: ChatConfirmation[];
  /** The model proposed talking to an agent (propose_handoff); nothing was handed over. */
  handoff: boolean;
  /** `done` only after the server's done event; anything else is not a successful answer. */
  status: "streaming" | "done" | "error" | "cancelled";
  error?: string;
  /** API error code of a failed turn (e.g. assistant.handoff_active). */
  errorCode?: string;
};

type State = { turns: Turn[]; demo: boolean | null };

type Action =
  | { type: "start"; id: number; question: string }
  | { type: "event"; id: number; event: ChatEvent }
  | { type: "fail"; id: number; message: string; code?: string }
  | { type: "cancel"; id: number };

function update(state: State, id: number, change: (turn: Turn) => Turn) {
  return {
    ...state,
    turns: state.turns.map((turn) =>
      turn.id === id && turn.status === "streaming" ? change(turn) : turn,
    ),
  };
}

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case "start":
      return {
        ...state,
        turns: [
          ...state.turns,
          {
            id: action.id,
            question: action.question,
            tools: [],
            text: "",
            confirmations: [],
            handoff: false,
            status: "streaming",
          },
        ],
      };
    case "cancel":
      return update(state, action.id, (turn) => ({
        ...turn,
        status: "cancelled",
      }));
    case "fail":
      return update(state, action.id, (turn) => ({
        ...turn,
        status: "error",
        error: action.message,
        errorCode: action.code,
      }));
    case "event": {
      const { event } = action;
      if (event.name === "meta") return { ...state, demo: event.data.demo };
      return update(state, action.id, (turn) => {
        switch (event.name) {
          case "tool":
            return { ...turn, tools: [...turn.tools, event.data] };
          case "delta":
            return { ...turn, text: turn.text + event.data.text };
          case "confirmation":
            return {
              ...turn,
              confirmations: [...turn.confirmations, event.data],
            };
          case "handoff":
            return { ...turn, handoff: true };
          case "done":
            return { ...turn, status: "done" };
          case "error":
            return {
              ...turn,
              status: "error",
              error: event.data.message,
              errorCode: event.data.code,
            };
        }
      });
    }
  }
}

/** Completed turns only: an interrupted or cancelled answer is not context for the next one. */
function historyOf(turns: Turn[]): ChatHistoryItem[] {
  return turns
    .filter((turn) => turn.status === "done" && turn.text.trim())
    .flatMap((turn): ChatHistoryItem[] => [
      { role: "user", text: turn.question.slice(0, MAX_HISTORY_TEXT) },
      { role: "assistant", text: turn.text.slice(0, MAX_HISTORY_TEXT) },
    ])
    .slice(-MAX_HISTORY_ITEMS);
}

function failureMessage(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 429)
      return "Za dużo pytań w krótkim czasie. Spróbuj za chwilę.";
    if (error.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
    if (error.code === HANDOFF_ACTIVE)
      return "Rozmowę prowadzi teraz konsultant - asystent jest wyłączony.";
    if (error.status === 403)
      return "Asystent jest dostępny tylko dla kont klientów.";
    if (error.status === 400 && error.problem.errors?.message)
      return `Wiadomość musi mieć od 1 do ${MAX_MESSAGE_LENGTH} znaków.`;
    if (error.status === 400)
      return "Nie udało się wysłać wcześniejszej rozmowy. Odśwież ekran i zapytaj ponownie.";
    return "Asystent jest chwilowo niedostępny. Spróbuj ponownie.";
  }
  return "Połączenie zostało przerwane - odpowiedź jest niepełna. Spróbuj ponownie.";
}

type ConversationSlot = { id: string | null; key: string };

const newSlot = (): ConversationSlot => ({
  id: null,
  key: crypto.randomUUID(),
});

/**
 * The turns live in this screen only (not cached, not stored). The server conversation (ADR 0017)
 * starts before the first turn with a key kept until it succeeds, so a retry after a lost response
 * gets the same one. One turn at a time; leaving the screen or pressing stop aborts the request,
 * and the server cancels the turn. Leaving the screen ends the conversation.
 */
export function useChat() {
  const [state, dispatch] = useReducer(reducer, { turns: [], demo: null });
  const active = useRef<AbortController | null>(null);
  const nextId = useRef(1);
  const conversation = useRef<ConversationSlot>(newSlot());
  const streaming = state.turns.some((turn) => turn.status === "streaming");

  useEffect(
    () => () => {
      active.current?.abort();
      // The latest conversation on purpose: the one this screen leaves.
      const id = conversation.current.id;
      if (id) void assistantService.endConversation(id).catch(() => {});
    },
    [],
  );

  const conversationId = useCallback(async () => {
    const slot = conversation.current;
    if (slot.id) return slot.id;
    const created = await assistantService.startConversation(slot.key);
    slot.id = created.id;
    return created.id;
  }, []);

  /** Starts a turn; false when it did not (empty, or another turn still runs), so the draft stays. */
  const send = useCallback(
    (question: string) => {
      const message = question.trim();
      if (!message || active.current) return false;
      const id = nextId.current++;
      const controller = new AbortController();
      active.current = controller;
      const release = () => {
        if (active.current === controller) active.current = null;
      };
      dispatch({ type: "start", id, question: message });
      let finished = false;
      const history = historyOf(state.turns);
      const attempt = async (retry: boolean): Promise<void> => {
        try {
          await assistantService.chat(
            await conversationId(),
            message,
            history,
            controller.signal,
            (event) => {
              if (event.name === "done" || event.name === "error") {
                finished = true;
                // The turn is over: the next one may start before the body closes.
                release();
              }
              dispatch({ type: "event", id, event });
            },
          );
        } catch (error) {
          // Refused before the model ran: safe to ask once more in a new conversation.
          if (
            retry &&
            error instanceof ApiError &&
            error.code &&
            CONVERSATION_GONE.has(error.code)
          ) {
            conversation.current = newSlot();
            return attempt(false);
          }
          throw error;
        }
      };
      const run = async () => {
        try {
          await attempt(true);
          if (!finished)
            dispatch({ type: "fail", id, message: failureMessage(null) });
        } catch (error) {
          if (finished) return;
          if (controller.signal.aborted) {
            dispatch({ type: "cancel", id });
            return;
          }
          // Close the request too, so the server stops the turn.
          controller.abort();
          dispatch({
            type: "fail",
            id,
            message: failureMessage(error),
            code: error instanceof ApiError ? error.code : undefined,
          });
        } finally {
          release();
        }
      };
      void run();
      return true;
    },
    [state.turns, conversationId],
  );

  const cancel = useCallback(() => active.current?.abort(), []);

  /** Completed turns as the history a support case may attach. */
  const history = useCallback(() => historyOf(state.turns), [state.turns]);

  return {
    turns: state.turns,
    demo: state.demo,
    streaming,
    send,
    cancel,
    history,
    /** The current conversation, for a support case to link (null before the first turn). */
    conversationId: () => conversation.current.id,
  };
}

/**
 * The button behind a confirmation event. Retrying with the same key replays the answer; a new key
 * with the same token is safe too (an active coupon keeps its code), so the key is not persisted.
 * The response (with the coupon code) is not shown here: the coupons screen reads it again.
 */
export function useConfirmActivation() {
  const queryClient = useQueryClient();
  return useIdempotentMutation<unknown, string>({
    mutationFn: (token, key) => assistantService.confirm(token, key),
    onSettled: () =>
      Promise.all(
        invalidates.activate.map((queryKey) =>
          queryClient.invalidateQueries({ queryKey }),
        ),
      ),
  });
}
