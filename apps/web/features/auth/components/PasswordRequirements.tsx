import { Check, Circle } from "lucide-react";
import { cn } from "@/lib/utils";
import { passwordRules } from "../schemas";

export function PasswordRequirements({ id, value }: { id: string; value: string }) {
  return (
    <ul id={id} className="grid grid-cols-2 gap-x-4 gap-y-1.5 text-sm" aria-label="Wymagania hasła">
      {passwordRules.map((rule) => {
        const met = rule.test(value);
        const Icon = met ? Check : Circle;
        return (
          <li key={rule.id} className={cn("flex items-center gap-2", met ? "text-foreground" : "text-muted-foreground")}>
            <Icon className={cn("size-4 shrink-0", met && "text-primary")} aria-hidden />
            <span>
              {rule.label}
              <span className="sr-only">{met ? " - spełnione" : " - niespełnione"}</span>
            </span>
          </li>
        );
      })}
    </ul>
  );
}
