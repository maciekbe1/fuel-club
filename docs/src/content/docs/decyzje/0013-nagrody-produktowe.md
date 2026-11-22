---
title: "0013 - Nagrody produktowe"
description: Drugi rodzaj korzyści oferty - produkt (kawa, hot dog, myjnia) - z tą samą wymianą i aktywacją oraz odbiorem w kasie bez tankowania.
sidebar:
  order: 13
---

## Status

Decyzja przyjęta i zrealizowana (2026-11-22), P13. Opis działania: [Rewards](/moduly/rewards/#nagrody-produktowe).

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
- Kupon kopiuje rodzaj do snapshotu, tak jak nazwę, koszt, stawkę i limit. Kupon produktowy zachowuje snapshot nazwy i kosztu: edycja oferty nie zmienia wydanych nagród.

### Wymiana i aktywacja - bez zmian

- Wymiana: ta sama transakcja (saldo, ledger, kupon), `Idempotency-Key`, równoległe wydawanie bez ujemnego salda.
- Aktywacja: ten sam endpoint i kod `K…` z QR. Aktywacja niczego nie pobiera ponownie.
- Stany: `Purchased → Active → Redeemed`.

### Odbiór nagrody w kasie

- `POST /api/cashier/reward-handovers` (rola `Cashier`, CSRF, `Idempotency-Key`). Treść: `{ cardCode, couponCode }` w body, nigdy w URL ani logach.
- Jedna transakcja:
  1. karta → właściciel; kupon tego właściciela. Nieznany i cudzy kod dają **ten sam `404`**; rodzaj kuponu sprawdzany dopiero po właścicielu, więc błąd rodzaju nie ujawnia cudzego kuponu;
  2. wiersz `RewardHandovers` (kupon `UNIQUE`, kasjer, stacja, kasa, czas) - ślad wydania mimo braku tankowania;
  3. warunkowy `UPDATE "Coupons" SET "Status" = 'Redeemed', "RedeemedHandoverId" = ... WHERE ... "Status" = 'Active' AND "Kind" = 'Product'`; 0 wierszy wycofuje transakcję.
- Bez zakupu paliwa i bez punktów.
- Sama karta nie jest dodatkowym uwierzytelnieniem: zachowuje spójny proces z rabatem i pozwala backendowi sprawdzić właściciela.
- Błędy:
  - cudzy albo nieistniejący kod → `404`, ten sam co przy rabacie;
  - kupon odebrany → `409 rewards.coupon_redeemed` z `handedOverAt`, `stationCode` i `registerCode` wydania (decyzja właściciela po review - czas i stanowisko, kasa dodana w kolejnym review, bo na stanowisku bywa kilka kas; kasa rozpoznaje własną zgubioną próbę bez pamięci po stronie klienta); nieaktywowany nie ma kodu, więc → `404`, jak przy rabacie;
  - kupon rabatowy w tym endpoincie albo nagroda w zakupie paliwa → `409 rewards.coupon_wrong_kind`.
- Ponowienie z tym samym kluczem odtwarza wynik. Inny klucz po odbiorze → `409` (warunkowy UPDATE pilnuje jednorazowości).
- Ograniczenia bazy rozróżniają oba odbiory:
  - `FuelDiscount` + `Redeemed` ⇒ `RedeemedPurchaseId` ustawiony, bez wydania;
  - `Product` + `Redeemed` ⇒ wiersz `RewardHandovers`, `RedeemedPurchaseId` pusty;
  - zakup paliwa nie może wskazać kuponu produktowego (złożony FK lub CHECK z rodzajem).
- Podgląd w kasie (karta + kupon) pokazuje rodzaj: przy nagrodzie przycisk „Wydaj nagrodę” zamiast rejestracji tankowania.

### Interfejs

Nazewnictwo: „Nagroda” (bez „bonu”), rabat dalej „kupon”.

- „Oferty”: dwie sekcje - „Rabaty na paliwo” i „Nagrody” - z kosztem i brakującymi punktami.
- „Moje kupony i nagrody”: nagroda pokazuje nazwę (np. „Kawa”), koszt, status i przycisk „Aktywuj odbiór”; kod i QR jak dotąd.
- Kasa: „Wydaj nagrodę”.
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
- Migracja: nowa kolumna z wartością domyślną, nullowalne stawka i limit, nowe CHECK-i, tabela `RewardHandovers`.
- `npm run api:types` - nowe pola i endpoint.
- Testy:
  - API: CHECK-i rodzaju, edycja nie zmienia rodzaju, wymiana i aktywacja nagrody, wydanie jednorazowe przy różnych kluczach i równolegle, cudza karta, zły rodzaj w obu kierunkach, brak punktów i zakupu przy wydaniu, kasjer bez roli → `403`;
  - E2E: klient wymienia i aktywuje kawę, kasjer skanuje kartę i kupon, wydaje nagrodę; kupon „wykorzystany”.
