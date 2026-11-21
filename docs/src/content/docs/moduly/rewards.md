---
title: Rewards (oferty i kupony)
description: Oferty z rabatem na litr, kupony klientów, kod kuponu i obliczenie rabatu.
sidebar:
  order: 3
---

:::caution[W trakcie]
Zrealizowane: model danych, ograniczenia w bazie, migracja `*_RewardsModel`, reguły (kod kuponu, obliczenie rabatu, walidacja oferty) z testami. Planowane: endpointy (katalog, wymiana punktów, aktywacja, podgląd z limitem prób, odbiór razem z zakupem), panel administratora, „Moje kupony”, kupon w panelu kasjera.
:::

## Co robi

Klient wymienia punkty na kupon z rabatem na paliwo, aktywuje go w telefonie i pokazuje przy kasie razem z kartą. Kasa nalicza rabat na litr do zapisywanego zakupu. Decyzja: [ADR 0006](/decyzje/0006-model-korzysci-kuponu/).

## Gdzie jest kod

| Co | Plik |
| --- | --- |
| Encje | `apps/api/Modules/Rewards/Domain/RewardOffer.cs`, `Coupon.cs` |
| Kod kuponu | `Domain/CouponCode.cs` |
| Obliczenie rabatu | `Domain/CouponDiscount.cs` |
| Walidacja oferty | `Domain/RewardOfferRules.cs` |
| Kody błędów | `RewardsErrorCodes.cs` |
| Konfiguracja EF, `CHECK` | `Infrastructure/RewardsModelConfiguration.cs` |
| Punkty od kwoty zapłaconej | `Modules/Loyalty/Domain/PointsPolicy.cs` (`ForPaidAmount`) |

## Obliczenie rabatu

`rabat = round(stawka × min(litry, limit), 2, AwayFromZero)`, najwyżej kwota brutto; `do zapłaty = brutto - rabat`; punkty `floor(do zapłaty)`.

| Stawka | Limit | Litry | Brutto | Rabat | Do zapłaty | Punkty |
| --- | --- | --- | --- | --- | --- | --- |
| 0,20 | 50 l | 40,5 | 250,00 | 8,10 | 241,90 | 241 |
| 0,20 | 50 l | 60,123 | 400,00 | 10,00 (limit) | 390,00 | 390 |
| 0,15 | 50 l | 0,3 | 2,00 | 0,05 (0,045 w górę) | 1,95 | 1 |
| 10,00 | 100 l | 5 | 30,00 | 30,00 (do brutto) | 0,00 | 0 |

Zaokrąglenie połówek od zera, nie bankierskie (0,045 → 0,05, a nie 0,04).

## Dane

### `RewardOffers`

| Kolumna | Typ | Ograniczenia |
| --- | --- | --- |
| `Id` | `uuid` | PK |
| `Name` | `varchar(100)` | `CHECK (btrim(Name) <> '')` |
| `Description` | `varchar(500)` | |
| `PointsCost` | `integer` | `CHECK 1 - 1 000 000` |
| `DiscountPerLiter` | `numeric(4,2)` | `CHECK (> 0 AND <= 10)` |
| `MaxLiters` | `numeric(7,3)` | `CHECK (> 0 AND <= 1000)` |
| `IsActive` | `boolean` | wyłączona: brak nowych wymian, wydane kupony działają |
| `CreatedAt`, `UpdatedAt` | `timestamptz` | |
| `xmin` | | token współbieżności edycji |

Oferty nie usuwamy: kupony mają FK `RESTRICT`.

### `Coupons`

