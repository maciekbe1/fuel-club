"use client";

import { Headset, Send, Square } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { HANDOFF_ACTIVE, MAX_MESSAGE_LENGTH, useChat } from "../hooks";
import type { ChatHistoryItem } from "../services";
import { ChatTurn } from "./ChatTurn";

const SUGGESTIONS = [
  "Ile mam punktów?",
  "Jakie są oferty?",
  "Tankuję 40 l za 250 zł - który kupon wybrać?",
  "Dlaczego dostałem tyle punktów?",
  "Pokaż moje kupony",
  "Aktywuj mój kupon",
];

/**
 * @param onTalkToAgent The customer asked for an agent: completed turns (to attach, if they agree)
 *   and a way to stop the answer in progress before the case is created.
 * @param locked A support case is shown: no new questions, no confirmations.
 * @param onHandoffActive A turn was refused because a case is open (e.g. opened in another tab).
 */
export function ChatScreen({
  onTalkToAgent,
  locked = false,
  onHandoffActive,
}: {
  onTalkToAgent?: (history: () => ChatHistoryItem[], stop: () => void) => void;
  locked?: boolean;
  onHandoffActive?: () => void;
} = {}) {
  const chat = useChat();
  // Each refused turn, not just the first: a later case may be opened in another tab too.
  const refused = chat.turns.findLast(
    (turn) => turn.errorCode === HANDOFF_ACTIVE,
  )?.id;
  useEffect(() => {
    if (refused !== undefined) onHandoffActive?.();
  }, [refused, onHandoffActive]);
  const talkToAgent =
    onTalkToAgent && !locked
      ? () => onTalkToAgent(chat.history, chat.cancel)
      : undefined;
  const [draft, setDraft] = useState("");
  const end = useRef<HTMLDivElement>(null);
  const last = chat.turns.at(-1);

  useEffect(() => {
    end.current?.scrollIntoView({ block: "end" });
  }, [chat.turns.length, last?.text, last?.status]);

  const submit = (text = draft) => {
    if (chat.streaming || !chat.send(text)) return;
    // A suggestion leaves what the user was typing in place.
    if (text === draft) setDraft("");
  };

  return (
    <div className="flex flex-col gap-4">
      {chat.demo && (
        <p
          data-testid="chat-demo"
          className="rounded-2xl border border-dashed px-4 py-2 text-sm text-muted-foreground"
        >
          Tryb demo: odpowiedzi z prostych reguł, bez modelu AI.
        </p>
      )}

      <div
        role="log"
        aria-label="Rozmowa z asystentem"
        aria-live="polite"
        className="flex flex-col gap-4"
      >
        {chat.turns.length === 0 && !locked && (
          <div className="flex flex-col gap-3 rounded-3xl border p-5">
            <p className="text-muted-foreground">
              Zapytaj o saldo, oferty albo swoje kupony. Aktywację kuponu
              asystent tylko proponuje - zatwierdzasz ją przyciskiem.
            </p>
            <div className="flex flex-wrap gap-2">
              {SUGGESTIONS.map((suggestion) => (
                <Button
                  key={suggestion}
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => submit(suggestion)}
                >
                  {suggestion}
                </Button>
              ))}
            </div>
          </div>
        )}
        {chat.turns.map((turn) => (
          <ChatTurn
            key={turn.id}
            turn={turn}
            locked={locked}
            onTalkToAgent={talkToAgent}
          />
        ))}
        <div ref={end} />
      </div>

      {!locked && (
        <form
          className="sticky bottom-0 flex flex-col gap-2 bg-background pt-2 pb-4"
          onSubmit={(event) => {
            event.preventDefault();
            submit();
          }}
        >
          <label htmlFor="chat-message" className="sr-only">
            Wiadomość do asystenta
          </label>
          <div className="flex items-end gap-2">
            <Textarea
              id="chat-message"
              rows={2}
              maxLength={MAX_MESSAGE_LENGTH}
              value={draft}
              placeholder="Napisz pytanie…"
              onChange={(event) => setDraft(event.target.value)}
              onKeyDown={(event) => {
                // Not while an input method is composing a character.
                if (
                  event.key === "Enter" &&
                  !event.shiftKey &&
                  !event.nativeEvent.isComposing
                ) {
                  event.preventDefault();
                  submit();
                }
              }}
              className="min-h-12 flex-1 resize-none rounded-xl px-4 py-3"
            />
            {chat.streaming ? (
              <Button
                type="button"
                size="lg"
                variant="outline"
                className="h-12"
                onClick={chat.cancel}
              >
                <Square className="size-4" /> Zatrzymaj
              </Button>
            ) : (
              <Button
                type="submit"
                size="lg"
                className="h-12"
                disabled={!draft.trim()}
              >
                <Send className="size-4" /> Wyślij
              </Button>
            )}
          </div>
          <div className="flex items-center justify-between gap-2">
            {talkToAgent ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={talkToAgent}
              >
                <Headset className="size-4" /> Porozmawiaj z konsultantem
              </Button>
            ) : (
              <span />
            )}
            <p className="text-xs text-muted-foreground">
              {draft.length}/{MAX_MESSAGE_LENGTH}
            </p>
          </div>
        </form>
      )}
    </div>
  );
}
