import { LoyaltyCard } from "@/components/brand/LoyaltyCard";
import { Wordmark } from "@/components/brand/Wordmark";

export default function PublicLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex flex-1 flex-col bg-brand-navy lg:flex-row">
      <section className="relative flex flex-col gap-8 overflow-hidden px-6 pt-8 pb-14 text-brand-navy-foreground lg:w-1/2 lg:justify-between lg:p-12">
        <div className="pointer-events-none absolute -top-24 -right-24 size-72 rounded-full bg-brand-orange/20 blur-3xl" />
        <Wordmark />
        <div className="relative flex flex-col gap-3">
          <h1 className="text-3xl font-bold tracking-tight text-balance lg:text-5xl">
            Tankuj. Zbieraj punkty. <span className="text-brand-orange">Odbieraj nagrody.</span>
          </h1>
          <p className="max-w-md text-white/70 max-sm:hidden">
            Za każdy pełny 1 zł wydany na paliwo dostajesz 1 punkt. Punkty wymienisz na kupony do wykorzystania na stacji.
          </p>
        </div>
        <LoyaltyCard className="relative mx-auto -rotate-6 max-sm:max-w-64 lg:mx-0 lg:max-w-sm" />
      </section>

      <main className="-mt-6 flex flex-1 flex-col rounded-t-3xl bg-background px-6 pt-8 pb-10 lg:mt-0 lg:justify-center lg:rounded-none lg:px-12">
        <div className="mx-auto w-full max-w-sm">{children}</div>
      </main>
    </div>
  );
}
