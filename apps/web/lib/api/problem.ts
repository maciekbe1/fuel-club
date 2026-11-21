import type { FieldValues, Path, UseFormSetError } from "react-hook-form";

export type FieldError = { field: string; code: string };

export type ProblemDetails = {
  status?: number;
  title?: string;
  code?: string;
  errors?: Record<string, FieldError[]>;
};

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
  ) {
    super(problem.code ?? problem.title ?? `HTTP ${status}`);
    this.name = "ApiError";
  }

  get code() {
    return this.problem.code;
  }
}

export class NetworkError extends Error {
  constructor(cause: unknown) {
    super("Network error", { cause });
    this.name = "NetworkError";
  }
}

export function toApiError(status: number, body: unknown): ApiError {
  const problem = body && typeof body === "object" ? (body as ProblemDetails) : {};
  return new ApiError(status, problem);
}

export function applyFieldErrors<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  messages: Record<string, string>,
  fields?: readonly Path<T>[],
): boolean {
  if (!(error instanceof ApiError) || !error.problem.errors) return false;
  let applied = false;
  for (const [field, fieldErrors] of Object.entries(error.problem.errors)) {
    const first = fieldErrors[0];
    if (!first) continue;
    const target = !fields || fields.includes(field as Path<T>) ? (field as Path<T>) : ("root" as Path<T>);
    setError(target, { type: first.code, message: messages[first.code] ?? messages.default });
    applied = true;
  }
  return applied;
}
