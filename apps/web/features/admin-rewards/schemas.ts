import { z } from "zod";
import type { AdminReward, CreateRewardBody } from "./services";

const decimal = (label: string, places: number, max: number) =>
  z
    .string()
    .trim()
    .min(1, `Podaj ${label}`)
    .regex(
      new RegExp(`^\\d{1,7}([.,]\\d{1,${places}})?$`),
      `Podaj ${label} jako liczbę (maks. ${places} miejsca po przecinku)`,
    )
    .refine((value) => {
      const number = Number(value.replace(",", "."));
      return number > 0 && number <= max;
    }, `Wartość musi być większa od 0 i nie większa niż ${max}`);

/** Same limits as RewardOfferRules on the server; the server stays the authority. */
export const rewardSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, "Podaj nazwę")
      .max(100, "Nazwa może mieć najwyżej 100 znaków"),
    description: z
      .string()
      .trim()
      .max(500, "Opis może mieć najwyżej 500 znaków"),
    pointsCost: z
      .string()
      .trim()
      .regex(/^\d{1,7}$/, "Podaj koszt jako liczbę całkowitą")
      .refine(
        (value) => Number(value) >= 1 && Number(value) <= 1_000_000,
        "Koszt musi być od 1 do 1 000 000 pkt",
      ),
    kind: z.enum(["FuelDiscount", "Product"]),
    // Checked below only for a fuel discount: a product has no per-liter terms.
    discountPerLiter: z.string(),
    maxLiters: z.string(),
    isActive: z.boolean(),
  })
  .superRefine((values, ctx) => {
    if (values.kind !== "FuelDiscount") return;
    for (const [field, schema] of [
      ["discountPerLiter", decimal("rabat na litr", 2, 10)],
      ["maxLiters", decimal("limit litrów", 3, 1000)],
    ] as const) {
      const result = schema.safeParse(values[field]);
      if (!result.success)
        ctx.addIssue({
          code: "custom",
          path: [field],
          message: result.error.issues[0]?.message ?? "Niepoprawna wartość",
        });
    }
  });

export type RewardValues = z.infer<typeof rewardSchema>;

const num = (value: string) => Number(value.trim().replace(",", "."));

export function toRewardBody(values: RewardValues): CreateRewardBody {
  return {
    name: values.name.trim(),
    description: values.description.trim(),
    pointsCost: Number(values.pointsCost),
    kind: values.kind,
    discountPerLiter:
      values.kind === "Product" ? null : num(values.discountPerLiter),
    maxLiters: values.kind === "Product" ? null : num(values.maxLiters),
    isActive: values.isActive,
  };
}

const text = new Intl.NumberFormat("pl-PL", {
  maximumFractionDigits: 3,
  useGrouping: false,
});

export function toRewardValues(offer: AdminReward): RewardValues {
  return {
    name: offer.name,
    description: offer.description,
    pointsCost: String(offer.pointsCost),
    kind: offer.kind === "Product" ? "Product" : "FuelDiscount",
    discountPerLiter:
      offer.discountPerLiter === null
        ? ""
        : text.format(offer.discountPerLiter),
    maxLiters: offer.maxLiters === null ? "" : text.format(offer.maxLiters),
    isActive: offer.isActive,
  };
}

export const emptyReward: RewardValues = {
  name: "",
  description: "",
  pointsCost: "",
  kind: "FuelDiscount",
  discountPerLiter: "",
  maxLiters: "",
  isActive: true,
};

/** True when the stored offer has exactly the values of the form (used after an unknown outcome). */
export const sameOffer = (offer: AdminReward, values: RewardValues) =>
  JSON.stringify(toRewardBody(toRewardValues(offer))) ===
  JSON.stringify(toRewardBody(values));

export const rewardMessages: Record<string, string> = {
  "rewards.required_field": "To pole jest wymagane.",
  "rewards.too_long": "Wartość jest za długa.",
  "rewards.invalid_format": "Niepoprawny format.",
  "rewards.points_cost_out_of_range": "Koszt musi być od 1 do 1 000 000 pkt.",
  "rewards.discount_per_liter_out_of_range":
    "Rabat musi być większy od 0 i nie większy niż 10 zł/l.",
  "rewards.discount_per_liter_precision":
    "Rabat może mieć najwyżej 2 miejsca po przecinku.",
  "rewards.max_liters_out_of_range":
    "Limit musi być większy od 0 i nie większy niż 1000 l.",
  "rewards.max_liters_precision":
    "Limit może mieć najwyżej 3 miejsca po przecinku.",
  "rewards.not_for_product": "Nagroda nie ma rabatu ani limitu litrów.",
  "rewards.kind_immutable": "Rodzaju oferty nie można zmienić.",
  default: "Niepoprawna wartość.",
};
