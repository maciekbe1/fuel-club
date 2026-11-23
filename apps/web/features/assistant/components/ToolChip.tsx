import { Check, Wrench } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { ToolCall } from "../hooks";

const TOOL_LABELS: Record<string, string> = {
  get_my_balance: "Saldo punktów",
  list_available_rewards: "Dostępne oferty",
  list_my_coupons: "Twoje kupony",
  propose_coupon_activation: "Propozycja aktywacji",
  propose_handoff: "Propozycja konsultanta",
  compare_coupons: "Porównanie kuponów",
  explain_purchase_points: "Rachunek punktów",
};

const TOOL_STATUS: Record<string, string> = {
  ok: "sprawdzono",
  rejected: "odrzucono",
  error: "błąd",
};

export function ToolChip({ tool }: { tool: ToolCall }) {
  const label = TOOL_LABELS[tool.name] ?? "Nieznane narzędzie";
  const ok = tool.status === "ok";
  return (
    <li>
      <Badge
        variant="outline"
        className={cn(
          "h-auto font-normal whitespace-normal",
          !ok && "border-amber-500/50 text-amber-900 dark:text-amber-100",
        )}
      >
        {ok ? <Check /> : <Wrench />}
        {label}: {TOOL_STATUS[tool.status] ?? tool.status}
      </Badge>
    </li>
  );
}
