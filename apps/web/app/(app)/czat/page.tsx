import Link from "next/link";
import { ChatScreen } from "@/features/assistant/components/ChatScreen";

export default function ChatPage() {
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Saldo
        </Link>
        <h1 className="text-2xl font-semibold">Asystent</h1>
      </div>
      <ChatScreen />
    </div>
  );
}
