"use client";

import { RotateCw } from "lucide-react";
import type { ComponentProps } from "react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/** Refetch button with a spinning icon while the request runs; disabled meanwhile. */
export function RetryButton({
  busy,
  children = "Spróbuj ponownie",
  ...props
}: Omit<ComponentProps<typeof Button>, "disabled"> & { busy: boolean }) {
  return (
    <Button size="sm" disabled={busy} {...props}>
      <RotateCw className={cn("size-4", busy && "animate-spin")} aria-hidden />
      {children}
    </Button>
  );
}
