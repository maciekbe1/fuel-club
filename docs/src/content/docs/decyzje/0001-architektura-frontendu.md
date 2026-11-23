---
title: "0001 - Architektura frontendu"
description: Next.js jako BFF, TanStack Query, CSRF, idempotencja, SSE.
sidebar:
  order: 1
---

Status: przyjęta, 2026-11-21

## Kontekst

Next.js 16 (App Router, `cacheComponents`) i ASP.NET Core działają jako dwa procesy. Sesję trzyma cookie HttpOnly z Identity. Prawie wszystkie dane są per-user i mutowalne. Operacje na punktach i kuponach wymagają Idempotency-Key. Czat streamuje SSE z POST.

## Decyzje

### 1. Wspólny origin przez rewrite

`next.config.ts` przepisuje `/api/:path*` na `API_INTERNAL_URL`. Przeglądarka nie zna adresu API, nie potrzeba CORS, a cookie przechodzi bez zmian.

- Rewrite (tablica) działa po sprawdzeniu plików, więc `app/api/**/route.ts` ma pierwszeństwo. Jeśli rewrite buforuje SSE, czat dostaje własny route handler przepuszczający strumień.
- `API_INTERNAL_URL` jest czytany przy buildzie i starcie. Na Railway ustaw go przed buildem obrazu.

### 2. Dane przez TanStack Query, mutacje z klienta

- Server Components tylko dla powłoki i bramki sesji.
- Bez Server Actions do mutacji. Backend jest jedynym miejscem reguł, CSRF i idempotencji, a Server Actions by je dublowały.
- `lib/server/api.ts` (`serverGet`) służy wyłącznie do odczytu w RSC, z przekazaniem cookies.

### 3. Autoryzacja w trzech warstwach

1. `proxy.ts`: brak cookie sesji oznacza redirect na `/login?next=...`. To tylko optymistyczna kontrola.
2. `app/(app)/layout.tsx`: `serverGet("/api/auth/me")` w `<Suspense>`. Brak sesji oznacza redirect. `me` trafia do `useMe(initialData)`.
3. `app/(app)/admin/layout.tsx` (do utworzenia): rola różna od `Admin` oznacza redirect.

Decyduje zawsze backend (401/403). Ukrywanie przycisków jest tylko UX.

### 4. Typy z OpenAPI, nie ręcznie

- API: `AddOpenApi()` + `MapOpenApi()` (Development), dokument pod `/openapi/v1.json`.
- Endpointy zwracają `TypedResults` z nazwanymi rekordami (`Results<Ok<MeResponse>, UnauthorizedHttpResult>`) albo deklarują `.Produces<T>()` / `.ProducesProblem()`. Anonimowe obiekty i `IResult` nie mają schematu.
- Web: `npm run api:types` (przy działającym API) generuje `lib/api/schema.d.ts`, a `openapi-fetch` daje typowane `api.GET/POST`.
- `schema.d.ts` jest commitowany i regenerowany po każdej zmianie kontraktu.

### 5. CSRF przez endpoint

`GET /api/auth/csrf` zwraca `{ token }` i ustawia HttpOnly cookie `fuelclub.csrf`. Klient wysyła nagłówek `X-CSRF-TOKEN` przy POST/PUT/PATCH/DELETE. Backend waliduje przez filtr `.RequireCsrf()` na grupie endpointów.

Dlaczego endpoint, a nie cookie czytelne dla JS: oba cookie zostają HttpOnly, a token nie trafia do `document.cookie`. Do tego to natywny wzorzec ASP.NET Antiforgery.

Token jest powiązany z tożsamością. Po logowaniu i wylogowaniu klient woła `resetCsrfToken()`. Odpowiedź `403 csrf.invalid` (np. sesja zmieniona w innej karcie) powoduje pobranie nowego tokenu i jedno ponowienie, co robi `csrfFetch`.

### 6. Idempotency-Key

`useIdempotentMutation` trzyma klucz w `useRef`:

- te same dane po wyniku nieznanym lub przejściowym (`NetworkError`, `5xx`, `409 idempotency.in_progress`) dają ten sam klucz,
- po sukcesie, ostatecznym `4xx` (zapisany błąd domenowy, walidacja, `409 idempotency.key_reused`) albo zmianie danych powstaje nowy UUID - inaczej każde kolejne kliknięcie odtwarzałoby zapisany błąd,
- mutacje bez danych (`mutate()`) też dostają klucz,
- `409 key_reused` jest pokazywany jako błąd domenowy, nie sieciowy,
- mutacje nie mają auto-retry (`retry: false`). Ponowienie jest świadome i z tym samym kluczem,
- opcja `persist` trzyma klucz nierozstrzygniętej operacji w `sessionStorage` karty, aż przyjdzie ostateczna odpowiedź: wyjście z ekranu ani przeładowanie nie pozwala powtórzyć operacji z nowym kluczem (używa jej wymiana punktów). Wpis jest przypisany do konta (`owner` = id z `/api/auth/me`): inne konto w tej karcie go nie odczyta.
  - Wylogowanie i wygaśnięcie sesji go nie usuwają - ponowne zalogowanie tego samego konta wraca do ponowienia z tym samym kluczem.
  - Zalogowanie innego konta usuwa wpisy pozostałych kont.

