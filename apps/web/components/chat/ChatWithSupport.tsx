"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useState } from "react";
import { ChatScreen } from "@/features/assistant/components/ChatScreen";
import {
  CustomerSupport,
  type AgentRequest,
} from "@/features/support/components/CustomerSupport";
import { queryKeys } from "@/lib/query-keys";

/**
 * Composes the assistant and support on the chat screen (features do not import each other). While
 * a support case is shown, the assistant keeps its turns on screen but takes no questions and its
 * activation proposals are off - the API refuses both anyway (ADR 0012).
 */
export function ChatWithSupport() {
  const queryClient = useQueryClient();
  const [request, setRequest] = useState<AgentRequest | null>(null);
  const [active, setActive] = useState(false);

  const onTalkToAgent = useCallback(
    (history: AgentRequest["history"], stop: () => void) =>
      setRequest({ history, stop }),
    [],
  );
  const onHandoffActive = useCallback(
    () =>
      void queryClient.invalidateQueries({
        queryKey: queryKeys.support.current,
      }),
    [queryClient],
  );

  return (
    <div className="flex flex-col gap-4">
      <ChatScreen
        locked={active || request !== null}
        onTalkToAgent={onTalkToAgent}
        onHandoffActive={onHandoffActive}
      />
      <CustomerSupport
        request={request}
        onCancelRequest={() => setRequest(null)}
        onActiveChange={setActive}
      />
    </div>
  );
}
