---
title: CSRF
description: Ochrona operacji modyfikujących przy sesji w ciasteczku.
sidebar:
  order: 2
---

## Problem

Przeglądarka dołącza ciasteczko sesji do każdego żądania do naszej domeny, także wysłanego z obcej strony. Bez ochrony złośliwa strona mogłaby wysłać `POST` w imieniu zalogowanego użytkownika.

## Rozwiązanie

ASP.NET Antiforgery w wariancie z nagłówkiem:

1. Klient pobiera token: `GET /api/auth/csrf` zwraca `{ token }` i ustawia ciasteczko `fuelclub.csrf` (HttpOnly, SameSite=Strict).
2. Każdy `POST`, `PUT`, `PATCH` i `DELETE` wysyła token w nagłówku `X-CSRF-TOKEN`.
3. Filtr `RequireCsrf()` sprawdza, czy nagłówek pasuje do ciasteczka i do bieżącego użytkownika. Jeśli nie, zwraca `403` z kodem `csrf.invalid`.

Obca strona może wywołać żądanie z ciasteczkami, ale nie odczyta tokenu, bo nie ma dostępu do odpowiedzi z naszego origin.

## Token zależy od użytkownika

Token wydany anonimowo przestaje działać po zalogowaniu (i odwrotnie). Dlatego frontend:

- po logowaniu, wylogowaniu i rejestracji woła `resetCsrfToken()`,
- po odpowiedzi `403 csrf.invalid` pobiera nowy token i ponawia żądanie raz, np. gdy sesja zmieniła się w innej karcie.

Logika: `apps/web/lib/api/csrf.ts`.

## Dlaczego endpoint, a nie ciasteczko czytelne dla JS

Popularna alternatywa to ciasteczko bez HttpOnly, które JavaScript kopiuje do nagłówka. Wybraliśmy endpoint, bo oba ciasteczka zostają HttpOnly, a to natywny wzorzec ASP.NET.

## Zasada

Każda grupa endpointów z metodami modyfikującymi musi mieć `.RequireCsrf()`. Dotyczy to także logowania: bez ochrony ktoś mógłby zalogować ofiarę na swoje konto.
