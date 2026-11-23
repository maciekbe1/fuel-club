"use client";

import { useCallback } from "react";
import { Button } from "@/components/ui/button";
import type { ApiError } from "@/lib/api/problem";
import { useAgentCloseCase, useReleaseCase, type SupportHub } from "../hooks";
import { OPEN_STATUSES } from "../services";
import { agentActionError } from "../texts";
import { SupportConversation } from "./SupportConversation";

export function AgentCase({
  caseId,
  hub,
  onLeave,
}: {
  caseId: string;
  hub: SupportHub;
  onLeave: (message?: string) => void;
}) {
  const release = useReleaseCase();
  const close = useAgentCloseCase();
  const failed = release.error ?? close.error;
  const busy = release.isPending || close.isPending;
  const onUnavailable = useCallback(
    (error: ApiError) => onLeave(agentActionError(error)),
    [onLeave],
  );

  return (
    <div className="flex flex-col gap-3">
      <Button
        variant="ghost"
        size="sm"
        className="self-start"
        onClick={() => onLeave()}
      >
        ← Kolejka
      </Button>
      <SupportConversation
        side="agent"
        caseId={caseId}
        hub={hub}
        onUnavailable={onUnavailable}
        actions={(status) =>
          OPEN_STATUSES.has(status) && (
            <div className="flex flex-wrap gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={busy}
                onClick={() =>
                  release.mutate(caseId, {
                    onSuccess: () => onLeave("Zgłoszenie wróciło do kolejki."),
                  })
                }
              >
                Oddaj do kolejki
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={busy}
                onClick={() => close.mutate(caseId)}
              >
                Zamknij zgłoszenie
              </Button>
            </div>
          )
        }
      />
      {failed && (
        <p role="alert" className="text-sm">
          {agentActionError(failed)}
        </p>
      )}
    </div>
  );
}
