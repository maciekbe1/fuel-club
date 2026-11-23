import type { SupportMessage, SupportSide } from "../services";

/** Text the customer attached from the AI chat: their claim only, not a record of what the bot said. */
export function AttachedHistory({
  side,
  items,
}: {
  side: SupportSide;
  items: SupportMessage[];
}) {
  if (items.length === 0) return null;
  return (
    <details
      data-testid="support-history"
      className="rounded-2xl border border-dashed px-4 py-2 text-sm"
      open={side === "agent"}
    >
      <summary className="cursor-pointer text-muted-foreground">
        {side === "agent"
          ? "Tekst przekazany przez klienta - niepotwierdzony"
          : "Dołączona rozmowa z asystentem"}
      </summary>
      <ul className="mt-2 flex flex-col gap-1">
        {items.map((m) => (
          <li key={m.seq} className="whitespace-pre-wrap">
            <span className="font-medium">
              {m.historyRole === "assistant"
                ? "Asystent (wg klienta)"
                : "Klient"}
              :
            </span>{" "}
            {m.text}
          </li>
        ))}
      </ul>
    </details>
  );
}
