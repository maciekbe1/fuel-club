import { Message, MessageContent } from "@/components/ai-elements/message";
import type { SupportMessage, SupportSide } from "../services";
import { SYSTEM_TEXT } from "../texts";

const time = new Intl.DateTimeFormat("pl-PL", {
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});

/** People's messages stay plain text (no markdown); system events in this side's words. */
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
    <Message
      from={mine ? "user" : "assistant"}
      data-testid="support-message"
      data-author={message.author}
      className="gap-0.5"
    >
      <MessageContent className="whitespace-pre-wrap group-[.is-assistant]:rounded-lg group-[.is-assistant]:bg-muted group-[.is-assistant]:px-4 group-[.is-assistant]:py-3">
        {message.text}
      </MessageContent>
      <span className="text-xs text-muted-foreground group-[.is-user]:text-right">
        {mine ? "Ty" : message.author === "Support" ? "Konsultant" : "Klient"} ·{" "}
        {time.format(new Date(message.createdAt))}
      </span>
    </Message>
  );
}
