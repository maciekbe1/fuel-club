import { NextResponse, type NextRequest } from "next/server";

const SESSION_COOKIE = ".AspNetCore.Identity.Application";

export function proxy(request: NextRequest) {
  if (request.cookies.has(SESSION_COOKIE)) return NextResponse.next();
  const login = new URL("/login", request.url);
  login.searchParams.set("next", request.nextUrl.pathname);
  return NextResponse.redirect(login);
}

export const config = {
  matcher: ["/((?!api|login|rejestracja|_next|favicon.ico|.*\\..*).*)"],
};
