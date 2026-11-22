import { cn } from "@/lib/utils";

export function LoyaltyCard({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn(
        "relative aspect-[1.586] w-full max-w-xs overflow-hidden rounded-2xl bg-linear-to-br from-orange-500 to-orange-700 p-5 text-white shadow-2xl shadow-black/40",
        className,
      )}
    >
      <div className="absolute -top-10 -right-10 size-40 rounded-full bg-white/10" />
      <div className="absolute -bottom-16 -left-8 size-44 rounded-full bg-white/10" />
      <div className="relative flex h-full flex-col justify-between">
        <div className="flex items-start justify-between">
          <span className="text-sm font-semibold">Fuel Club</span>
          <span className="h-7 w-10 rounded-md bg-linear-to-br from-amber-200 to-amber-400" />
        </div>
        <div>
          <p className="text-xs uppercase tracking-widest text-white/70">
            Saldo
          </p>
          <p className="text-3xl font-bold tabular-nums">1 250 pkt</p>
        </div>
        <p className="font-mono text-sm tracking-[0.2em] text-white/80">
          •••• •••• 2048
        </p>
      </div>
    </div>
  );
}
