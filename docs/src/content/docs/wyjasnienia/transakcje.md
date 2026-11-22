---
title: Transakcje
description: Dlaczego niektóre operacje są atomowe.
sidebar:
  order: 4
---

## Zasada

Operacja, która zmienia kilka rekordów, musi zakończyć się w całości albo wcale. Inaczej po błędzie w połowie zostaje stan, którego reguły biznesowe nie przewidują.

## Rejestracja

Utworzenie konta i nadanie roli `User` to dwa zapisy. Bez transakcji błąd przy nadawaniu roli zostawiłby konto bez roli. Dlatego `IdentityAccountsService.RegisterAsync` otwiera transakcję i zatwierdza ją dopiero po obu krokach.

## Administrator startowy

`AdminAccountProvisioner` w jednej transakcji sprawdza, czy konto istnieje, tworzy je i nadaje rolę. Odmawia awansu istniejącego zwykłego konta.

## Baza jako ostatnia linia

Kod sprawdza reguły, ale równoległe żądania mogą je ominąć między odczytem a zapisem. Dlatego niezmienniki są też w bazie: `UNIQUE` (np. e-mail) i `CHECK`.

## Operacje idempotentne

Transakcję operacji z `Idempotency-Key` otwiera `IdempotencyStore`, nie handler: rezerwacja klucza, savepoint, zmiana domenowa i zapis odpowiedzi to jedna transakcja. Handler pisze przez ten sam `AppDbContext` i nie otwiera własnej transakcji. Opis: [idempotencja](/wyjasnienia/idempotencja/).

W transakcji idempotencji działają: zakup przez kasę razem z odbiorem kuponu (zakup, kupon `Active → Redeemed`, saldo, wpis historii - [Loyalty](/moduly/loyalty/#przepływ), [Rewards](/moduly/rewards/#odbiór-razem-z-zakupem)), wydanie i wymiana karty, wymiana punktów na kupon, aktywacja kuponu. Błąd po częściowym zapisie wycofuje całość.
