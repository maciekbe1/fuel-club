import { cn } from "@/lib/utils";
import type { SupportMessage, SupportSide } from "../services";
import { SYSTEM_TEXT } from "../texts";

const time = new Intl.DateTimeFormat("pl-PL", {
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});

export function SupportMessageBubble({
  side,
  message,
}: {
  side: SupportSide;
  message: SupportMessage;
}) {
  if (message.author === "System")
    return (
      <p
        data-testid="support-system"
        className="self-center text-center text-xs text-muted-foreground"
      >
        {SYSTEM_TEXT[side][message.text] ?? message.text}
      </p>
    );
  const mine =
    (side === "customer" && message.author === "Customer") ||
    (side === "agent" && message.author === "Support");
  return (
    <div
      data-testid="support-message"
      data-author={message.author}
      className={cn(
        "flex max-w-[85%] flex-col gap-0.5",
        mine ? "items-end self-end" : "self-start",
      )}
    >
      <p
        className={cn(
          "rounded-2xl px-4 py-2 whitespace-pre-wrap",
          mine
            ? "rounded-br-sm bg-primary text-primary-foreground"
            : "rounded-bl-sm bg-muted",
        )}
      >
        {message.text}
      </p>
      <span className="text-xs text-muted-foreground">
        {mine ? "Ty" : message.author === "Support" ? "Konsultant" : "Klient"} ·{" "}
        {time.format(new Date(message.createdAt))}
      </span>
    </div>
  );
}
