"use client";

import type { UseFormReturn } from "react-hook-form";
import { fieldA11y, fieldClass, FormField } from "@/components/form/FormField";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import type { RewardValues } from "../schemas";

/** The offer fields shared by the create and edit forms. */
export function RewardFields({ form }: { form: UseFormReturn<RewardValues> }) {
  const { errors } = form.formState;
  return (
    <>
      <FormField id="name" label="Nazwa" error={errors.name}>
        <Input
          autoComplete="off"
          className={fieldClass}
          {...fieldA11y("name", errors.name)}
          {...form.register("name")}
        />
      </FormField>
      <FormField
        id="description"
        label="Opis (opcjonalnie)"
        error={errors.description}
      >
        <textarea
          rows={3}
          className={cn(
            "rounded-xl border bg-transparent px-4 py-3 text-base shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 aria-invalid:border-destructive",
          )}
          {...fieldA11y("description", errors.description)}
          {...form.register("description")}
        />
      </FormField>
      <div className="grid gap-5 sm:grid-cols-3">
        <FormField
          id="pointsCost"
          label="Koszt (pkt)"
          error={errors.pointsCost}
        >
          <Input
            inputMode="numeric"
            autoComplete="off"
            className={fieldClass}
            {...fieldA11y("pointsCost", errors.pointsCost)}
            {...form.register("pointsCost")}
          />
        </FormField>
        <FormField
          id="discountPerLiter"
          label="Rabat (zł/l)"
          error={errors.discountPerLiter}
        >
          <Input
            inputMode="decimal"
            autoComplete="off"
            placeholder="0,20"
            className={fieldClass}
            {...fieldA11y("discountPerLiter", errors.discountPerLiter)}
            {...form.register("discountPerLiter")}
          />
        </FormField>
        <FormField id="maxLiters" label="Limit litrów" error={errors.maxLiters}>
          <Input
            inputMode="decimal"
            autoComplete="off"
            placeholder="50"
            className={fieldClass}
            {...fieldA11y("maxLiters", errors.maxLiters)}
            {...form.register("maxLiters")}
          />
        </FormField>
      </div>
      <label className="flex items-center gap-3 text-sm">
        <input
          type="checkbox"
          className="size-5 accent-[var(--color-brand-orange)]"
          {...form.register("isActive")}
        />
        Aktywna (widoczna w katalogu klientów)
      </label>
    </>
  );
}
