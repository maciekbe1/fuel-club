"use client";

import { Controller, type UseFormReturn } from "react-hook-form";
import { fieldA11y, fieldClass, FormField } from "@/components/form/FormField";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Textarea } from "@/components/ui/textarea";
import type { RewardValues } from "../schemas";

/**
 * The offer fields shared by the create and edit forms. The kind is chosen at creation only
 * (`kindLocked` on edit); a product has no per-liter fields.
 */
export function RewardFields({
  form,
  kindLocked = false,
  disabled = false,
}: {
  form: UseFormReturn<RewardValues>;
  kindLocked?: boolean;
  /** Base UI radios and checkboxes ignore a disabled <fieldset>: they need it themselves. */
  disabled?: boolean;
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
          <Controller
            control={form.control}
            name="kind"
            render={({ field }) => (
              <RadioGroup
                value={field.value}
                onValueChange={field.onChange}
                disabled={disabled}
                className="gap-3"
              >
                {(
                  [
                    ["FuelDiscount", "Rabat na paliwo"],
                    ["Product", "Nagroda (produkt)"],
                  ] as const
                ).map(([value, label]) => (
                  <Label key={value} className="gap-3 font-normal">
                    <RadioGroupItem value={value} />
                    {label}
                  </Label>
                ))}
              </RadioGroup>
            )}
          />
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
        <Textarea
          rows={3}
          className="rounded-xl px-4 py-3"
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
            <FormField
              id="minLiters"
              label="Od ilu litrów (opcjonalnie)"
              error={errors.minLiters}
            >
              <Input
                inputMode="decimal"
                autoComplete="off"
                placeholder="np. 30 - puste: od pierwszego litra"
                className={fieldClass}
                {...fieldA11y("minLiters", errors.minLiters)}
                {...form.register("minLiters")}
              />
            </FormField>
          </>
        )}
      </div>
      <Controller
        control={form.control}
        name="isActive"
        render={({ field }) => (
          <Label className="gap-3 font-normal">
            <Checkbox
              checked={field.value}
              onCheckedChange={field.onChange}
              disabled={disabled}
            />
            Aktywna (widoczna w katalogu klientów)
          </Label>
        )}
      />
    </>
  );
}
