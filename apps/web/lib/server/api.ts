import "server-only";
import { cookies } from "next/headers";

const apiInternalUrl = process.env.API_INTERNAL_URL ?? "http://localhost:5271";

export async function serverGet<T>(path: `/api/${string}`): Promise<{ status: number; data: T | null }> {
  const cookieHeader = (await cookies()).toString();
  const response = await fetch(`${apiInternalUrl}${path}`, {
    headers: { cookie: cookieHeader, accept: "application/json" },
    cache: "no-store",
  });
  if (!response.ok) return { status: response.status, data: null };
  return { status: response.status, data: (await response.json()) as T };
}
