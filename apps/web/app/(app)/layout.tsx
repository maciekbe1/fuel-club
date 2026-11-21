import { redirect } from "next/navigation";
import { Suspense } from "react";
import { AppShell } from "@/components/layout/AppShell";
import { SessionUnavailable } from "@/components/layout/SessionUnavailable";
import type { Me } from "@/features/auth/services";
import { serverGet } from "@/lib/server/api";

export default function AppLayout({ children }: LayoutProps<"/">) {
  return (
    <Suspense fallback={<div className="flex-1" aria-busy="true" />}>
      <SessionGate>{children}</SessionGate>
    </Suspense>
  );
}

/** Only 401 means "not logged in". API failures (network, 5xx) show a retryable error instead. */
async function SessionGate({ children }: { children: React.ReactNode }) {
  const session = await serverGet<Me>("/api/auth/me").catch(() => null);
  if (session?.status === 401) redirect("/login");
  if (!session?.data) return <SessionUnavailable />;
  return <AppShell me={session.data}>{children}</AppShell>;
}
