import Link from "next/link";
import type { ConversationListItem } from "../services";
import { END_REASONS, dateTime } from "./labels";
import { ReasonBadges } from "./ReasonBadges";

export function ConversationRow({ item }: { item: ConversationListItem }) {
  return (
    <li data-testid="conversation-row" data-id={item.id}>
      <Link
        href={`/admin/obserwowalnosc/rozmowy/${item.id}`}
        className="flex flex-col gap-2 rounded-2xl border p-4 text-sm hover:bg-muted/50"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="font-medium">
            {dateTime.format(new Date(item.startedAt))} · tury: {item.turns}
            {" · "}
            {item.endReason
              ? (END_REASONS[item.endReason] ?? item.endReason)
              : "otwarta"}
          </span>
          <ReasonBadges reasons={item.reasons} />
        </div>
        <p className="text-muted-foreground">
          <span className="font-mono">{item.userPseudonym.slice(0, 12)}…</span>
          {item.models.length > 0 && ` · ${item.models.join(", ")}`}
          {item.promptVersions.length > 0 &&
            ` (${item.promptVersions.join(", ")})`}
          {item.handoffProposed && " · propozycja konsultanta"}
          {item.hasCase && " · zgłoszenie"}
          {item.runId && ` · przebieg ${item.runId}`}
        </p>
      </Link>
    </li>
  );
}
