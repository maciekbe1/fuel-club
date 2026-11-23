import Link from "next/link";
import { AgentPanel } from "@/features/support/components/AgentPanel";

export default function AgentPage() {
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Start
        </Link>
        <h1 className="text-2xl font-semibold">Konsultant</h1>
        <p className="text-sm text-muted-foreground">
          Rozmowy z klientami przekazane z asystenta.
        </p>
      </div>
      <AgentPanel />
    </div>
  );
}
