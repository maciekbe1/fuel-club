"use client";

import { HubConnectionState, type HubConnection } from "@microsoft/signalr";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/api/problem";
import { useIdempotentMutation } from "@/lib/idempotency";
import { queryKeys } from "@/lib/query-keys";
import {
  createSupportHub,
  OPEN_STATUSES,
  SUPPORT_PAGE_SIZE,
  supportService,
  type CreateSupportCase,
  type SupportCase,
  type SupportConflict,
  type SupportHubEvents,
  type SupportMessage,
  type SupportSide,
} from "./services";

/** Without a hub connection the screens read again this often (ADR 0014 fallback). */
const POLL_MS = 3000;
/**
 * With a connected hub, still read this often (ADR 0014, measured 2026-11-24): during a deploy the
 * WebSocket stays open on the old api instance, which no longer handles the new messages, so the
 * hub is silent without being disconnected. The read through the cursor bounds that silence.
 */
const CONNECTED_POLL_MS = 10_000;
/** "… pisze" is shown this long after the last signal. */
const TYPING_SHOWN_MS = 5000;
/** A typing signal is sent at most this often. */
const TYPING_SEND_MS = 2000;

type Typing = { caseId: string; author: string; until: number };

export type SupportHub = {
  connected: boolean;
  /** Who is typing in this case right now (other side only), or null. */
  typingIn: (caseId: string) => string | null;
  /** Call on every keystroke: throttled here, checked again by the server. */
  signalTyping: (caseId: string) => void;
};

/**
 * One hub connection for the screen. Events only invalidate queries: the content is always read
 * through the cursor, also after a reconnection (ADR 0014).
 */
export function useSupportHub(): SupportHub {
  const queryClient = useQueryClient();
  const [connected, setConnected] = useState(false);
  const [typing, setTyping] = useState<Typing[]>([]);
  const connection = useRef<HubConnection | null>(null);
  const lastSent = useRef(new Map<string, number>());

  useEffect(() => {
    const hub = createSupportHub();
    connection.current = hub;
    const refreshAll = () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.support.all });
    const clearTyping = (caseId: string) =>
      setTyping((all) => all.filter((t) => t.caseId !== caseId));

    hub.on("messageAdded", ({ caseId }: SupportHubEvents["messageAdded"]) => {
      clearTyping(caseId);
      void queryClient.invalidateQueries({
        queryKey: queryKeys.support.conversations,
      });
      void queryClient.invalidateQueries({
        queryKey: queryKeys.support.queue,
      });
    });
    hub.on("caseChanged", () => {
      void refreshAll();
    });
    hub.on("queueChanged", () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.support.queue });
    });
    hub.on("typing", ({ caseId, author }: SupportHubEvents["typing"]) =>
      setTyping((all) => [
        ...all.filter((t) => t.caseId !== caseId),
        { caseId, author, until: Date.now() + TYPING_SHOWN_MS },
      ]),
    );
    hub.onreconnecting(() => setConnected(false));
    hub.onreconnected(() => {
      setConnected(true);
      void refreshAll();
    });
    let stopped = false;
    let retry: ReturnType<typeof setTimeout> | undefined;
    // Automatic reconnection gives up after its last delay: start over, like the first connection.
    hub.onclose(() => {
      setConnected(false);
      if (!stopped) retry = setTimeout(start, 5000);
    });
    // The first connection is not covered by automatic reconnection: retry until unmounted.
    const start = () =>
      hub
        .start()
        .then(() => {
          if (stopped) return;
          setConnected(true);
          void refreshAll();
        })
        .catch(() => {
          if (!stopped) retry = setTimeout(start, 5000);
        });
    void start();
    return () => {
      stopped = true;
      clearTimeout(retry);
      connection.current = null;
      void hub.stop();
    };
  }, [queryClient]);

  // Expire typing statuses without a new signal.
  useEffect(() => {
    if (typing.length === 0) return;
    const next = Math.min(...typing.map((t) => t.until)) - Date.now();
    const timer = setTimeout(
      () => setTyping((all) => all.filter((t) => t.until > Date.now())),
      Math.max(next, 0) + 50,
    );
    return () => clearTimeout(timer);
  }, [typing]);

  const typingIn = useCallback(
    (caseId: string) =>
      typing.find((t) => t.caseId === caseId && t.until > Date.now())?.author ??
      null,
    [typing],
  );

  const signalTyping = useCallback((caseId: string) => {
    const hub = connection.current;
    if (hub?.state !== HubConnectionState.Connected) return;
    const now = Date.now();
    if (now - (lastSent.current.get(caseId) ?? 0) < TYPING_SEND_MS) return;
    lastSent.current.set(caseId, now);
    hub.invoke("Typing", caseId).catch(() => {});
  }, []);

  return { connected, typingIn, signalTyping };
}

