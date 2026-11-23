"use client";

import { CircleAlert, Headset, Send } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { StickToBottom } from "use-stick-to-bottom";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/lib/api/problem";
import {
  useConversation,
  useSendSupportMessage,
  type SupportHub,
} from "../hooks";
import {
  OPEN_STATUSES,
  SUPPORT_MESSAGE_MAX_LENGTH,
  type SupportSide,
} from "../services";
import { sendError, statusText } from "../texts";
import { AttachedHistory } from "./AttachedHistory";
import { SupportMessageBubble } from "./SupportMessageBubble";

/**
 * One support conversation, for either side. Messages come only from the cursor; the hub tells when
 * to read it and who is typing.
 */
export function SupportConversation({
  side,
  caseId,
  hub,
  actions,
  onUnavailable,
}: {
  side: SupportSide;
  caseId: string;
  hub: SupportHub;
  /** Buttons for the header (close, release). */
  actions?: (status: string) => ReactNode;
  /** The conversation cannot be read any more (deleted, another assignee). */
  onUnavailable?: (error: ApiError) => void;
}) {
  const conversation = useConversation(side, caseId, hub.connected);
  const send = useSendSupportMessage(side);
  const [draft, setDraft] = useState("");
  const status = conversation.data?.status ?? null;
  const open = status !== null && OPEN_STATUSES.has(status);
  const items = conversation.data?.items ?? [];
  const typing = hub.typingIn(caseId);
  const otherTyping =
    open && typing === (side === "customer" ? "Support" : "Customer");

  const readError = conversation.error;
  useEffect(() => {
    if (readError instanceof ApiError && readError.status < 500)
      onUnavailable?.(readError);
  }, [readError, onUnavailable]);

  const submit = () => {
    const text = draft.trim();
    if (!text || send.isPending || !open) return;
    send.mutate({ caseId, text }, { onSuccess: () => setDraft("") });
  };

  return (
    <section
      aria-label={
        side === "customer" ? "Rozmowa z konsultantem" : "Rozmowa z klientem"
      }
      data-testid="support-conversation"
      data-status={status ?? "loading"}
      className="flex flex-col gap-3 rounded-3xl border p-4"
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <p className="flex items-center gap-2 font-medium">
          <Headset className="size-4 text-primary" />
          <span data-testid="support-status">
            {status ? statusText(side, status) : "Wczytywanie…"}
          </span>
          {!hub.connected && open && (
            <span className="text-xs font-normal text-muted-foreground">
              (odświeżanie co kilka sekund)
            </span>
          )}
        </p>
        {status && actions?.(status)}
      </header>

      {conversation.isError && !conversation.data && (
        <p role="alert" className="flex items-start gap-2 text-sm">
          <CircleAlert className="mt-0.5 size-4 shrink-0 text-destructive" />
          Nie udało się wczytać rozmowy.
        </p>
      )}

      {/* The library scrolls Content's own element: the limit goes there, not on the root. */}
      <StickToBottom className="relative">
        <StickToBottom.Content
          scrollClassName="max-h-[60vh]"
          role="log"
          aria-live="polite"
          aria-label="Wiadomości"
          className="flex flex-col gap-2"
        >
          <AttachedHistory
            side={side}
            items={items.filter((m) => m.clientProvidedHistory)}
          />
          {items
            .filter((m) => !m.clientProvidedHistory)
            .map((message) => (
              <SupportMessageBubble
                key={message.seq}
                side={side}
                message={message}
              />
            ))}
          {otherTyping && (
            <p
              data-testid="support-typing"
              className="text-sm text-muted-foreground"
            >
              {side === "customer" ? "Konsultant pisze…" : "Klient pisze…"}
            </p>
          )}
        </StickToBottom.Content>
      </StickToBottom>

      {open && (
        <form
          className="flex flex-col gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            submit();
          }}
        >
          {send.isError && (
            <p role="alert" className="text-sm">
              {sendError(side, send.error)}
            </p>
          )}
          <label htmlFor={`support-message-${side}`} className="sr-only">
            {side === "customer"
              ? "Wiadomość do konsultanta"
              : "Odpowiedź dla klienta"}
          </label>
          <div className="flex items-end gap-2">
            <Textarea
              id={`support-message-${side}`}
              rows={2}
              maxLength={SUPPORT_MESSAGE_MAX_LENGTH}
              value={draft}
              placeholder="Napisz wiadomość…"
              className="max-h-40"
              onChange={(event) => {
                setDraft(event.target.value);
                // Only once the case is assigned is there someone to tell.
                if (status === "Assigned" && event.target.value)
                  hub.signalTyping(caseId);
              }}
              onKeyDown={(event) => {
                if (
                  event.key === "Enter" &&
                  !event.shiftKey &&
                  !event.nativeEvent.isComposing
                ) {
                  event.preventDefault();
                  submit();
                }
              }}
            />
            <Button
              type="submit"
              size="lg"
              className="h-12"
              disabled={!draft.trim() || send.isPending}
            >
              <Send className="size-4" /> Wyślij
            </Button>
          </div>
        </form>
      )}
    </section>
  );
}
