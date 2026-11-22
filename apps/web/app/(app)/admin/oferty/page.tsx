import Link from "next/link";
import { AdminRewardsList } from "@/features/admin-rewards/components/AdminRewardsList";

export default function AdminOffersPage() {
  return (
    <div className="mx-auto flex w-full max-w-2xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Start
        </Link>
        <h1 className="text-2xl font-semibold">Oferty - administracja</h1>
        <p className="text-sm text-muted-foreground">
          Wyłączona oferta znika z katalogu klientów. Ofert się nie usuwa.
        </p>
      </div>
      <AdminRewardsList />
    </div>
  );
}
