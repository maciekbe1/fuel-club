---
title: Uruchomienie testów
description: Testy integracyjne API na prawdziwym PostgreSQL i kontrole frontendu.
sidebar:
  order: 3
---

## Testy integracyjne API

Testy używają osobnej bazy `fuelclub_test` i odmawiają uruchomienia na każdej innej, żeby nie naruszyć danych deweloperskich.

```sh
docker compose up -d postgres-test
FUELCLUB_TEST_DATABASE='Host=localhost;Port=5434;Database=fuelclub_test;Username=fuelclub_test;Password=fuelclub_test' \
  dotnet test apps/api.tests
```

Przed testami fabryka stosuje migracje i usuwa konta testowe z poprzednich przebiegów razem z ich danymi Loyalty.

Klasy testów, które używają bazy, należą do kolekcji `DatabaseCollection`. Mają jedną wspólną fabrykę i działają po kolei. Nowa klasa testów bazodanowych musi mieć `[Collection(DatabaseCollection.Name)]`, inaczej sprzątanie jednej fabryki może usunąć dane innej. Testy jednostkowe (bez bazy) nie potrzebują kolekcji.

## Frontend

```sh
cd apps/web
npm run typecheck
npm run lint
```

## Formatowanie

Te same wersje co w rozszerzeniach VS Code przy zapisie: CSharpier (`dotnet-tools.json`) dla C#, Prettier (`apps/web/package.json`, domyślne ustawienia) dla frontendu. `lib/api/schema.d.ts` jest generowany, więc Prettier go pomija.

```sh
dotnet tool restore
dotnet csharpier format apps          # check: dotnet csharpier check apps
cd apps/web && npm run format         # check: npm run format:check
```

## Testy E2E (Playwright)

Prawdziwe API i osobna baza `fuelclub_e2e` w kontenerze `postgres-test`. Playwright sam uruchamia:

- API na `:5281` - najpierw `dotnet ef database update` na bazie E2E (tworzy ją, jeśli nie istnieje), konta demo wyłączone, kasjer `kasjer-e2e@example.test` (stanowisko `E2E-01`) z `BootstrapCashier` i administrator `admin-e2e@example.test` z `BootstrapAdmin` w zmiennych środowiskowych (`e2e/accounts.ts`; hasła tylko testowe; administrator tworzy oferty w testach wymiany),
- Next na `:3100` z `NEXT_DIST_DIR=.next-e2e`, więc działa obok zwykłego `npm run dev` (`:3000`, `.next`).

```sh
docker compose up -d postgres-test
cd apps/web
npx playwright install chromium   # raz
npm run e2e
```

Next łączy się z API przez proxy `e2e/fault-proxy.mjs` (`:5282`), które przekazuje ruch bez zmian. Ciasteczko `e2e-fault=503` albo `e2e-fault=drop` w kontekście jednego testu wymusza awarię `GET /api/auth/me` (status albo zerwane połączenie) tylko dla tego testu. Dla `POST /api/chat`: `e2e-fault=sse-split` przesyła strumień w 3-bajtowych kawałkach i wstrzymuje go 1,5 s przed pierwszym `delta`, `sse-cut` zrywa połączenie w tym miejscu.

Inna baza: `FUELCLUB_E2E_DATABASE`, ale nazwa bazy musi być `fuelclub_e2e` - inaczej `playwright.config.ts` przerywa uruchomienie przed migracją. Każdy test rejestruje nowe konto (`e2e-<uuid>@example.test`), więc testy nie zależą od siebie i nie wymagają czyszczenia. Serwery nie są używane ponownie (`reuseExistingServer: false`): działający proces mógłby pominąć migrację albo wskazywać inną bazę. Zajęty port `:5281`, `:5282` lub `:3100` kończy uruchomienie błędem.

Raport po błędzie: `test-results/` (trace: `npx playwright show-trace ...`).

:::caution[Planowane]
Evals agenta nie są jeszcze zrealizowane.
:::

## Ręczne sprawdzenie w przeglądarce

`specs/browser-instance.sh start` uruchamia osobną instancję: API `:5291` na bazie `fuelclub_browser` (kontener `postgres-test`, konta demo z hasłem testowym ze skryptu) i Next `:3200` (`.next-browser`). Nie dotyka bazy dev ani `:5271`/`:3000`. `specs/browser-instance.sh stop` ją zatrzymuje.
