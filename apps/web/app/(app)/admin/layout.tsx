import { redirect } from "next/navigation";
import { Suspense } from "react";
import type { Me } from "@/features/auth/services";
import { serverGet } from "@/lib/server/api";

/**
 * Admin screens only for the Admin role. A convenience: every admin endpoint is authorized by the API,
 * so a bypassed guard shows nothing but 403 errors.
 */
export default function AdminLayout({ children }: LayoutProps<"/admin">) {
  return (
    <Suspense fallback={<div className="flex-1" aria-busy="true" />}>
      <AdminGate>{children}</AdminGate>
    </Suspense>
  );
}

async function AdminGate({ children }: { children: React.ReactNode }) {
  const session = await serverGet<Me>("/api/auth/me").catch(() => null);
  if (session?.status === 401) redirect("/login");
  if (session?.data && !session.data.roles.includes("Admin")) redirect("/");
  return children;
}