| Kolumna | Typ | Ograniczenia |
| --- | --- | --- |
| `Id` | `uuid` | PK |
| `UserId` | `text` | FK `AspNetUsers` (RESTRICT) |
| `OfferId` | `uuid` | FK `RewardOffers` (RESTRICT) |
| `OfferName`, `PointsCost`, `DiscountPerLiter`, `MaxLiters` | | snapshot z chwili wymiany, te same `CHECK` co oferta |
| `Status` | `varchar(16)` | `Purchased`, `Active`, `Redeemed` |
| `Code` | `char(27)` | `NULL` przed aktywacją; `CHECK (~ '^K[0-7][0-9A-HJKMNP-TV-Z]{25}$')`, `UNIQUE` |
| `PurchasedAt`, `ActivatedAt`, `RedeemedAt` | `timestamptz` | `CHECK` kolejności |
| `RedeemedPurchaseId` | `uuid` | FK `FuelPurchases` (RESTRICT), `UNIQUE` - jeden kupon na zakup |

`CK_Coupons_State` wiąże status z polami:

| Status | `Code` | `ActivatedAt` | `RedeemedAt`, `RedeemedPurchaseId` |
| --- | --- | --- | --- |
| `Purchased` | brak | brak | brak |
| `Active` | jest | jest | brak |
| `Redeemed` | jest | jest | są |

Precyzję stawki i limitu sprawdza aplikacja (`RewardOfferRules`): `numeric(4,2)` zapisałby 0,205 jako 0,21 bez błędu.

### Zmiany w Loyalty

- `FuelPurchases`: `Amount` → `GrossAmount`; nowe `Discount` (`CHECK 0 ≤ Discount ≤ GrossAmount`) i `PaidAmount` (`CHECK PaidAmount = GrossAmount - Discount`). Punkty `floor(PaidAmount)`.
- `PointsLedgerEntries.Type`: także `CouponExchange` (wymiana punktów na kupon); znak `Delta` zależny od typu: `FuelPurchase` > 0, `CouponExchange` < 0.

## Zabezpieczenia

- Rabat liczy serwer z kopii parametrów w kuponie; klient nie wysyła kwoty rabatu.
- Kody kuponów nie trafiają do logów; w żądaniach tylko w treści (POST), nie w URL.

:::caution[Planowane]
Sprawdzanie właściciela (kupon = właściciel karty), warunkowa zmiana statusu przy odbiorze, idempotencja odbioru razem z zakupem i limit prób podglądu powstaną z endpointami.
:::

## Testy

| Plik | Sprawdza |
| --- | --- |
| `RewardsRulesTests.cs` - `CouponDiscountTests` | przykłady z tabeli, limit litrów, zaokrąglenie połówek (0,045 → 0,05; 0,005 → 0,01), ograniczenie do brutto i zapłata 0 → 0 pkt, punkty od kwoty zapłaconej, odrzucenie niezwalidowanych danych |
| `RewardsRulesTests.cs` - `CouponCodeTests` | 27 znaków z prefiksem `K`, część losowa to pełny kod w formacie karty, unikalność; kod karty ≠ kod kuponu (w obie strony); normalizacja wpisanych wariantów |
| `RewardsRulesTests.cs` - `RewardOfferRulesTests` | wymagana nazwa, długości, zakres kosztu, stawki i limitu; precyzja (0,205 zł/l, 50,0005 l) odrzucona |
| `RewardsModelConstraintsTests.cs` | wszystkie poprawne stany kuponu; `CHECK` zakresów oferty i snapshotu; pusta nazwa; **baza zaokrągla 0,205 → 0,21** (dlatego walidacja w aplikacji); 7 niespójnych stanów kuponu; aktywacja przed zakupem; zły format kodu; duplikat kodu; jeden kupon na zakup; oferty z kuponami nie da się usunąć; równoległa edycja oferty wykryta (`xmin`) |
| `LoyaltyModelConstraintsTests.cs` | rabat równy brutto (zapłata 0, 0 pkt) przyjęty; rabat poza `0 - brutto` i `PaidAmount ≠ GrossAmount - Discount` odrzucone; znak `Delta` zgodny z typem |

Próby negatywne: zaokrąglenie bankierskie (`ToEven`) i brak ograniczenia do brutto wywracają testy obliczeń.
