import { buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/**
 * "Zaloguj się" after an expired session. A plain link on purpose: a full page load drops the screens
 * Next keeps hidden in the DOM (with codes and balances), so nothing is left for the next person.
 */
export function LoginAgainLink({ className }: { className?: string }) {
  return (
    <a href="/login" className={cn(buttonVariants({ size: "sm" }), className)}>
      Zaloguj się
    </a>
  );
}
