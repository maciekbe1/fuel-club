---
title: Symulacja ruchu
description: Konta i operacje syntetyczne na osobnej bazie demo, przez publiczne API - przebieg, ponowienie i sprzątanie.
sidebar:
  order: 8
---

Decyzje: [ADR 0017](/decyzje/0017-ruch-syntetyczny-i-rozmowy/). Symulator: `apps/tools/Simulator` (.NET, tylko publiczne HTTP API).

:::caution[Planowane]
P16b: rozmowy według scenariuszy (czat SSE), serwerowy `conversationId` i oznaczenie tur `Synthetic`/`RunId`. P16c: metryki w panelu (P15b). Teraz działa populacja kont (P16a).
:::

## Baza i instancja demo

Osobna baza `fuelclub_demo` (port 5435) i osobna instancja API (port 5301). Baza dev (5433) i testowa (5434) nie są używane.

```sh
docker compose up -d postgres-demo
```

Sekrety przebiegu w pliku lokalnym `.simulation/demo.env` (katalog `.simulation/` jest w `.gitignore`, plik `chmod 600`):

```sh
mkdir -p .simulation && umask 077 && cat > .simulation/demo.env <<EOF2
export SIM_SECRET='$(openssl rand -base64 24)'
export SIM_DEMO_PASSWORD='$(openssl rand -base64 18 | tr -d '/+=')Aa1!'
export SIM_CASHIER_EMAIL='kasjer@demo.fuelclub.test'
export SIM_ADMIN_EMAIL='admin@demo.fuelclub.test'
export SIM_DEMO_DATABASE='Host=localhost;Port=5435;Database=fuelclub_demo;Username=fuelclub_demo;Password=fuelclub_demo'
EOF2
```

- `SIM_SECRET` - z niego symulator wylicza hasła kont symulacji (te same przy każdym ponowieniu, nigdzie niezapisane).
- `SIM_DEMO_PASSWORD` - hasło kont demo (kasjer, admin) tej instancji.

Migracje i start instancji. `Simulation:RunId` ustala środowisko instancji - klient nie może go zmienić:

```sh
source .simulation/demo.env
dotnet ef database update --project apps/api --connection "$SIM_DEMO_DATABASE"
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__DefaultConnection="$SIM_DEMO_DATABASE" \
Simulation__Enabled=true Simulation__RunId=pilot-1 \
Assistant__Provider=demo \
DemoAccounts__Enabled=true DemoAccounts__Password="$SIM_DEMO_PASSWORD" \
RateLimits__AuthPermitLimit=200 \
dotnet run --project apps/api --no-launch-profile --urls http://localhost:5301
```

- `Assistant__Provider=demo` jawnie: user-secrets Development mogą ustawiać Gemini.
- `RateLimits__AuthPermitLimit=200` - wyższy limit logowań tej instancji (konfiguracja serwera). Domyślnie 10/min na IP: 200 kont to ok. 40 minut samych logowań.

## Populacja kont

```sh
source .simulation/demo.env
export SIM_CASHIER_PASSWORD="$SIM_DEMO_PASSWORD" SIM_ADMIN_PASSWORD="$SIM_DEMO_PASSWORD"
dotnet run --project apps/tools/Simulator -- populate --run pilot-1 --accounts 5 \
  --api http://localhost:5301 --auth-per-minute 100
```

- Przed wysłaniem czegokolwiek symulator czyta `GET /api/simulation` i odmawia, gdy instancja nie jest symulacją, ma inny `runId` albo innego dostawcę niż `--expect-provider` (domyślnie `demo`).
- Konta `sim-<run>-<nr>@sim.fuelclub.test`, rozkład po kolei: `Zero` (bez zakupów), `Low` (80 zł → 80 pkt), `Medium` (2 zakupy, kupon rabatowy `Purchased`), `High` (3 zakupy, kupony `Redeemed` - rabat użyty przy zakupie, `Active` - nagroda czeka na odbiór, `Purchased`).
- Operacje wyłącznie przez API: rejestracja, logowanie, wydanie karty, zakupy przez konto kasjera, oferty symulacji przez admina (`SIM <run> rabat` / `hot dog`), wymiana, aktywacja, realizacja.
- Tempo poniżej limitów: `--auth-per-minute` (domyślnie 8, limit API 10/min na IP), `--purchases-per-minute` (domyślnie 50, limit 60/min na kasjera), `--parallel` (domyślnie 4). Na `429` czeka `Retry-After`.

## Manifest i ponowienie

`.simulation/<run>/manifest.json`: konta (e-mail, profil, id użytkownika), oferty przebiegu i każda operacja z kluczem idempotencji, statusem (`Pending`, `Done`, `Failed`) i wynikiem (id kuponu albo zakupu). Bez haseł, ciasteczek, kodów karty i kodów kuponów - kod do realizacji jest czytany z API w chwili użycia.

- Ponowne uruchomienie z tym samym `--run` pomija `Done`, a `Pending`/`Failed` wysyła ponownie z tym samym kluczem: API odtwarza odpowiedź, nic się nie podwaja. Paragon wynika z klucza operacji, a data sprzedaży (`saleDate` w manifeście) to lokalna data stanowiska z chwili założenia przebiegu (`--station-time-zone`, domyślnie `Europe/Warsaw` jak `Loyalty:StationTimeZone`), więc treść ponowienia jest identyczna.
- Rejestracja bez klucza idempotencji: gdy ponowienie dostaje odmowę (konto już istnieje), symulator loguje się hasłem przebiegu - udane logowanie dowodzi, że konto jest tego przebiegu.
- Błąd jednego konta (API, timeout, zła odpowiedź) nie zatrzymuje pozostałych; jego krok zostaje do ponowienia.
- Klucze idempotencji API wygasają ([Idempotencja](/wyjasnienia/idempotencja/)) - ponowienie operacji `Pending` sprzed wygaśnięcia jest bezpieczne, później już nie.

## Sprzątanie przebiegu

```sh
source .simulation/demo.env
dotnet run --project apps/tools/Simulator -- cleanup --run pilot-1
```

- Działa tylko na bazie `fuelclub_demo` (`SIM_DEMO_DATABASE`); na każdej innej odmawia.
- Usuwa konta o dokładnych adresach tego przebiegu (`sim-<run>-NNNN@sim.fuelclub.test` - przebieg `pilot` nie sięga `pilot-1`), także te bez id użytkownika w manifeście, wraz z ich zakupami, kuponami, wpisami punktów, kartami i zgłoszeniami, oraz rekordy idempotencji przebiegu (klientów i operacji kasjera/admina, po kluczach z manifestu), w jednej transakcji. Oferty przebiegu - gdy nie mają cudzych kuponów. Konta demo i katalog zostają.
- Manifest dostaje `cleanedAt`; ten `runId` nie da się już zapełnić - nowy przebieg to nowy `runId`.

## Wyniki (2026-11-23)

`pilot-1`, 5 kont: 26/26 operacji. Ponowienie: bez zmian w bazie. Ponowienie z trzema operacjami oznaczonymi `Pending` (zakup, realizacja kuponu, wymiana): bez podwojeń. Sprzątanie na bazie testowej: odmowa; na demo: 5 kont, 7 zakupów, 5 kuponów, 2 oferty usunięte, konta demo zostały.

`pilot-2` (po review): 26/26; ponowienie z dwiema rejestracjami `Pending` - bez błędów; sprzątanie z kontem bez id w manifeście - usunięte wszystkie 5 kont i 21 rekordów idempotencji.
