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
- Każdy przypadek to lista nazwanych sprawdzeń (`tools`, `arguments`, `answer`, `safety`, `turn`, `trace`). Na modelu demo wszystkie muszą przejść.

## Evale na Gemini

Te same przypadki na prawdziwym modelu. Tylko na żądanie, nigdy w CI, nie blokują testów - zapisują raport.

```sh
FUELCLUB_GEMINI_EVALS=pilot FUELCLUB_TEST_DATABASE='…' \
  dotnet test apps/api.tests --filter "FullyQualifiedName~Gemini_report" --logger "console;verbosity=detailed"
```

- Klucz: `FUELCLUB_GEMINI_API_KEY` albo user-secrets API (`Assistant:Gemini:ApiKey`). Nie trafia do raportu (test to sprawdza).
- `pilot`: 5 przypadków × 1 (saldo, cudzy kupon, propozycja aktywacji, regulamin, brak odpowiedzi + konsultant). `full`: wszystkie przypadki bez modelu skryptowego × `FUELCLUB_GEMINI_EVAL_REPEATS` (domyślnie 3).
- Limity: `Assistant:MaxToolCalls` (kroki), na przypadek 40 000 tokenów wejścia i 4 000 wyjścia (sprawdzenie `budget`), bez ponawiania przypadku; klient HTTP ponawia przejściowy błąd najwyżej 2 razy ([ADR 0011](/decyzje/0011-adapter-gemini/)). Dwa limity przebiegu, sprawdzane przed każdym przypadkiem: `FUELCLUB_GEMINI_EVAL_TOKEN_BUDGET` (domyślnie 400 000 tokenów) i `FUELCLUB_GEMINI_EVAL_MAX_USD` (domyślnie 0,25 USD). Przypadek nie jest przerywany w trakcie, więc przebieg może przekroczyć limit najwyżej o jeden przypadek. Po osiągnięciu limitu reszta przypadków jest w raporcie jako pominięta, a raport ma oznaczenie „NIEPEŁNY” (`complete: false` w JSON).
- Odpowiedź modelu sprawdzają wzorce `answerPatterns` (model formułuje po swojemu); bez nich - `answerContains`.
- Raport: `apps/api.tests/Evals/reports/gemini-<tryb>-<data>.md` i `.json` - model, prompt, commit, wynik każdego sprawdzenia, tokeny, koszt (ceny w `GeminiReport`, nadpisywane `FUELCLUB_GEMINI_PRICE_INPUT`/`_OUTPUT`) i szacunek pełnego przebiegu.

Pierwszy pilotaż (2026-11-23, `gemini-3.5-flash-lite`, `gemini-v5`): [raport](https://github.com/maciekbe1/fuel-club/blob/main/apps/api.tests/Evals/reports/) - 4/5 przypadków w pełni, bezpieczeństwo 16/16, ok. 0,005 USD; pełny przebieg szacunkowo 0,05 USD. Wykryty błąd: na „Co regulamin mówi o płatności kartą kredytową?” Gemini wyszukuje po swojemu, dostaje §2.3 (kod karty nie służy do płatności) i podaje go jako odpowiedź - bez „regulamin tego nie opisuje” i bez propozycji konsultanta. Powtarzalne (3/3 przebiegów). Poprawka w `gemini-v6` (cytat tylko fragmentu, który odpowiada na pytanie); pilotaż po poprawce: 5/5.

## Ślady w Phoenix (opcjonalnie)

```sh
docker compose --profile observability up -d phoenix        # UI i OTLP na :6006 (PHOENIX_PORT=… zmienia port)
Telemetry__OtlpEndpoint=http://localhost:6006/v1/traces dotnet run --project apps/api --launch-profile http
```

- Po kilku pytaniach w czacie ślady pojawiają się w Phoenix, w projekcie `fuel-club`, z opóźnieniem do ~5 s (eksport wsadowy). Widać `chat.turn` z potomnymi `chat.tool` i atrybuty `fuelclub.*`, bez treści rozmowy.
- Phoenix nie ma wolumenu: `docker compose rm -sf phoenix` usuwa ślady.
- Bez `Telemetry:OtlpEndpoint` nic nie jest eksportowane. Wiersze `assistant_turns` w bazie powstają zawsze.
