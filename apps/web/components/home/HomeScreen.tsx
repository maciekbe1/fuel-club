"use client";

import {
  Headset,
  CreditCard,
  History,
  ScanLine,
  Ticket,
  Settings,
  TicketCheck,
  MessageCircle,
  Activity,
} from "lucide-react";
import { useMe } from "@/features/auth/hooks";
import { BalanceCard } from "@/features/wallet/components/BalanceCard";
import { Shortcut } from "./Shortcut";

/**
 * Composes features for the home page (features do not import each other). Staff-only accounts
 * (Cashier/Admin without User) have no wallet, so the balance is not requested for them.
 */
export function HomeScreen() {
  const { data: me } = useMe();
  const isCustomer = me?.roles.includes("User") ?? false;
  const isCashier = me?.roles.includes("Cashier") ?? false;
  const isAdmin = me?.roles.includes("Admin") ?? false;
  const isSupport = me?.roles.includes("Support") ?? false;
  const staffShortcuts = (isCashier || isAdmin || isSupport) && (
    <>
      {isCashier && (
        <Shortcut
          href="/kasa"
          icon={ScanLine}
          title="Kasa"
          description="Zarejestruj zakup paliwa"
        />
      )}
      {isSupport && (
        <Shortcut
          href="/konsultant"
          icon={Headset}
          title="Konsultant"
          description="Rozmowy z klientami z kolejki"
        />
      )}
      {isAdmin && (
        <Shortcut
          href="/admin/oferty"
          icon={Settings}
          title="Administracja ofert"
          description="Twórz, zmieniaj i wyłączaj oferty"
        />
      )}
      {isAdmin && (
        <Shortcut
          href="/admin/obserwowalnosc"
          icon={Activity}
          title="Obserwowalność"
          description="Metryki, anomalie, tury do przejrzenia"
        />
      )}
    </>
  );

  if (me && !isCustomer) {
    return (
      <div className="mx-auto flex w-full max-w-xl flex-col gap-3">
        <h1 className="text-2xl font-semibold">Cześć, {me.displayName}</h1>
        <p className="text-muted-foreground">
          To konto obsługi stacji. Saldo, karta i historia punktów są dostępne
          na kontach klientów.
        </p>
        {staffShortcuts && (
          <nav
            aria-label="Na skróty"
            className="mt-3 grid gap-3 sm:grid-cols-2"
          >
            {staffShortcuts}
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
          title="Moje kupony i nagrody"
          description="Aktywuj i pokaż przy kasie"
        />
        <Shortcut
          href="/czat"
          icon={MessageCircle}
          title="Asystent"
          description="Zapytaj o saldo, oferty i kupony"
        />
        {staffShortcuts}
      </nav>
    </div>
  );
}
