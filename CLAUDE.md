# Fuel Club

Demo programu lojalnościowego stacji paliw: punkty za tankowanie, kupony, czat AI (SSE). Zakres i niezmienniki: `PLAN-MVP.md`. Dokumentacja (Starlight, Diátaxis): `docs/src/content/docs/`. Architektura frontendu: `docs/src/content/docs/decyzje/0001-architektura-frontendu.md`.

## Tryb pracy

Claude implementuje uzgodnione kroki, użytkownik decyduje o zakresie i sprawdza wynik. Przed realizacją ustal szczegóły, które zmieniają model lub kontrakt. Każdy krok: testy + aktualizacja `docs/`.

## Układ

- `apps/api` - ASP.NET Core 10, modularny monolit, `Modules/<Nazwa>/<Przypadek>/...Endpoint.cs`
- `apps/api.tests` - xUnit, integracyjne na prawdziwym PostgreSQL (`fuelclub_test`)
- `apps/web` - Next.js 16 App Router; przed pisaniem kodu Next przeczytaj `apps/web/AGENTS.md` (API różni się od danych treningowych - docs w `node_modules/next/dist/docs/`)
- `docs` - Astro Starlight; sekcje: nauka, instrukcje, referencja, wyjasnienia, moduly, decyzje
- `compose.yml` - `postgres` (5433, dev), `postgres-test` (5434)

## Komendy

```sh
docker compose up -d postgres postgres-test
dotnet tool restore && dotnet ef database update --project apps/api   # API nie migruje samo
dotnet run --project apps/api --launch-profile http          # :5271
FUELCLUB_TEST_DATABASE='Host=localhost;Port=5434;Database=fuelclub_test;Username=fuelclub_test;Password=fuelclub_test' dotnet test apps/api.tests
cd apps/web && npm run dev                                   # :3000, /api/* -> API
cd apps/web && npm run api:types                             # przy działającym API
cd apps/web && npm run typecheck && npm run lint
cd apps/web && npm run e2e                                   # Playwright: API :5281 + web :3100, baza fuelclub_e2e (5434)
cd docs && npm run dev                                       # dokumentacja; npm run build sprawdza
```

## Dokumentacja

- Zmiana zachowania aplikacji i aktualizacja `docs/` w tym samym commicie/PR. Dotyczy endpointów, kodów błędów, konfiguracji, tabel, zabezpieczeń i testów.
- Strona modułu (`moduly/<nazwa>.md`): co robi → gdzie jest kod → przepływ → dane → zabezpieczenia → testy.
- Niezrealizowane oznaczaj `:::caution[Planowane]`. Nie opisuj planu jak działającego kodu.
- Istotna decyzja = nowy ADR w `decyzje/` (problem, rozwiązanie, alternatywy, konsekwencje).
- Kontrakt HTTP jest w OpenAPI; docs opisują to, czego OpenAPI nie pokazuje.

## Zasady - API

- Nie dodawaj polskich komentarzy do kodu; komentarze pisz po angielsku albo pomijaj.
- Endpointy zwracają `TypedResults` z nazwanymi rekordami lub deklarują `.Produces<T>()` / `.ProducesProblem()` - inaczej brak typów na froncie.
- Błędy: ProblemDetails z `code` (`<moduł>.<błąd>`) i `errors[field][] { field, code }`.
- Grupy z metodami modyfikującymi: `.RequireCsrf()`.
- Saldo + ledger w jednej transakcji; UNIQUE/CHECK w bazie jako ostatnia linia; Idempotency-Key dla tankowania, wydania i wymiany karty, wymiany punktów, aktywacji, odbioru.
- Po każdej zmianie kontraktu HTTP: `npm run api:types` i commit `schema.d.ts` w tej samej zmianie.
- Autoryzacja i kontrola właściciela zawsze na backendzie.
- Docelowy proces: skan karty klienta -> zakup z kasy -> punkty. Kasjer (rola Cashier) realizuje aktywny kupon; Admin zarządza ofertami, nie zastępuje kasjera. Panel kasjera to symulator POS. Kod karty i token kuponu są odrębne. Decyzja: docs/src/content/docs/decyzje/0004-karta-i-kasa.md.

## Zasady - web

- `features/<x>/services.ts` (HTTP, bez Reacta) -> `hooks.ts` (TanStack Query) -> `components/`. Strony w `app/` cienkie.
- Feature nie importuje innego feature.
- Typy tylko z `lib/api/schema.d.ts` (generowany) - nie pisz typów DTO ręcznie.
- Mutacje z klienta przez `api` z `lib/api/client.ts`; bez Server Actions. RSC tylko czyta przez `lib/server/api.ts`.
- Operacje idempotentne: `useIdempotentMutation`. Klucze cache: `lib/query-keys.ts`.
- Bez Zustand i własnych Contextów bez uzasadnienia w ADR.
- UI po polsku, responsywny. Runtime API (`cookies()`, `useSearchParams`) w `<Suspense>` (wymóg `cacheComponents`).

## Bezpieczeństwo

Nie loguj haseł, cookies, kluczy API ani kodów odbioru. Sekrety tylko w zmiennych środowiskowych / user-secrets.
