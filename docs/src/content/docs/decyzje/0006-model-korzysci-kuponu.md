---
title: "0006 - Model korzyści kuponu"
description: Rabat na litr realizowany razem z zakupem paliwa, punkty od kwoty zapłaconej.
sidebar:
  order: 6
---

## Status

Decyzja przyjęta. Zrealizowane: encje, ograniczenia w bazie, migracja, reguły obliczeń z testami, wymiana, aktywacja, podgląd z limitem prób. Planowane: odbiór razem z zakupem.

## Problem

Plan mówił, że kasjer „potwierdza zastosowanie korzyści do zakupu”, ale nie określał, czym jest korzyść, do jakiego zakupu się odnosi ani jak kasa ją liczy. Bez tego nie da się zaprojektować encji ani sprawdzić jednorazowości rabatu.

## Rozwiązanie

- **Jeden rodzaj korzyści: rabat na litr.** Oferta definiuje stawkę (zł/l, np. 0,20) i limit litrów objętych rabatem. Dowolne paliwo; rodzaju paliwa nie zapisujemy.
- **Kupon realizowany razem z zakupem paliwa**, w jednej transakcji: zakup, rabat, jednorazowe zużycie kuponu (`Active → Redeemed`) i punkty. Kupon wskazuje zakup (`RedeemedPurchaseId`, `UNIQUE`): jeden kupon na zakup.
- **Obliczenie po stronie serwera**: `rabat = round(stawka × min(litry, limit), 2, AwayFromZero)`, najwyżej kwota brutto. Klient wysyła kod kuponu, nigdy kwotę rabatu.
- **Kwoty zakupu**: `GrossAmount` (paragon), `Discount`, `PaidAmount = GrossAmount - Discount` (`CHECK`). `PaidAmount = 0` jest dozwolone.
- **Punkty od kwoty zapłaconej**: `floor(PaidAmount)`; 0 zł → 0 pkt, bez błędu.
- **Snapshot przy wymianie**: nazwa, koszt, stawka i limit kopiowane do kuponu. Edycja ani wyłączenie oferty nie zmienia wydanych kuponów; wyłączenie blokuje tylko nowe wymiany. Oferty się nie usuwa (FK `RESTRICT`).
- **Bez terminu ważności** w tym zakresie.
- **Właściciel kuponu = właściciel zeskanowanej karty**, sprawdzane na backendzie. Cudzy i nieistniejący kod dają ten sam błąd.
- **Kod kuponu**: `K` + 128 losowych bitów w 26 znakach Crockford Base32 (27 znaków; pierwszy znak losowy 0-7). Format karty bez zmian. Prefiks pomaga skanerowi; backend i tak sprawdza typ, właściciela i status. Kod powstaje przy aktywacji - kupon nieaktywny nie ma czego skanować, więc `coupon_not_active` to zabezpieczenie stanu, nie zwykły scenariusz kasjera.
- **Podgląd** (karta + kupon): od razu stawka i limit; końcowy rabat i kwota do zapłaty dopiero po podaniu litrów i kwoty brutto. Podgląd niczego nie zużywa i ma limit prób od pierwszego udostępnienia endpointu.

## Alternatywy

- **Rabat kwotowy (np. 10 zł od 100 zł)** obok rabatu na litr: dwa rodzaje to więcej reguł i stanów bez nowej wartości dla demo. Odrzucone na ten zakres.
- **Darmowy produkt (np. kawa)**: kasa zna tylko paliwo; bez katalogu towarów i koszyka „wydanie” niczego nie liczy i nie wiąże się z zakupem.
- **Odbiór kuponu jako osobna operacja, bez zakupu**: prostsze, ale „rabat zastosowany” nie dotyczyłby żadnej transakcji i nie dałoby się go sprawdzić.
- **Punkty od kwoty brutto**: klient dostawałby punkty za pieniądze, których nie zapłacił.

## Konsekwencje

- Zakup bez kuponu: `Discount = 0`, `PaidAmount = GrossAmount` - punkty jak dotąd. Migracja uzupełniła tak istniejące zakupy.
- Precyzję stawki i limitu sprawdza aplikacja: `numeric(p,s)` w PostgreSQL zaokrągla nadmiarowe miejsca zamiast je odrzucać (test to dokumentuje).
- Zakup z rabatem i zapłatą poniżej 1 zł nie ma wpisu w historii punktów; zużycie kuponu musi być widoczne w „Moich kuponach”.
- Kupon zużyty równolegle na innej kasie odrzuca cały zakup (`409`); kasjer zapisuje go świadomie jeszcze raz, bez kuponu.
- Jeden klucz idempotencji obejmuje zakup z kuponem; odcisk zawiera kod kuponu.
