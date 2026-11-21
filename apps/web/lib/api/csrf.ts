const MUTATING = new Set(["POST", "PUT", "PATCH", "DELETE"]);
export const CSRF_HEADER = "X-CSRF-TOKEN";
export const CSRF_INVALID_CODE = "csrf.invalid";

let tokenPromise: Promise<string> | null = null;

async function fetchToken(): Promise<string> {
  const response = await fetch("/api/auth/csrf", { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) throw new Error(`CSRF token request failed: ${response.status}`);
  const body = (await response.json()) as { token: string };
  return body.token;
}

export function getCsrfToken(): Promise<string> {
  tokenPromise ??= fetchToken().catch((error) => {
    tokenPromise = null;
    throw error;
  });
  return tokenPromise;
}

export function resetCsrfToken() {
  tokenPromise = null;
}

async function isCsrfRejection(response: Response) {
  if (response.status !== 403) return false;
  try {
    const body = (await response.clone().json()) as { code?: string };
    return body.code === CSRF_INVALID_CODE;
  } catch {
    return false;
  }
}

export async function csrfFetch(input: Request): Promise<Response> {
  if (!MUTATING.has(input.method)) return fetch(input);

  const retry = input.clone();
  const withToken = async (request: Request) => {
    request.headers.set(CSRF_HEADER, await getCsrfToken());
    return fetch(request);
  };

  const response = await withToken(input);
  if (!(await isCsrfRejection(response))) return response;

  resetCsrfToken();
  return withToken(retry);
}
