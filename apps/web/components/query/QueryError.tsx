import { CircleAlert } from "lucide-react";
import { LoginAgainLink } from "@/components/auth/LoginAgainLink";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { RetryButton } from "@/components/ui/retry-button";
import { ApiError, readErrorMessage } from "@/lib/api/problem";

/**
 * A read that failed with nothing to show: the reason, then "log in again" for an expired session
 * or a retry for anything else.
 */
export function QueryError({
  error,
  fallback,
  message,
  retrying,
  onRetry,
}: {
  error: unknown;
  /** Shown when the error carries no message of its own. */
  fallback: string;
  /** Overrides the message (e.g. for 404). */
  message?: string;
  retrying: boolean;
  onRetry: () => void;
}) {
  return (
    <Alert className="gap-3 rounded-2xl p-5">
      <CircleAlert />
      <AlertDescription className="flex flex-col items-start gap-3 text-base text-foreground">
        <p>{message ?? readErrorMessage(error, fallback)}</p>
        {error instanceof ApiError && error.status === 401 ? (
          <LoginAgainLink />
        ) : (
          <RetryButton busy={retrying} onClick={onRetry} />
        )}
      </AlertDescription>
    </Alert>
  );
}
