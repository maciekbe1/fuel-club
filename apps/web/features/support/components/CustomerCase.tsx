"use client";

import { Button } from "@/components/ui/button";
import { useCloseCase, useSupportHub } from "../hooks";
import { OPEN_STATUSES } from "../services";
import { SupportConversation } from "./SupportConversation";

/** The customer's open (or just finished) case, with its own hub connection. */
export function CustomerCase({
  caseId,
  onBack,
}: {
  caseId: string;
  onBack: () => void;
}) {
  const hub = useSupportHub();
  const close = useCloseCase();

  return (
    <div className="flex flex-col gap-2">
      <SupportConversation
        side="customer"
        caseId={caseId}
        hub={hub}
        onUnavailable={onBack}
        actions={(status) =>
          OPEN_STATUSES.has(status) ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={close.isPending}
              onClick={() => close.mutate(caseId)}
            >
              Zakończ rozmowę
            </Button>
          ) : (
            <Button type="button" size="sm" onClick={onBack}>
              Wróć do asystenta
            </Button>
          )
        }
      />
      {close.isError && (
        <p role="alert" className="text-sm">
          Nie udało się zakończyć rozmowy. Spróbuj ponownie.
        </p>
      )}
    </div>
  );
}
