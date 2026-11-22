import { Fuel } from "lucide-react";
import { cn } from "@/lib/utils";

export function Wordmark({ className }: { className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-2 text-lg font-semibold tracking-tight",
        className,
      )}
    >
      <span className="grid size-9 place-items-center rounded-xl bg-brand-orange text-brand-navy">
        <Fuel className="size-5" aria-hidden />
      </span>
      Fuel Club
    </span>
  );
}
