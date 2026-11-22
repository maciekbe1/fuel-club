import { NextResponse, type NextRequest } from "next/server";

const SESSION_COOKIE = ".AspNetCore.Identity.Application";

/**
 * The API limits logins per client address from X-Forwarded-For. Next does not set that header on
 * the /api rewrite and passes a client-supplied one through, so it is dropped unless an edge proxy
 * in front of Next sets it (TRUST_FORWARDED_FOR=true, see ADR 0009). Without it every client shares
 * the Next server's address.
 */
function forwardApi(request: NextRequest) {
  if (process.env.TRUST_FORWARDED_FOR === "true") return NextResponse.next();
  const headers = new Headers(request.headers);
  headers.delete("x-forwarded-for");
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