export type Conversation = {
  status: string;
  lastSeq: number;
  items: SupportMessage[];
};

/**
 * The conversation read by cursor: each fetch asks only for messages after the last one held and
 * merges them. Refetched on hub events; polled while the hub is not connected.
 */
export function useConversation(
  side: SupportSide,
  caseId: string | null,
  hubConnected: boolean,
) {
  const queryClient = useQueryClient();
  const key = queryKeys.support.conversation(side, caseId ?? "");
  // Leaving the conversation drops it: a cached 403 from losing the case must not greet the next
  // claim of the same case.
  useEffect(
    () => () =>
      queryClient.removeQueries({
        queryKey: queryKeys.support.conversation(side, caseId ?? ""),
        exact: true,
      }),
    [queryClient, side, caseId],
  );
  return useQuery({
    queryKey: key,
    enabled: caseId !== null,
    queryFn: async (): Promise<Conversation> => {
      const held = queryClient.getQueryData<Conversation>(key);
      let after = held?.lastSeq ?? 0;
      const items = [...(held?.items ?? [])];
      let status = held?.status ?? "Waiting";
      for (;;) {
        const page = await supportService.messages(side, caseId!, after);
        status = page.status;
        for (const item of page.items)
          if (!items.some((m) => m.seq === item.seq)) items.push(item);
        after = page.items.at(-1)?.seq ?? after;
        if (page.items.length < SUPPORT_PAGE_SIZE) break;
      }
      items.sort((a, b) => a.seq - b.seq);
      return { status, lastSeq: after, items };
    },
    refetchInterval: (query) =>
      query.state.data && !OPEN_STATUSES.has(query.state.data.status)
        ? false
        : hubConnected
          ? CONNECTED_POLL_MS
          : POLL_MS,
    retry: (count, error) =>
      !(error instanceof ApiError && error.status < 500) && count < 2,
  });
}

export function useSendSupportMessage(side: SupportSide) {
  const queryClient = useQueryClient();
  return useIdempotentMutation<unknown, { caseId: string; text: string }>({
    mutationFn: ({ caseId, text }, key) =>
      supportService.send(side, caseId, text, key),
    onSettled: (_data, _error, { caseId }) =>
      queryClient.invalidateQueries({
        queryKey: queryKeys.support.conversation(side, caseId),
      }),
  });
}

// Customer

export function useCurrentCase(enabled = true) {
  return useQuery({
    queryKey: queryKeys.support.current,
    queryFn: supportService.current,
    enabled,
  });
}

/**
 * Opens the case. A second attempt after a lost answer gets 409 support.case_open with the id of
 * the case the first one created, so the key is not persisted.
 */
export function useCreateCase() {
  const queryClient = useQueryClient();
  return useIdempotentMutation<SupportCase, CreateSupportCase>({
    mutationFn: (body, key) => supportService.create(body, key),
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.support.current }),
  });
}

export function useCloseCase() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (caseId: string) => supportService.close(caseId),
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.support.all }),
  });
}

/** The case id from 409 support.case_open, if that is the error. */
export function openCaseIdOf(error: unknown) {
  if (error instanceof ApiError && error.code === "support.case_open") {
    return (error.problem as SupportConflict).caseId ?? null;
  }
  return null;
}

// Agent

export function useSupportQueue(hubConnected: boolean) {
  return useQuery({
    queryKey: queryKeys.support.queue,
    queryFn: supportService.queue,
    refetchInterval: hubConnected ? CONNECTED_POLL_MS : POLL_MS,
    retry: (count, error) =>
      !(error instanceof ApiError && error.status < 500) && count < 2,
  });
}

function useAgentAction(action: (caseId: string) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: action,
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.support.all }),
  });
}

export const useClaimCase = () => useAgentAction(supportService.claim);
export const useReleaseCase = () => useAgentAction(supportService.release);
export const useAgentCloseCase = () =>
  useAgentAction(supportService.agentClose);
