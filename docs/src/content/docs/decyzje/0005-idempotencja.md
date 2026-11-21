---
title: "0005 - Idempotency-Key w bazie i filtrze endpointu"
description: Rezerwacja klucza, savepoint i zapis odpowiedzi w jednej transakcji PostgreSQL.
sidebar:
  order: 5
---

## Status

Decyzja przyjęta i zrealizowana jako infrastruktura. Endpointy domenowe korzystają z niej od kroku 3.2.

## Problem

Tankowanie, wydanie i wymiana karty, wymiana punktów, aktywacja i odbiór muszą dać ten sam skutek przy ponowieniu po utracie odpowiedzi. Naiwne „sprawdź, potem zapisz” przepuszcza równoległe żądania. Zapis błędów domenowych wymaga wycofania częściowych zmian bez zwalniania klucza.

## Rozwiązanie

- Endpoint oznaczony `.RequireIdempotency()`. Filtr koordynuje: nagłówek, odcisk, przechwycenie odpowiedzi. Transakcję i zapis prowadzi osobna usługa `IdempotencyStore`.
- Klucz unikalny per wykonawca: PK `(UserId, IdempotencyKey)`.
- Rezerwacja to `INSERT ... ON CONFLICT DO NOTHING` w transakcji, potem savepoint, operacja i zapis odpowiedzi w tej samej transakcji. Zatwierdzony wiersz zawsze ma odpowiedź.
- Błąd domenowy: rollback do savepointu, wyczyszczenie śledzenia EF, zapis błędu, commit. 400 i 5xx: pełny rollback, nic nie zostaje.
- Odcisk: SHA-256 z metody, szablonu i wartości trasy, query, kanonicznego JSON. Duplikaty pól odrzucane bez względu na wielkość liter (jak wiązanie DTO).
- Równoległość: czekanie na blokadzie indeksu z `lock_timeout` (1 ms - 30 s, walidowane przy starcie) i anulowaniem; po limicie `409 idempotency.in_progress`, niezapisywane.
- Autoryzacja i CSRF przed odtworzeniem. Zapis tylko statusu, `Content-Type`, `Location` i treści.

## Alternatywy

- **Middleware zamiast filtra**: działałby przed autoryzacją endpointu i bez dostępu do wyniku `IResult`; trudniej zagwarantować sprawdzenie uprawnień przed odtworzeniem.
- **Status „w trakcie” zatwierdzany osobno**: widoczny dla innych żądań, ale po awarii procesu zostaje zablokowany klucz i potrzebne jest jego sprzątanie. Rezerwacja w niezatwierdzonej transakcji znika sama.
- **Advisory lock zamiast wiersza**: dodatkowy mechanizm obok unikalności, którą i tak trzeba mieć.
- **Redis**: osobna infrastruktura i brak wspólnej transakcji ze zmianą domenową.

## Konsekwencje

- Handler idempotentnego endpointu nie otwiera własnej transakcji i pisze przez scoped `AppDbContext`.
- `400` oznacza odrzucenie przed operacją; błąd po zmianie danych musi mieć kod domenowy.
- Połączenie z bazą jest zajęte przez cały czas operacji (dotyczy też czekającego żądania, do `LockTimeout`).
- Różna zapisowo, ale równoważna treść (`10` vs `10.0`) daje `409` zamiast odtworzenia.
- Tabela rośnie bez retencji; sprzątanie to późniejsza decyzja.
