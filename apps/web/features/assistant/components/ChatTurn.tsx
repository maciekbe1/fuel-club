"use client";

import { CircleAlert, Headset } from "lucide-react";
import { Message, MessageContent } from "@/components/ai-elements/message";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import type { Turn } from "../hooks";
import { AssistantAnswer } from "./AssistantAnswer";
import { ConfirmationCard } from "./ConfirmationCard";
import { ToolChip } from "./ToolChip";

export function ChatTurn({
  turn,
  locked,
  onTalkToAgent,
}: {
  turn: Turn;
  locked: boolean;
  onTalkToAgent?: () => void;
}) {
  return (
    <section
      aria-label={`Pytanie: ${turn.question}`}
      data-testid="chat-turn"
      data-status={turn.status}
      className="flex flex-col gap-2"
    >
      <Message from="user">
        <MessageContent className="break-words whitespace-pre-wrap">
          {turn.question}
        </MessageContent>
      </Message>
      <Message from="assistant">
        {turn.tools.length > 0 && (
          <ul aria-label="Użyte narzędzia" className="flex flex-wrap gap-1.5">
            {turn.tools.map((tool, index) => (
              <ToolChip key={index} tool={tool} />
            ))}
          </ul>
        )}
        {(turn.text || turn.status === "streaming") && (
          <MessageContent data-testid="chat-answer">
            {turn.text ? (
              <AssistantAnswer text={turn.text} />
            ) : (
              <span className="text-muted-foreground">Asystent pisze…</span>
            )}
          </MessageContent>
        )}
        {turn.confirmations.map((confirmation) => (
          <ConfirmationCard
            key={confirmation.proposalId}
            confirmation={confirmation}
            // Offered only once the answer finished; a failed turn's proposal is not offered.
            turnStatus={turn.status}
            locked={locked}
          />
        ))}
        {turn.handoff && turn.status === "done" && (
          <div
            data-testid="chat-handoff"
            className="flex flex-col gap-2 rounded-2xl border px-4 py-3 text-sm"
          >
            <p>Asystent proponuje rozmowę z konsultantem.</p>
            {onTalkToAgent && (
              <Button
                type="button"
                size="sm"
                className="self-start"
                onClick={onTalkToAgent}
              >
                <Headset className="size-4" /> Porozmawiaj z konsultantem
              </Button>
            )}
          </div>
        )}
        {turn.status === "error" && (
          <Alert
            className="flex items-start gap-2 rounded-2xl px-4 py-2 text-sm"
            variant="destructive"
          >
            <CircleAlert className="mt-0.5 size-4 shrink-0 text-destructive" />
            {turn.error}
          </Alert>
        )}
        {turn.status === "cancelled" && (
          <p className="text-sm text-muted-foreground">
            Zatrzymano - odpowiedź jest niepełna.
          </p>
        )}
      </Message>
    </section>
  );
}
