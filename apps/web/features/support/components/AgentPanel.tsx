"use client";

import { CircleAlert } from "lucide-react";
import { useCallback, useState } from "react";
import { Alert } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { useClaimCase, useSupportHub, useSupportQueue } from "../hooks";
import { agentActionError } from "../texts";
import { AgentCase } from "./AgentCase";
import { AgentCaseList } from "./AgentCaseList";

/**
 * The agent's queue and the case they work on. Who is the assignee is decided by the API only;
 * this screen reads it again after every change it hears of.
 */
export function AgentPanel() {
  const hub = useSupportHub();
  const queue = useSupportQueue(hub.connected);
  const claim = useClaimCase();
  const [openId, setOpenId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const leave = useCallback((message?: string) => {
    setOpenId(null);
    setNotice(message ?? null);
  }, []);

  if (queue.isError && !queue.data)
    return (
      <p role="alert" className="flex items-start gap-2">
        <CircleAlert className="mt-0.5 size-4 shrink-0 text-destructive" />
        {agentActionError(queue.error)}
      </p>
    );

  if (openId) return <AgentCase caseId={openId} hub={hub} onLeave={leave} />;

  return (
    <div className="flex flex-col gap-6">
      {notice && (
        <p role="status" className="rounded-2xl border px-4 py-2 text-sm">
          {notice}
        </p>
      )}
      {claim.isError && (
        <Alert className="rounded-2xl px-4 py-2 text-sm">
          {agentActionError(claim.error)}
        </Alert>
      )}
      <AgentCaseList
        title="Moje rozmowy"
        empty="Nie prowadzisz teraz żadnej rozmowy."
        items={queue.data?.mine ?? []}
        action={(item) => (
          <Button size="sm" onClick={() => setOpenId(item.id)}>
            Otwórz
          </Button>
        )}
      />
      <AgentCaseList
        title="Kolejka"
        empty="Nikt nie czeka na konsultanta."
        items={queue.data?.waiting ?? []}
        action={(item) => (
          <Button
            size="sm"
            disabled={claim.isPending}
            onClick={() => {
              setNotice(null);
              claim.mutate(item.id, { onSuccess: () => setOpenId(item.id) });
            }}
          >
            Przejmij
          </Button>
        )}
      />
      {!hub.connected && (
        <p className="text-xs text-muted-foreground">
          Brak połączenia na żywo - kolejka odświeża się co kilka sekund.
        </p>
      )}
    </div>
  );
}
