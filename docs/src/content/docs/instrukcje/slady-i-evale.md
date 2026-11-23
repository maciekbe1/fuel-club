---
title: Ślady i evale czatu
description: Jak uruchomić evale asystenta i obejrzeć ślady tur w lokalnym Phoenix.
sidebar:
  order: 6
---

Decyzja i zakres danych: [ADR 0008](/decyzje/0008-slady-i-evale-czatu/).

## Evale

Evale to testy xUnit na bazie testowej (jak [testy API](/instrukcje/testy/)):

```sh
FUELCLUB_TEST_DATABASE='Host=localhost;Port=5434;Database=fuelclub_test;Username=fuelclub_test;Password=fuelclub_test' \
  dotnet test apps/api.tests --filter "FullyQualifiedName~AssistantEvals" --logger "console;verbosity=normal"
```

- Każdy przypadek wypisuje wiersz `assistant-evals-v2 <id> [<kategoria>] ok: <wynik>, tools=<n>, <ms> ms`.
- Przypadki są w `apps/api.tests/Evals/assistant-cases.v2.json`. Nowy przypadek to nowy wpis z unikalnym `id` i jedną z kategorii. Zmiana znaczenia istniejących przypadków to nowa wersja pliku.
- Modele skryptowe (`"model": "bad-arguments" | "provider-failure" | "tool-loop"`) są w `AssistantEvals.Scripted`.

## Ślady w Phoenix (opcjonalnie)

```sh
docker compose --profile observability up -d phoenix        # UI i OTLP na :6006 (PHOENIX_PORT=… zmienia port)
Telemetry__OtlpEndpoint=http://localhost:6006/v1/traces dotnet run --project apps/api --launch-profile http
```

- Po kilku pytaniach w czacie ślady pojawiają się w Phoenix, w projekcie `fuel-club`, z opóźnieniem do ~5 s (eksport wsadowy). Widać `chat.turn` z potomnymi `chat.tool` i atrybuty `fuelclub.*`, bez treści rozmowy.
- Phoenix nie ma wolumenu: `docker compose rm -sf phoenix` usuwa ślady.
- Bez `Telemetry:OtlpEndpoint` nic nie jest eksportowane. Wiersze `assistant_turns` w bazie powstają zawsze.
