import { Inbox } from "lucide-react";
import type { ReactNode } from "react";
import { shortDateTimeFormat as time } from "@/lib/format";
import type { SupportQueueItem } from "../services";

export function AgentCaseList({
  title,
  empty,
  items,
  action,
}: {
  title: string;
  empty: string;
  items: SupportQueueItem[];
  action: (item: SupportQueueItem) => ReactNode;
}) {
  return (
    <section aria-label={title} className="flex flex-col gap-2">
      <h2 className="text-lg font-semibold">{title}</h2>
      {items.length === 0 ? (
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Inbox className="size-4" /> {empty}
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {items.map((item) => (
            <li
              key={item.id}
              data-testid="support-queue-item"
              className="flex items-start justify-between gap-3 rounded-2xl border p-3"
            >
              <div className="flex min-w-0 flex-col gap-0.5">
                <p className="font-medium">{item.customerName}</p>
                <p className="truncate text-sm">{item.firstMessage}</p>
                <p className="text-xs text-muted-foreground">
                  {time.format(new Date(item.createdAt))}
                </p>
              </div>
              {action(item)}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
