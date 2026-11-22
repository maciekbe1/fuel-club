import Link from "next/link";
import { CouponsList } from "@/features/coupons/components/CouponsList";

export default function CouponsPage() {
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Saldo
        </Link>
        <h1 className="text-2xl font-semibold">Moje kupony i nagrody</h1>
        <p className="text-sm text-muted-foreground">
          Aktywuj kupon lub odbiór nagrody i pokaż kod przy kasie razem z kartą.
        </p>
      </div>
      <CouponsList />
    </div>
  );
}
