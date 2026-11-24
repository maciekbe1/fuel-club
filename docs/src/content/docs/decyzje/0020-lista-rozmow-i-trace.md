---
title: "0020 - Lista rozmów i trace w panelu"
description: Lista rozmów admina z kursorem i filtrem podejrzanych, szczegół rozmowy jako sekwencja tur - tylko metadane (P15c).
sidebar:
  order: 20
---

## Status

Decyzja przyjęta (2026-11-24), P15c. Uzupełnia [ADR 0008](/decyzje/0008-slady-i-evale-czatu/) (tury bez treści), [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/) (kolejka przeglądu) i [ADR 0017](/decyzje/0017-ruch-syntetyczny-i-rozmowy/) (rozmowa).

## Problem

Panel pokazywał metryki i kolejkę pojedynczych tur. Admin nie mógł zobaczyć rozmowy jako całości: które tury do niej należą, jak się skończyła, gdzie był błąd i ile kosztowała. Kolejka miała stały limit bez stron.

## Rozwiązanie

- `GET /api/admin/observability/conversations`: rozmowy rozpoczęte w oknie (`hours` 1-720), najnowsze pierwsze. Filtry jak w metrykach (`traffic`, `runId`, `model`, `promptVersion`; model i prompt muszą pasować do tej samej tury) i `suspicious`.
- Podejrzana rozmowa: co najmniej jedna tura z błędem albo z narzędziem o statusie innym niż `ok`, w turze nieprzerwanej przez klienta. Ta sama reguła co w kolejce przeglądu, ale przejrzane tury nadal się liczą: lista to historia, kolejka to praca do zrobienia. Flagi wiersza i tury są tylko z takich tur - przerwanie przez klienta i „powtarzające się błędy” użytkownika na czystej rozmowie nie dają flagi.
- Strony kluczem `(StartedAt, Id)`, kursor `nextCursor` (`limit` 1-100, domyślnie 25). Koniec okna pierwszej strony jest w kursorze, więc kolejne strony mają to samo okno. Strona nie powtarza ani nie pomija wierszy istniejących przy pierwszym odczycie. Nowe rozmowy pojawiają się po odświeżeniu pierwszej strony.
- Wiersz: pseudonim, czasy, powód zakończenia, liczba tur, modele i wersje promptu z tur, propozycja konsultanta, `hasCase`, flagi (`error`, `rejected_tool`, `tool_failure`, `repeated_errors`).
- `GET /api/admin/observability/conversations/{id}`: rozmowa i jej tury po kolei. Tura to trace: pierwsze zdarzenie i całość, wynik i kod błędu, kroki modelu, tokeny, szacowany koszt z wersją cennika, narzędzia (nazwa, status, czas), znacznik przeglądu.
- Pozycja kolejki ma `conversationId`, więc z kolejki można przejść do całej rozmowy.
- Tylko metadane i pseudonimy. Zgłoszenie konsultanta tylko jako `hasCase` - jego treść zostaje przy Support.

## Alternatywy

- **Strony offsetem**: prostsze, ale przy nowych rozmowach strony przesuwają się i powtarzają wiersze.
- **Trace tylko w Phoenix**: Phoenix jest opcjonalny i lokalny; panel musi działać bez niego.
- **Treść rozmów w trace**: wymaga osobnej decyzji o zakresie i retencji (SPEC), poza P15c.

## Konsekwencje

- Tury sprzed P16b (bez `ConversationId`) nie mają rozmowy i są widoczne tylko w kolejce.
- Rozmowa i tury wygasają według `Observability:TraceRetention`. Rozmowa bez tur w retencji pokazuje „brak zapisanych tur”.
- Osobne spany wywołań modelu w trace (P15a) nadal nie istnieją: tura podaje liczbę kroków modelu, nie czas każdego kroku.
