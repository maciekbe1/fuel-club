import { LoginAgainLink } from "@/components/auth/LoginAgainLink";
import { RetryButton } from "@/components/ui/retry-button";
import { ApiError, readErrorMessage } from "@/lib/api/problem";

export function LoadError({
  error,
  retry,
  busy,
}: {
  error: unknown;
  retry: () => void;
  busy: boolean;
}) {
  return (
    <div
      role="alert"
      className="flex flex-col items-start gap-3 rounded-2xl border p-5"
    >
      <p>
        {readErrorMessage(
          error,
          "Nie udało się pobrać danych. Spróbuj ponownie.",
        )}
      </p>
      {error instanceof ApiError && error.status === 401 ? (
        <LoginAgainLink />
      ) : (
        <RetryButton busy={busy} onClick={retry} />
      )}
    </div>
  );
}
