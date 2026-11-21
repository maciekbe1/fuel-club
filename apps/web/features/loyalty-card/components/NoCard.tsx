"use client";

import { Button } from "@/components/ui/button";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import type { useIssueCard } from "../hooks";
import { mutationError, orange } from "./messages";

export function NoCard({ issue }: { issue: ReturnType<typeof useIssueCard> }) {
  return (
    <section
      aria-labelledby="no-card-heading"
      className="flex max-w-md flex-col items-start gap-4 rounded-3xl border bg-card p-6"
    >
      <h2 id="no-card-heading" className="text-lg font-semibold">
        Nie masz jeszcze karty
      </h2>
      <p className="text-muted-foreground">
        Karta Fuel Club to kod, który pokazujesz przy kasie. Dzięki niemu punkty za tankowanie trafią na Twoje konto.
      </p>
      <Button size="lg" className={orange} onClick={() => issue.mutate()} disabled={issue.isPending}>
        {issue.isPending ? "Wydawanie…" : issue.isError && keepsIdempotencyKey(issue.error) ? "Ponów" : "Wydaj kartę"}
      </Button>
      {issue.isError && (
        <p role="alert" className="text-sm text-destructive">
          {mutationError(issue.error)}
        </p>
      )}
    </section>
  );
}
