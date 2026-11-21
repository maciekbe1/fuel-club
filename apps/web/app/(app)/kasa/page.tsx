import Link from "next/link";
import { CashierPanel } from "@/features/cashier/components/CashierPanel";

export default function CashierPage() {
  return (
    <div className="mx-auto flex w-full max-w-xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link href="/" className="text-sm text-muted-foreground hover:text-foreground">
          ← Start
        </Link>
        <h1 className="text-2xl font-semibold">Kasa</h1>
        <p className="text-sm text-muted-foreground">Symulator kasy: zarejestruj zakup paliwa na karcie klienta.</p>
      </div>
      <CashierPanel />
    </div>
  );
}
