import Link from "next/link";
import { ObservabilityDashboard } from "@/features/admin-observability/components/ObservabilityDashboard";

export default function ObservabilityPage() {
  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          href="/"
          className="text-sm text-muted-foreground hover:text-foreground"
        >
          ← Start
        </Link>
        <h1 className="text-2xl font-semibold">Obserwowalność</h1>
        <p className="text-sm text-muted-foreground">
          Metryki czatu i zdarzeń bezpieczeństwa, reguły anomalii i tury do
          przejrzenia.
        </p>
      </div>
      <ObservabilityDashboard />
    </div>
  );
}
