import { LoaderCircle } from "lucide-react";
import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";

export function SubmitButton({ pending, pendingLabel, children }: { pending: boolean; pendingLabel: string; children: ReactNode }) {
  return (
    <Button type="submit" disabled={pending} className="mt-2 h-12 rounded-full text-base font-semibold">
      {pending && <LoaderCircle className="animate-spin" aria-hidden />}
      {pending ? pendingLabel : children}
    </Button>
  );
}
