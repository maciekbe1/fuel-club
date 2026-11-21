---
title: Identity, sesja i cookies
description: Jak działa logowanie i dlaczego frontend nie widzi tokenów.
sidebar:
  order: 1
---

## Identity

ASP.NET Core Identity przechowuje konta i role w PostgreSQL i haszuje hasła. Sami nie piszemy kryptografii ani porównywania haseł.

Logowanie (`SignInManager.PasswordSignInAsync`) po sukcesie ustawia ciasteczko `.AspNetCore.Identity.Application` z zaszyfrowaną tożsamością użytkownika.

## Dlaczego cookie, a nie token w JavaScript

- Ciasteczko jest **HttpOnly**: skrypt na stronie go nie odczyta, więc atak XSS nie wykradnie sesji.
- Przeglądarka wysyła je sama przy każdym żądaniu do tego samego origin.
- Frontend i API są dla przeglądarki jednym origin. Next.js przepisuje `/api/*` na prywatny adres API, więc nie potrzeba CORS ani przekazywania tokenów.

Ceną jest podatność na CSRF - patrz [CSRF](/wyjasnienia/csrf/).

## 401 zamiast przekierowania

Domyślnie Identity przekierowuje niezalogowanego użytkownika na stronę logowania. API to nie strona, więc `ConfigureApplicationCookie` zamienia to na `401` (brak sesji) i `403` (brak uprawnień). O przekierowaniu decyduje frontend.

## Gdzie frontend sprawdza sesję

1. `proxy.ts` - brak ciasteczka oznacza od razu `/login`. To tylko szybka kontrola, ciasteczko może być wygasłe.
2. `app/(app)/layout.tsx` - po stronie serwera woła `GET /api/auth/me` z ciasteczkami użytkownika. Tylko `401` oznacza przekierowanie na `/login`. Awaria API (brak połączenia, `5xx`) nie jest wylogowaniem: strona pokazuje „Serwis chwilowo niedostępny” z przyciskiem ponowienia (`router.refresh()`), a ciasteczko sesji zostaje.
3. API - zawsze ostatecznie. Ukrycie przycisku w UI nie jest zabezpieczeniem.

## Blokada konta

Logowanie używa `lockoutOnFailure: true`. Po domyślnych 5 nieudanych próbach konto jest blokowane na 5 minut. API zwraca wtedy ten sam `401 accounts.invalid_credentials`, żeby nie zdradzać, że konto istnieje.

:::caution[Planowane]
Limity prób na poziomie adresu IP (rate limiting) dla logowania, odbioru kodu i czatu.
:::