### 7. SSE

`features/assistant/services.ts` - `readChatEvents()`: fetch POST przez `api` (openapi-fetch, `parseAs: "stream"`) + `eventsource-parser` + `AbortSignal`. Zdarzenia `meta | tool | delta | confirmation | handoff | done | error` (lista typowana kontraktem `ChatStreamContract`) obsługuje reducer w `useChat` (`hooks.ts`). Koniec strumienia bez `done` lub `error` to błąd. Przycisk z `confirmation` woła zwykły endpoint aktywacji, z własnym kluczem idempotencji.

### 8. Invalidacja cache

Klucze i mapa „co unieważnić po operacji” są w `lib/query-keys.ts`. Wylogowanie robi `queryClient.clear()`, żeby dane poprzedniego użytkownika nie przetrwały.

### 9. Formularze

RHF + zod (`features/*/schemas.ts`). Błędy z ProblemDetails (`errors[field][].code`) mapuje `applyFieldErrors()`, wspólne komunikaty odczytu (brak połączenia, sesja wygasła) daje `readErrorMessage()` z `lib/api/problem.ts`, a słownik `kod -> komunikat PL` leży w schemas danego feature.

## Struktura

```text
app/                    routing; strony cienkie
  (public)/login, (public)/rejestracja
  (app)/layout.tsx      bramka sesji + AppShell
  (app)/...             strony użytkownika
  (app)/admin/...       strony admina (guard w layout)
  providers.tsx         QueryClientProvider
features/<nazwa>/
  services.ts           czyste wywołania HTTP (api.GET/POST), bez Reacta
  hooks.ts              useQuery/useMutation owijające services
  schemas.ts            zod + komunikaty błędów
  components/           komponenty feature
components/
  ui/                   shadcn (generowane)
  layout/               powłoka aplikacji
hooks/                  tylko generyczne, wielofeature'owe (np. useMediaQuery)
lib/
  api/                  client, csrf, problem, schema.d.ts (generowany)
  server/               kod tylko dla RSC (import "server-only")
  idempotency.ts, format.ts (wspólne formatery pl-PL), query-keys.ts, utils.ts
proxy.ts
```

Reguły zależności: `app -> features -> lib`. Feature nie importuje innego feature, a wspólne rzeczy trafiają do `lib/` lub `components/`. `services.ts` nie importuje Reacta.

Jeden komponent na plik (nazwa pliku = nazwa komponentu); stałe, teksty i formatery wspólne dla kilku komponentów - w sąsiednim module `.ts`. Wyjątek: `components/ui/` (konwencja shadcn). Elementy UI ogólnego przeznaczenia bierzemy z rejestru shadcn albo lekkich, popularnych bibliotek; własny kod tylko dla logiki domenowej (idempotencja, kursor, ProblemDetails).

## Stan globalny

- Stan serwera trzyma TanStack Query. Sesja to `useMe()`, nie Context.
- Stan UI jest lokalny (`useState`/`useReducer`). Stan czatu to reducer w `useChatStream`.
- Filtry i zakładki trzymamy w URL (`searchParams`).
- Bez Zustand w MVP. Dodajemy go dopiero, gdy pojawi się stan klienta współdzielony przez niepowiązane drzewa i trasy, którego nie da się trzymać w URL ani w cache serwera.
- Własny React Context tylko dla wartości statycznych dla poddrzewa, np. konfiguracji. Providery bibliotek (QueryClient) są OK.

## Alternatywy

- **Server Actions do mutacji**: krótszy kod, ale CSRF, idempotencja i błędy byłyby obsługiwane w dwóch miejscach (Next i .NET).
- **Wywołania API bezpośrednio z przeglądarki (CORS)**: prościej w konfiguracji, ale cookie między originami wymaga `SameSite=None` i ujawnia adres API.
- **Zustand lub własny Context na sesję**: zbędne, bo sesja jest stanem serwera i trzyma ją cache TanStack Query.
- **Ręcznie pisane typy DTO**: szybki start, potem rozjazd z backendem.

## Konsekwencje

- Każda zmiana kontraktu API wymaga `npm run api:types`.
- Każda nowa grupa endpointów modyfikujących w API musi mieć `.RequireCsrf()`.
- Testy integracyjne API pobierają token CSRF przed POST (`PostWithCsrfAsync`).
