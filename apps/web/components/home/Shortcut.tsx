import { ChevronRight } from "lucide-react";
import Link from "next/link";
import type { ComponentType } from "react";

export function Shortcut({
  href,
  icon: Icon,
  title,
  description,
}: {
  href:
    | "/karta"
    | "/historia"
    | "/oferty"
    | "/kupony"
    | "/czat"
    | "/kasa"
    | "/konsultant"
    | "/admin/oferty"
    | "/admin/obserwowalnosc";
  icon: ComponentType<{ className?: string }>;
  title: string;
  description: string;
}) {
  const content = (
    <>
      <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-brand-orange/15 text-primary">
        <Icon className="size-5" />
      </span>
      <span className="flex flex-1 flex-col">
        <span className="font-medium">{title}</span>
        <span className="text-sm text-muted-foreground">{description}</span>
      </span>
      <ChevronRight
        className="size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5"
        aria-hidden
      />
    </>
  );

  return (
    <Link
      href={href}
      className="group flex items-center gap-4 rounded-2xl border bg-card p-4 transition-colors hover:border-primary/40 hover:bg-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
    >
      {content}
    </Link>
  );
}
