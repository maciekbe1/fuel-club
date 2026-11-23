"use client";

import { useState } from "react";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { Queue } from "./Queue";
import { Summary } from "./Summary";

const WINDOWS = [
  { hours: 1, label: "1 h" },
  { hours: 24, label: "24 h" },
  { hours: 168, label: "7 dni" },
] as const;

/** Metadata only: counts, times, rules and pseudonymous turns - never conversation content. */
export function ObservabilityDashboard() {
  const [hours, setHours] = useState<number>(24);
  return (
    <div className="flex flex-col gap-8">
      <ToggleGroup
        aria-label="Okno czasu"
        variant="outline"
        size="sm"
        value={[String(hours)]}
        // A window is always chosen: clicking the active one keeps it.
        onValueChange={(value) => value[0] && setHours(Number(value[0]))}
      >
        {WINDOWS.map((window) => (
          <ToggleGroupItem key={window.hours} value={String(window.hours)}>
            {window.label}
          </ToggleGroupItem>
        ))}
      </ToggleGroup>
      <Summary hours={hours} />
      <Queue />
    </div>
  );
}
