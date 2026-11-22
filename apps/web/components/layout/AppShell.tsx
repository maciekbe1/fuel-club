"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useLogout, useMe } from "@/features/auth/hooks";
import type { Me } from "@/features/auth/services";

export function AppShell({
  me: initialMe,
  children,
}: {
  me: Me;
  children: ReactNode;
}) {
  const { data: me } = useMe(initialMe);
  const logout = useLogout();

  return (
    <div className="flex flex-1 flex-col">
      <header className="flex items-center justify-between border-b px-4 py-3">
        <span className="font-semibold">Fuel Club</span>
        <div className="flex items-center gap-3">
          <span className="text-sm text-muted-foreground">
            {me?.displayName}
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => logout.mutate()}
            disabled={logout.isPending}
          >
            Wyloguj
          </Button>
        </div>
      </header>
      <main className="flex flex-1 flex-col px-4 py-6">{children}</main>
    </div>
  );
}
