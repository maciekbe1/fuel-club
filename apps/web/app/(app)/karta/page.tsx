import Link from "next/link";
import { LoyaltyCardPanel } from "@/features/loyalty-card/components/LoyaltyCardPanel";

export default function CardPage() {
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href="/" className="text-sm text-muted-foreground hover:text-foreground">
          ← Saldo
        </Link>
        <h1 className="text-2xl font-semibold">Twoja karta</h1>
      </div>
      <LoyaltyCardPanel />
    </div>
  );
}
