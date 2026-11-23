"use client";

import { Headset } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { openCaseIdOf, useCreateCase } from "../hooks";
import {
  SUPPORT_MESSAGE_MAX_LENGTH,
  type SupportHistoryItem,
} from "../services";
import { createError, SUPPORT_CONSENT } from "../texts";

/** Consent, the optional chat history (off by default) and the first message. */
export function SupportStartForm({
  history,
  stopAssistant,
  conversationId,
  onCancel,
  onStarted,
}: {
  history: () => SupportHistoryItem[];
  stopAssistant: () => void;
  conversationId: string | null;
  onCancel: () => void;
  onStarted: (caseId: string) => void;
}) {
  const create = useCreateCase();
  const [message, setMessage] = useState("");
  const [attach, setAttach] = useState(false);
  const hasHistory = history().length > 0;

  const submit = () => {
    const text = message.trim();
    if (!text || create.isPending) return;
    // The bot's answer in progress ends before the case exists.
    stopAssistant();
    const attached = attach ? history() : [];
    create.mutate(
      {
        message: text,
        history: attached.length > 0 ? attached : null,
        conversationId,
      },
      {
        onSuccess: (created) => onStarted(created.id),
        onError: (error) => {
          // A lost answer to an earlier attempt, or a case from another tab: show that one.
          const open = openCaseIdOf(error);
          if (open) onStarted(open);
        },
      },
    );
  };

  return (
    <form
      aria-label="Rozmowa z konsultantem"
      data-testid="support-start"
      className="flex flex-col gap-3 rounded-3xl border p-4"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <p className="flex items-center gap-2 font-medium">
        <Headset className="size-4 text-primary" /> Porozmawiaj z konsultantem
      </p>
      <p className="text-sm text-muted-foreground">{SUPPORT_CONSENT}</p>
      {hasHistory && (
        <Label className="flex items-start gap-2 text-sm font-normal">
          <Checkbox
            className="mt-0.5"
            checked={attach}
            onCheckedChange={setAttach}
          />
          Dołącz dotychczasową rozmowę z asystentem
        </Label>
      )}
      <Label htmlFor="support-first-message">W czym możemy pomóc?</Label>
      <Textarea
        id="support-first-message"
        rows={3}
        maxLength={SUPPORT_MESSAGE_MAX_LENGTH}
        value={message}
        onChange={(event) => setMessage(event.target.value)}
      />
      {create.isError && !openCaseIdOf(create.error) && (
        <p role="alert" className="text-sm">
          {createError(create.error)}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={!message.trim() || create.isPending}>
          {create.isPending ? "Łączenie…" : "Połącz z konsultantem"}
        </Button>
        <Button
          type="button"
          variant="outline"
          disabled={create.isPending}
          onClick={onCancel}
        >
          Anuluj
        </Button>
      </div>
    </form>
  );
}
