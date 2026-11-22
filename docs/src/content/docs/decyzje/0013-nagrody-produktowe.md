---
title: "0013 - Nagrody produktowe"
description: Drugi rodzaj korzyści oferty - produkt (kawa, hot dog, myjnia) - z tą samą wymianą i aktywacją oraz odbiorem w kasie bez tankowania.
sidebar:
  order: 13
---

## Status

Proponowana (2026-11-22). Nie zrealizowana - model i kontrakt do akceptacji przed implementacją.

## Problem

Oferta zna jeden rodzaj korzyści: rabat na litr realizowany razem z zakupem paliwa ([ADR 0006](/decyzje/0006-model-korzysci-kuponu/)). Klient chce też wymieniać punkty na produkty: kawę, hot doga, myjnię.

Produkt nie ma litrów ani kwoty do obniżenia, a jego wydanie nie jest tankowaniem. Odbiór kuponu jest dziś związany z zakupem paliwa tego samego klienta - nagroda tego nie spełni.

Drugi, równoległy system (osobne nagrody, wymiany, kody) powieliłby wymianę, idempotencję, aktywację i kontrolę właściciela.

## Rozwiązanie

### Rodzaj korzyści

- `RewardOffer.Kind`: `FuelDiscount` (dotychczasowe oferty) albo `Product`.
  - `FuelDiscount`: stawka i limit litrów wymagane, jak dziś.
  - `Product`: stawka i limit puste.
  - CHECK w bazie wiąże rodzaj z polami. Migracja ustawia istniejącym ofertom `FuelDiscount`.
- Rodzaj wybiera admin przy tworzeniu. **Edycja nie zmienia rodzaju** (`400 rewards.kind_immutable`): kupony mają snapshot, a oferta zmieniająca rodzaj myliłaby historię.
- Kupon kopiuje rodzaj do snapshotu, tak jak nazwę, koszt, stawkę i limit.

### Wymiana i aktywacja - bez zmian

- Wymiana: ta sama transakcja (saldo, ledger, kupon), `Idempotency-Key`, równoległe wydawanie bez ujemnego salda.
- Aktywacja: ten sam endpoint i kod `K…` z QR. Aktywacja niczego nie pobiera ponownie.
- Stany: `Purchased → Active → Redeemed`.

### Odbiór nagrody w kasie

- `POST /api/cashier/reward-handovers` (rola `Cashier`, CSRF, `Idempotency-Key`). Treść: `{ cardCode, couponCode }` w body, nigdy w URL ani logach.
- Jedna transakcja:
  1. karta → właściciel; kupon tego właściciela;
  2. warunkowy `UPDATE coupons SET Status = Redeemed ... WHERE Id = @id AND Status = 'Active' AND Kind = 'Product'`;
  3. wiersz `reward_handovers` (kupon `UNIQUE`, kasjer, stacja, kasa, czas).
- Bez zakupu paliwa i bez punktów.
- Błędy:
  - cudzy albo nieistniejący kod → ten sam błąd co dziś przy kuponie (bez ujawniania, czy istnieje);
  - kupon nieaktywny lub odebrany → `409`, jak przy rabacie;
  - kupon rabatowy w tym endpoincie albo nagroda w zakupie paliwa → `409 rewards.coupon_wrong_kind`.
- Ponowienie z tym samym kluczem odtwarza wynik. Inny klucz po odbiorze → `409` (warunkowy UPDATE pilnuje jednorazowości).
- CHECK kuponu: `Redeemed` ⇔ dokładnie jedno z dwojga - zakup paliwa (`FuelDiscount`) albo wydanie (`Product`).
- Podgląd w kasie (karta + kupon) pokazuje rodzaj: przy nagrodzie przycisk „Wydaj nagrodę” zamiast rejestracji tankowania.

### Interfejs

- „Oferty”: dwie sekcje - „Rabaty na paliwo” i „Nagrody” - z kosztem i brakującymi punktami.
- „Moje kupony”: rodzaj widoczny; kod i QR jak dotąd.
- Admin: wybór rodzaju przy tworzeniu; pola stawki i limitu tylko dla rabatu.
- Konta demo: trzy nagrody - kawa 150 pkt, hot dog 250 pkt, myjnia 600 pkt. Bez magazynu i terminów ważności.

### Czat

- `list_available_rewards` zwraca rodzaj (`kind`). Stawka i limit tylko przy rabacie.
- `compare_coupons` porównuje tylko rabaty na paliwo. Nagrody nie mają kwoty w zł, więc model nie przelicza ich na złotówki.
- Czat nie wymienia punktów. Kieruje do „Ofert”, a wymianę z potwierdzeniem odkładamy.
- Gemini dostaje nazwy i koszty nagród - dane katalogu, nie klienta.

## Alternatywy

- **Osobny system nagród** (tabele, wymiana, kody): dwa razy ta sama idempotencja, aktywacja i kontrola właściciela. Odrzucone.
- **Nagroda jako „zakup” za 0 zł**: fałszywe tankowanie w historii i paragon bez sensu. Odrzucone.
- **Rodzaj tylko w kuponie, nie w ofercie**: admin nie mógłby pokazać i filtrować katalogu. Odrzucone.

## Konsekwencje

- ADR 0006 zostaje dla rabatów; jego alternatywa „darmowy produkt” jest zrealizowana tutaj osobną ścieżką odbioru.
- Migracja: nowa kolumna z wartością domyślną, nullowalne stawka i limit, nowe CHECK-i, tabela `reward_handovers`.
- `npm run api:types` - nowe pola i endpoint.
- Testy:
  - API: CHECK-i rodzaju, edycja nie zmienia rodzaju, wymiana i aktywacja nagrody, wydanie jednorazowe przy różnych kluczach i równolegle, cudza karta, zły rodzaj w obu kierunkach, brak punktów i zakupu przy wydaniu, kasjer bez roli → `403`;
  - E2E: klient wymienia i aktywuje kawę, kasjer skanuje kartę i kupon, wydaje nagrodę; kupon „wykorzystany”.

## Otwarte pytania

1. Czy wydanie nagrody ma wymagać zeskanowania karty, czy wystarczy kod kuponu? Propozycja: karta + kupon, jak przy rabacie - spójny proces i kontrola właściciela.
2. Czy nagrody mają osobną nazwę w UI („bon”), czy zostają „kuponami”? Propozycja: „kupon na nagrodę”.
