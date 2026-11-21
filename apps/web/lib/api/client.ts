import createClient from "openapi-fetch";
import { csrfFetch } from "./csrf";
import { NetworkError, toApiError } from "./problem";
import type { paths } from "./schema";

export const api = createClient<paths>({
  baseUrl: "",
  credentials: "same-origin",
  fetch: async (request) => {
    try {
      return await csrfFetch(request);
    } catch (error) {
      throw new NetworkError(error);
    }
  },
});

export async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok) throw toApiError(response.status, error);
  return data as T;
}
