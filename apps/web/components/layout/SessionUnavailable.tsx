"use client";

import { RotateCw } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTransition } from "react";
import { Wordmark } from "@/components/brand/Wordmark";
import { Button } from "@/components/ui/button";

/** Shown when the session could not be checked (API down, 5xx). Not a logout: the cookie is kept. */
export function SessionUnavailable() {
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  return (
    <main className="flex flex-1 flex-col items-center justify-center gap-6 px-6 py-10 text-center">
      <Wordmark />
      <div role="alert" className="flex max-w-sm flex-col gap-2">
        <h1 className="text-xl font-semibold">Serwis chwilowo niedostępny</h1>
        <p className="text-muted-foreground">
          Nie udało się połączyć z Fuel Club. Nie wylogowaliśmy Cię - spróbuj
          ponownie za chwilę.
        </p>
      </div>
      <Button
        onClick={() => startTransition(() => router.refresh())}
        disabled={pending}
      >
        <RotateCw
          className={pending ? "size-4 animate-spin" : "size-4"}
          aria-hidden
        />
        Spróbuj ponownie
      </Button>
    </main>
  );
}
