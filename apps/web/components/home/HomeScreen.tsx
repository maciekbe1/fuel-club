"use client";

import {
  ChevronRight,
  CreditCard,
  History,
  ScanLine,
  Ticket,
  TicketCheck,
} from "lucide-react";
import Link from "next/link";
import type { ComponentType } from "react";
import { useMe } from "@/features/auth/hooks";
import { BalanceCard } from "@/features/wallet/components/BalanceCard";

/**
 * Composes features for the home page (features do not import each other). Staff-only accounts
 * (Cashier/Admin without User) have no wallet, so the balance is not requested for them.
 */
export function HomeScreen() {
  const { data: me } = useMe();
  const isCustomer = me?.roles.includes("User") ?? false;
  const isCashier = me?.roles.includes("Cashier") ?? false;
  const cashierShortcut = isCashier && (
    <Shortcut
      href="/kasa"
      icon={ScanLine}
      title="Kasa"
      description="Zarejestruj zakup paliwa"
    />
  );

  if (me && !isCustomer) {
    return (
      <div className="mx-auto flex w-full max-w-xl flex-col gap-3">
        <h1 className="text-2xl font-semibold">Cześć, {me.displayName}</h1>
        <p className="text-muted-foreground">
          To konto obsługi stacji. Saldo, karta i historia punktów są dostępne
          na kontach klientów.
        </p>
        {cashierShortcut && (
          <nav
            aria-label="Na skróty"
            className="mt-3 grid gap-3 sm:grid-cols-2"
          >
            {cashierShortcut}
          </nav>
        )}
      </div>
    );
  }

  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
      <h1 className="text-2xl font-semibold">
        {me ? `Cześć, ${me.displayName}` : "Twoje konto"}
      </h1>
      {isCustomer && <BalanceCard />}
      <nav aria-label="Na skróty" className="grid gap-3 sm:grid-cols-2">
        <Shortcut
          href="/karta"
          icon={CreditCard}
          title="Karta"
          description="Pokaż kod przy kasie"
        />
        <Shortcut
          href="/historia"
          icon={History}
          title="Historia punktów"
          description="Ostatnie tankowania"
        />
        <Shortcut
          href="/oferty"
          icon={Ticket}
          title="Oferty"
          description="Wymień punkty na kupon"
        />
        <Shortcut
          href="/kupony"
          icon={TicketCheck}
          title="Moje kupony"
          description="Aktywuj i pokaż przy kasie"
        />
        {cashierShortcut}
      </nav>
    </div>
  );
}

function Shortcut({
  href,
  icon: Icon,
  title,
  description,
}: {
  href: "/karta" | "/historia" | "/oferty" | "/kupony" | "/kasa";
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
