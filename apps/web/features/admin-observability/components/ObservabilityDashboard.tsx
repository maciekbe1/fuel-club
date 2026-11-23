"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
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
      <div role="group" aria-label="Okno czasu" className="flex gap-2">
        {WINDOWS.map((window) => (
          <Button
            key={window.hours}
            type="button"
            size="sm"
            variant={hours === window.hours ? "default" : "outline"}
            aria-pressed={hours === window.hours}
            onClick={() => setHours(window.hours)}
          >
            {window.label}
          </Button>
        ))}
      </div>
      <Summary hours={hours} />
      <Queue />
    </div>
  );
}
