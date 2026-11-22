"use client";

import { Bot, Check, CircleAlert, Send, Square, Wrench } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import {
  MAX_MESSAGE_LENGTH,
  useChat,
  type ToolCall,
  type Turn,
} from "../hooks";
import { ConfirmationCard } from "./ConfirmationCard";

const TOOL_LABELS: Record<string, string> = {
  get_my_balance: "Saldo punktów",
  list_available_rewards: "Dostępne oferty",
  list_my_coupons: "Twoje kupony",
  propose_coupon_activation: "Propozycja aktywacji",
};

const TOOL_STATUS: Record<string, string> = {
  ok: "sprawdzono",
  rejected: "odrzucono",
  error: "błąd",
};

const SUGGESTIONS = [
  "Ile mam punktów?",
  "Jakie są oferty?",
  "Pokaż moje kupony",
  "Aktywuj mój kupon",
];

export function ChatScreen() {
  const chat = useChat();
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
        {chat.turns.length === 0 && (
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
          <TurnView key={turn.id} turn={turn} />
        ))}
        <div ref={end} />
      </div>

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
          <textarea
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
            className="min-h-12 flex-1 resize-none rounded-xl border bg-transparent px-4 py-3 text-base shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
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
        <p className="text-right text-xs text-muted-foreground">
          {draft.length}/{MAX_MESSAGE_LENGTH}
        </p>
      </form>
    </div>
  );
}

function TurnView({ turn }: { turn: Turn }) {
  return (
    <section
      aria-label={`Pytanie: ${turn.question}`}
      data-testid="chat-turn"
      data-status={turn.status}
      className="flex flex-col gap-2"
    >
      <p className="max-w-[85%] self-end rounded-2xl rounded-br-sm bg-primary px-4 py-2 whitespace-pre-wrap text-primary-foreground">
        {turn.question}
      </p>
      <div className="flex max-w-[95%] flex-col gap-2 self-start">
        {turn.tools.length > 0 && (
          <ul aria-label="Użyte narzędzia" className="flex flex-wrap gap-1.5">
            {turn.tools.map((tool, index) => (
              <ToolChip key={index} tool={tool} />
            ))}
          </ul>
        )}
        {(turn.text || turn.status === "streaming") && (
          <div className="flex gap-2 rounded-2xl rounded-bl-sm bg-muted px-4 py-3">
            <Bot className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
            <p data-testid="chat-answer" className="whitespace-pre-wrap">
              {turn.text ||
                (turn.status === "streaming" && (
                  <span className="text-muted-foreground">Asystent pisze…</span>
                ))}
            </p>
          </div>
        )}
        {turn.confirmations.map((confirmation) => (
          <ConfirmationCard
            key={confirmation.proposalId}
            confirmation={confirmation}
            // Offered only once the answer finished; a failed turn's proposal is not offered.
            turnStatus={turn.status}
          />
        ))}
        {turn.status === "error" && (
          <p
            role="alert"
            className="flex items-start gap-2 rounded-2xl border border-destructive/40 px-4 py-2 text-sm"
          >
            <CircleAlert className="mt-0.5 size-4 shrink-0 text-destructive" />
            {turn.error}
          </p>
        )}
        {turn.status === "cancelled" && (
          <p className="text-sm text-muted-foreground">
            Zatrzymano - odpowiedź jest niepełna.
          </p>
        )}
      </div>
    </section>
  );
}

function ToolChip({ tool }: { tool: ToolCall }) {
  const label = TOOL_LABELS[tool.name] ?? "Nieznane narzędzie";
  const ok = tool.status === "ok";
  return (
    <li
      className={cn(
        "flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs",
        !ok && "border-amber-500/50 text-amber-900 dark:text-amber-100",
      )}
    >
      {ok ? <Check className="size-3" /> : <Wrench className="size-3" />}
      {label}: {TOOL_STATUS[tool.status] ?? tool.status}
    </li>
  );
}
