import { ApiError } from "@/lib/api/problem";

export const FIELDS = [
  "name",
  "description",
  "pointsCost",
  "kind",
  "discountPerLiter",
  "maxLiters",
  "isActive",
] as const;

export const isConflict = (error: unknown) =>
  error instanceof ApiError && error.code === "rewards.offer_version_conflict";

/** Validation (400), conflict (409), missing offer (404) and 401 are final; anything else is unknown. */
export const isFinal = (error: unknown) =>
  error instanceof ApiError && [400, 401, 403, 404, 409].includes(error.status);
