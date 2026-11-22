"use client";

import type { UseFormReturn } from "react-hook-form";
import { fieldA11y, fieldClass, FormField } from "@/components/form/FormField";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import type { RewardValues } from "../schemas";

/**
 * The offer fields shared by the create and edit forms. The kind is chosen at creation only
 * (`kindLocked` on edit); a product has no per-liter fields.
 */
export function RewardFields({
  form,
  kindLocked = false,
}: {
  form: UseFormReturn<RewardValues>;
  kindLocked?: boolean;
}) {
  const { errors } = form.formState;
  const product = form.watch("kind") === "Product";
  return (
    <>
      {kindLocked ? (
        // Not a disabled input: react-hook-form drops disabled values, the kind stays in the form.
        <p className="text-sm">
          <span className="font-medium">Rodzaj:</span>{" "}
          {product ? "Nagroda (produkt)" : "Rabat na paliwo"}. Nie można go
          zmienić po utworzeniu.
          {errors.kind && (
            <span role="alert" className="block text-destructive">
              {errors.kind.message}
            </span>
          )}
        </p>
      ) : (
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">Rodzaj</legend>
          {(
            [
              ["FuelDiscount", "Rabat na paliwo"],
              ["Product", "Nagroda (produkt)"],
            ] as const
          ).map(([value, label]) => (
            <label key={value} className="flex items-center gap-3 text-sm">
              <input
                type="radio"
                value={value}
                className="size-5 accent-[var(--color-brand-orange)]"
                {...form.register("kind")}
              />
              {label}
            </label>
          ))}
          {errors.kind && (
            <p role="alert" className="text-sm text-destructive">
              {errors.kind.message}
            </p>
          )}
        </fieldset>
      )}
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
        {!product && (
          <>
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
            <FormField
              id="maxLiters"
              label="Limit litrów"
              error={errors.maxLiters}
            >
              <Input
                inputMode="decimal"
                autoComplete="off"
                placeholder="50"
                className={fieldClass}
                {...fieldA11y("maxLiters", errors.maxLiters)}
                {...form.register("maxLiters")}
              />
            </FormField>
          </>
        )}
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
