---
title: "0018 - Metryki tur, rozmów i koszt"
description: Definicje liczników, mianowników, przekazania i kosztu w panelu obserwowalności (P15b na danych P16).
sidebar:
  order: 18
---

## Status

Decyzja przyjęta (2026-11-23), P16c. Uzupełnia [ADR 0017](/decyzje/0017-ruch-syntetyczny-i-rozmowy/) (rozmowa, oznaczenie symulacji) i [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/) (panel, reguły).

## Problem

Panel liczył tylko tury. Do pokazu i oceny agenta potrzebne są metryki z jawnymi definicjami, tak żeby liczba nie sugerowała więcej, niż mierzy: rozmowy, przekazania, kroki modelu, narzędzia i koszt, osobno dla ruchu rzeczywistego i syntetycznego.

## Rozwiązanie

### Okno i filtry

- Okno: ostatnie `hours` (1-720). Tura należy do okna po `StartedAt`, rozmowa po `StartedAt`.
- `traffic`: `real` (domyślnie, `Synthetic = false`), `synthetic`, `all`. Dalej `runId`, `model`, `promptVersion`.
- Filtry dotyczą tur i rozmów. Rozmowa pasuje do filtra modelu lub wersji promptu, gdy pasuje któraś z jej tur.
- Zdarzenia bezpieczeństwa i reguły anomalii nie są filtrowane (liczą wszystko, w swoich oknach).
- Listy wartości filtrów pochodzą z okna i wybranego ruchu.

### Osobne liczniki

- **Tury**: wynik (`Done`/`Error`/`Cancelled`), p50/p95 pierwszego zdarzenia i całej tury (`percentile_cont`).
- **Kroki modelu**: wywołania modelu w turze, także nieudane (`ModelSteps`). Suma, p95 na turę i liczba tur z danymi (tury sprzed P16c nie mają tej wartości).
- **Wywołania narzędzi**: liczba wpisów w `Tools` (to samo źródło co rozbicie nazwa/stan, więc udział „nie `ok`” nie przekroczy 100%). „Nie `ok`” to stany `rejected`, `error`, `timeout`, `no_match` itp. Mianownik to wywołania, nie tury.
- **Rozmowy**: rozpoczęte w oknie. Otwarte to te bez zakończenia i nie dłużej bezczynne niż `Assistant:ConversationIdleTimeout`. Zakończone dzielą się na `Client`, `Idle` i `Handoff`. Rozmowa bezczynna ponad limit liczy się jako `Idle`, zanim serwer ją zamknie.

### Przekazanie

- Liczone raz na rozmowę:
  - „z propozycją konsultanta”: rozmowy z co najmniej jedną turą `HandoffProposed`;
  - „ze zgłoszeniem”: rozmowy z `SupportCaseId`, czyli przekazanie.
- „Zakończone bez zgłoszenia” / „zakończone” to metryka **odsetek rozmów bez przekazania**.
- To nie jest containment ani skuteczność. `Idle` i `Client` nie oznaczają rozwiązania sprawy; klient mógł zrezygnować.

### Koszt

- Szacunek, nie faktura dostawcy. Liczony przy zapisie tury: tokeny × cena z `Assistant:Pricing`.
  - Cena jest w USD za milion tokenów wejścia i wyjścia. Obowiązuje wpis z najpóźniejszym `EffectiveFrom` nie późniejszym niż tura.
  - Zapisany razem z wersją ceny (`CostUsd`, `PriceVersion`), więc edycja cennika nie zmienia historii.
- Tokeny wyjścia zawierają już tokeny „myślenia” Gemini, więc nie liczymy ich drugi raz.
- **Demo: „nie dotyczy”**: adapter nie ma ceny ani tokenów (`CostUsd = null`, bez wersji).
- **Nieznany**: tura nie-demo z wywołaniem modelu, ale bez danych o użyciu albo bez ceny modelu. Nigdy zero.
- **Zero**: tura nie-demo bez wywołania modelu (`ModelSteps = 0`: klient odszedł albo błąd przed modelem) - nic nie wydano.
- Cennik: wersje unikalne, najwyżej jeden wpis na dostawcę, model i `EffectiveFrom` (inaczej wybór zależałby od kolejności).
  - Suma w panelu obejmuje tylko znane tury i jest pełna, gdy nieznanych jest 0.
- Domyślny wpis cennika (`gemini-flash-lite-assumed-2026-11-23`: 0,30 / 2,50 USD za milion) to założenie z evali, do potwierdzenia u dostawcy przed pokazaniem kosztu jako faktu.
- Koszt judge'a liczony osobno (P16d).

### Ruch demo

- 2000 tur na adapterze demo pokazuje działanie aplikacji, panelu i przepływów. To nie jest pomiar wydajności ani kosztu Gemini.
- Panel oznacza widok, w którym wszystkie tury są z adaptera demo.

## Alternatywy

- **Koszt liczony w zapytaniu z bieżącego cennika**: prostsze, ale zmiana ceny przepisałaby historię.
- **Brak danych jako 0**: zaniża koszt i miesza demo z nieznanym.
- **Przekazanie jako liczba zdarzeń `handoff`**: dwie propozycje w jednej rozmowie liczyłyby się podwójnie.
- **Filtrowanie anomalii po ruchu**: reguły bezpieczeństwa muszą widzieć wszystko.

## Konsekwencje

- Nowe kolumny tury: `ModelSteps`, `CostUsd` (`CHECK >= 0`), `PriceVersion` (migracja `MetricsAndCost`).
- Zmiana kontraktu `GET /api/admin/observability`, regeneracja `schema.d.ts`.
- Odmowy na karcie potwierdzenia („Nie teraz”) nie są rejestrowane na serwerze: metryka odmów wymaga osobnego zdarzenia (później).
