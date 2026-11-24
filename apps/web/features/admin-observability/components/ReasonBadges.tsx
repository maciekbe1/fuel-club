import { Badge } from "@/components/ui/badge";
import { REASONS } from "./labels";

/** Why a turn or a conversation is suspicious; nothing when it is not. */
export function ReasonBadges({ reasons }: { reasons: string[] }) {
  if (reasons.length === 0) return null;
  return (
    <span className="flex flex-wrap gap-1">
      {reasons.map((reason) => (
        <Badge
          key={reason}
          className="h-auto bg-amber-100 font-normal whitespace-normal text-amber-950 dark:bg-amber-900 dark:text-amber-50"
        >
          {REASONS[reason] ?? reason}
        </Badge>
      ))}
    </span>
  );
}
