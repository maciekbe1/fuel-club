import type { ReactNode } from "react";
import { Label } from "@/components/ui/label";

export const fieldClass = "h-12 rounded-xl px-4 text-base md:text-base";

export const errorId = (id: string) => `${id}-error`;

export function fieldA11y(id: string, error?: { message?: string }) {
  return {
    id,
    "aria-invalid": !!error,
    "aria-describedby": error ? errorId(id) : undefined,
  };
}

export function FormField({
  id,
  label,
  error,
  children,
}: {
  id: string;
  label: string;
  error?: { message?: string };
  children: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {error?.message && (
        <p id={errorId(id)} className="text-sm text-destructive">
          {error.message}
        </p>
      )}
    </div>
  );
}
