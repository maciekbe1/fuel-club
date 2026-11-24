import Link from "next/link";
import { ConversationList } from "@/features/admin-observability/components/ConversationList";

export default function ConversationsPage() {
  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/admin/obserwowalnosc"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Obserwowalność
        </Link>
        <h1 className="text-2xl font-semibold">Rozmowy</h1>
        <p className="text-sm text-muted-foreground">
          Rozmowy z asystentem jako metadane: czasy, zakończenie, tury i flagi.
          Treść rozmów nie jest zapisywana.
        </p>
      </div>
      <ConversationList />
    </div>
  );
}
