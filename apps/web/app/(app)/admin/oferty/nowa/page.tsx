import Link from "next/link";
import { CreateRewardForm } from "@/features/admin-rewards/components/CreateRewardForm";

export default function NewOfferPage() {
  return (
    <div className="mx-auto flex w-full max-w-2xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/admin/oferty"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Oferty
        </Link>
        <h1 className="text-2xl font-semibold">Nowa oferta</h1>
      </div>
      <CreateRewardForm />
    </div>
  );
}
