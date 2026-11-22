import Link from "next/link";
import { RewardsCatalog } from "@/features/rewards/components/RewardsCatalog";
import { BalanceCard } from "@/features/wallet/components/BalanceCard";

export default function RewardsPage() {
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Saldo
        </Link>
        <h1 className="text-2xl font-semibold">Oferty</h1>
        <p className="text-sm text-muted-foreground">
          Wymień punkty na kupon z rabatem na paliwo.
        </p>
      </div>
      <BalanceCard />
      <RewardsCatalog />
    </div>
  );
}
