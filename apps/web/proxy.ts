import { NextResponse, type NextRequest } from "next/server";

const SESSION_COOKIE = ".AspNetCore.Identity.Application";

/**
 * The API trusts X-Forwarded-For (login limit per client) and X-Forwarded-Proto (Secure cookies).
 * Next does not set the first on the /api rewrite and passes client-supplied values through, so both
 * are dropped unless an edge proxy in front of Next sets them (TRUST_FORWARDED_FOR=true, ADR 0009).
 * Without it every client shares the Next server's address and the API sees plain HTTP.
 */
function forwardApi(request: NextRequest) {
  if (process.env.TRUST_FORWARDED_FOR === "true") return NextResponse.next();
  const headers = new Headers(request.headers);
  headers.delete("x-forwarded-for");
  headers.delete("x-forwarded-proto");
  return NextResponse.next({ request: { headers } });
}

export function proxy(request: NextRequest) {
  if (request.nextUrl.pathname.startsWith("/api/")) return forwardApi(request);
  if (request.cookies.has(SESSION_COOKIE)) return NextResponse.next();
  const login = new URL("/login", request.url);
  login.searchParams.set("next", request.nextUrl.pathname);
  return NextResponse.redirect(login);
}

export const config = {
  matcher: [
    "/api/:path*",
    "/((?!api|login|rejestracja|_next|favicon.ico|.*\\..*).*)",
  ],
};
