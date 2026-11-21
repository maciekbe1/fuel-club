---
title: "0003 - Migracje jako osobny krok"
description: API nie migruje bazy przy starcie.
sidebar:
  order: 3
---

Status: przyjęta, 2026-11-21

## Problem

API stosowało migracje przy każdym starcie. Lokalnie to wygodne, na produkcji niebezpieczne:

- kilka instancji migruje równocześnie (wyścig, blokady),
- nieudana migracja zatrzymuje start aplikacji w trakcie wdrożenia,
- użytkownik bazy aplikacji musi mieć prawa do zmiany schematu, więc przejęcie aplikacji daje kontrolę nad schematem,
- brak momentu na przegląd, kopię bazy i decyzję o wycofaniu.

## Rozwiązanie

- API nie stosuje migracji w żadnym środowisku. `DatabaseSchemaVerifier` przy starcie porównuje migracje w kodzie z `__EFMigrationsHistory` i przy brakach zatrzymuje aplikację z instrukcją.
- Lokalnie: `dotnet ef database update`.
- Wdrożenie: `dotnet ef migrations bundle`, uruchamiany przed startem nowej wersji, z osobnym użytkownikiem bazy z prawami do zmiany schematu.
- Testy: `ApiFactory` migruje bazę testową przed startem hosta.

## Alternatywy

- **Migracje przy starcie wszędzie**: najprostsze, ale z wadami opisanymi wyżej.
- **Migracje przy starcie tylko w Development**: wygodne lokalnie, ale środowiska działają różnie, a lokalnie nie ćwiczymy procesu, który działa na produkcji.
- **Skrypty SQL (`migrations script`) stosowane ręcznie**: pełna kontrola, ale więcej ręcznej pracy. Bundle robi to samo idempotentnie.

## Konsekwencje

- Po `git pull` z nową migracją trzeba uruchomić `dotnet ef database update`. Inaczej API nie wystartuje i powie dlaczego.
- Pipeline wdrożenia ma osobny krok migracji.
- Aplikacja potrzebuje tylko praw do odczytu i zapisu danych.
- Kontrola przy starcie opiera się na historii migracji, nie na rzeczywistym schemacie. Ręczne zmiany w bazie i rozjazd historii ze schematem przejdą niezauważone, dlatego schemat zmieniamy wyłącznie migracjami.
- Oddzielny krok nie gwarantuje, że poprzednia wersja API przetrwa nieudaną migrację. Zależy to od transakcji (wcześniejsze migracje z tego samego uruchomienia i operacje poza transakcją zostają) oraz od zgodności nowego schematu ze starym kodem. Zmiany niezgodne wstecz robimy etapami (expand/contract).
