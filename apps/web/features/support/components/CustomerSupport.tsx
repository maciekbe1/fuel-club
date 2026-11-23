"use client";

import { useEffect, useState } from "react";
import { useCurrentCase } from "../hooks";
import type { SupportHistoryItem } from "../services";
import { CustomerCase } from "./CustomerCase";
import { SupportStartForm } from "./SupportStartForm";

/** The customer asked for an agent: what may be attached and how to stop the bot's answer. */
export type AgentRequest = {
  /** Read at submit time: a turn that finishes while the form is open is included. */
  history: () => SupportHistoryItem[];
  stop: () => void;
  /** The chat conversation handed over: the API links it to the case once (ADR 0017). */
  conversationId: string | null;
};

/**
 * The customer's side of support on the chat screen: the consent form after a request, then the
 * conversation. An open case found on entry (another tab, a return after logging in) is shown at once.
 */
export function CustomerSupport({
  request,
  onCancelRequest,
  onActiveChange,
}: {
  request: AgentRequest | null;
  onCancelRequest: () => void;
  /** A case is on screen: the assistant is off until the customer returns to it. */
  onActiveChange: (active: boolean) => void;
}) {
  const current = useCurrentCase();
  const [shown, setShown] = useState<string | null>(null);
  const [dismissed, setDismissed] = useState<string[]>([]);
  const found = current.data?.id;
  const caseId = shown ?? (found && !dismissed.includes(found) ? found : null);

  // Keep a case found on entry on screen after it closes (current then answers 204).
  if (caseId && shown === null) setShown(caseId);

  useEffect(() => {
    onActiveChange(caseId !== null);
  }, [caseId, onActiveChange]);

  if (caseId)
    return (
      <CustomerCase
        caseId={caseId}
        onBack={() => {
          setDismissed((all) => [...all, caseId]);
          setShown(null);
          onCancelRequest();
        }}
      />
    );
  if (request)
    return (
      <SupportStartForm
        history={request.history}
        stopAssistant={request.stop}
        conversationId={request.conversationId}
        onCancel={onCancelRequest}
        onStarted={setShown}
      />
    );
  return null;
}
