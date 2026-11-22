---
title: Rewards (oferty i kupony)
description: Oferty z rabatem na litr, kupony klientów, kod kuponu i obliczenie rabatu.
sidebar:
  order: 3
---

:::caution[W trakcie]
Zrealizowane: model danych, ograniczenia w bazie, reguły (kod kuponu, obliczenie rabatu, walidacja oferty), API ofert (katalog, zarządzanie przez Admin z wersją), wymiana punktów na kupon, odczyt własnych kuponów, aktywacja kuponu. Planowane: podgląd z limitem prób i odbiór razem z zakupem, interfejs (katalog, panel administratora, kupon w kasie).
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
| Katalog (klient) | `Catalog/CatalogEndpoint.cs` |
| Zarządzanie ofertami (Admin) | `Admin/AdminRewardEndpoints.cs` |
| Wymiana punktów | `Exchange/ExchangeEndpoint.cs` |
| Moje kupony (odczyt) | `Coupons/MyCouponsEndpoints.cs` |
| Aktywacja kuponu | `Coupons/ActivateEndpoint.cs` |
| `Cache-Control: no-store` | `apps/api/Infrastructure/NoStore.cs` |
| Pobranie punktów (Loyalty) | `Modules/Loyalty/Points/PointsSpending.cs` |
| Mapowanie, błędy modułu | `RewardsModule.cs`, `RewardsProblems.cs` |
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
| `Version` | `integer` | `CHECK (>= 1)`; wersja edycji, start od 1 |

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

## Oferty - API

| Endpoint | Rola | Działanie |
| --- | --- | --- |
| `GET /api/rewards` | `User` | aktywne oferty; kolejność `PointsCost`, `Name`, `Id` (jednoznaczna także przy tym samym koszcie i nazwie); bez stronicowania - katalog jest mały |
| `GET /api/admin/rewards` | `Admin` | wszystkie oferty, z `isActive` i `version` |
| `GET /api/admin/rewards/{id}` | `Admin` | jedna oferta do formularza edycji |
| `POST /api/admin/rewards` | `Admin`, `Idempotency-Key` | tworzy ofertę z `version = 1`; ponowienie z tym samym kluczem zwraca tę samą ofertę zamiast duplikatu |
| `PUT /api/admin/rewards/{id}` | `Admin` | cały formularz (także `isActive`) + `version` |

Kasjer nie ma dostępu do ofert. Brak `DELETE` - oferta jest tylko wyłączana. Wszystkie pola liczbowe i `isActive` są wymagane w `POST` i `PUT`; brak pola to `rewards.required_field` (brak `isActive` nie może wyłączyć oferty, brak liczby nie jest raportowany jako „poza zakresem”).

### Konflikt edycji

Formularz wysyła `version`, z którą oferta została wczytana. Zapis to jedno polecenie:

```sql
UPDATE "RewardOffers"
SET ..., "Version" = "Version" + 1, "UpdatedAt" = now()
WHERE "Id" = @id AND "Version" = @version
RETURNING ...
```

- 1 wiersz → `200` z zapisanym wierszem z `RETURNING` (bez osobnego odczytu, który mógłby trafić na kolejną edycję).
- 0 wierszy, oferta istnieje → `409 rewards.offer_version_conflict`, nic nie zapisano. 0 wierszy, brak oferty → `404`.
- Walidacja (`400`) idzie przed porównaniem wersji: zły formularz z nieaktualną wersją dostaje `400`, nie `409`. `version` wymagane i `>= 1`.
- Każdy inny zapis oferty przez EF też podnosi wersję: `AppDbContext.SaveChanges` zwiększa `Version` zmienionych ofert, a EF porównuje wersję oryginalną (token współbieżności). Formularz wczytany przed taką zmianą dostanie `409`; `UpdatedAt` też się zmienia. `ExecuteUpdate` omija `SaveChanges` - taki zapis musi sam ustawić `"Version" = "Version" + 1`.
- Jawna kolumna zamiast `xmin`: `xmin` nie przetrwa dump/restore ani replikacji logicznej, a samo `xmin` w EF nie wykryje zapisu ze starego formularza (formularz musi przekazać wersję).

`PUT` nie ma idempotencji. Ponowienie po utracie odpowiedzi dostaje `409`, bo pierwsza próba już podniosła wersję - **to nie dowodzi, że ofertę zmienił ktoś inny**.

:::caution[Planowane - interfejs]
Po utracie odpowiedzi `PUT` UI pokazuje „wynik zapisu nieznany” (nie „zapis nie powiódł się”) i proponuje odświeżenie oferty. Przy `409` pokazuje aktualny stan, nie nadpisując wpisanych zmian automatycznie.
:::

## Wymiana punktów

`POST /api/rewards/{id}/exchange` (rola `User`, CSRF, `Idempotency-Key`), treść `{ version }` - wersja oferty z katalogu, który klient widział. Odpowiedź `201 { coupon, balance }`, `Location: /api/me/coupons/{id}`. Kupon ma status `Purchased` i nie ma jeszcze kodu.

Kolejność w transakcji idempotencji:

1. `version` wymagane i `>= 1` → inaczej `400 rewards.validation_failed`.
2. Oferta `SELECT ... FOR SHARE`: edycja admina czeka, aż wymiana się zatwierdzi, więc kupon odpowiada ofercie w chwili zatwierdzenia. Wymiana czeka na trwającą edycję najwyżej `Rewards:OfferLockTimeout` (`lock_timeout`), potem `503 rewards.offer_busy` z `Retry-After`.
3. Brak oferty → `404 rewards.offer_not_found`; wyłączona → `409 rewards.offer_inactive`; inna wersja → `409 rewards.offer_changed`. Wyłączenie jest sprawdzane przed wersją, bo wyłączenie też podnosi wersję - klient ma dostać „niedostępna”, nie „zmieniona”.
4. `PointsSpending.SpendAsync` (Loyalty): blokada klienta (`503 loyalty.customer_busy`), potem `UPDATE "LoyaltyAccounts" SET "Balance" = "Balance" - koszt WHERE "Balance" >= koszt RETURNING`. 0 wierszy (także brak konta) → `409 rewards.insufficient_points`. Wpis historii `CouponExchange` z `Delta = -koszt`, `SourceId` = id kuponu.
5. Kupon z kopią nazwy, kosztu, stawki i limitu litrów oferty.

Wszystko w jednej transakcji: nieudana próba nie zostawia ani obciążenia, ani kuponu (idempotencja cofa do savepointu przy odpowiedzi innej niż 2xx). Rewards nie pisze do tabel Loyalty - salda i historii dotyka tylko `PointsSpending`.

- Wersja zamiast samego kosztu: chroni też stawkę i limit litrów. Każda edycja oferty (także opisu) wymaga ponownego potwierdzenia przez klienta.
- `409 rewards.insufficient_points` jest zapisywany pod kluczem: ponowienie z tym samym kluczem zwraca go, nawet gdy klient zdobył już punkty. Nowa próba = nowy klucz (frontend zwalnia klucz po 4xx).
- Brak limitu kuponów na klienta - ogranicza je saldo.

## Moje kupony

`GET /api/me/coupons` (wszystkie własne, od najnowszych, bez stronicowania) i `GET /api/me/coupons/{id}` (rola `User`). Cudzy albo nieistniejący kupon → `404 rewards.coupon_not_found` - odpowiedź nie zdradza, że kupon istnieje. `code` jest `null` przed aktywacją, potem zawiera kod - właściciel musi go móc pokazać przy kasie także po odświeżeniu strony.

## Aktywacja kuponu

`POST /api/me/coupons/{id}/activate` (rola `User`, CSRF, `Idempotency-Key`, bez treści) → `200` z kuponem (`Active`, `code`, `activatedAt`). Punkty się nie zmieniają - zapłacone przy wymianie.

1. Jedno warunkowe `UPDATE ... SET "Status" = 'Active', "Code" = nowy, "ActivatedAt" = clock_timestamp() WHERE "Id" = id AND "UserId" = właściciel AND "Status" = 'Purchased' RETURNING *` - odpowiedź zawiera zapisane wartości.
2. Gdy nic nie zmieniło, decyduje bieżący stan: brak albo cudzy → `404 rewards.coupon_not_found`; aktywny → `200` z tym samym kodem (także z nowym kluczem); odebrany → `409 rewards.coupon_redeemed`.

- Warunek na status dopuszcza tylko przejście `Purchased → Active`: odebrany kupon nigdy nie wraca do `Active`, a z równoległych aktywacji tylko jedna zmienia wiersz (reszta czeka na jego blokadę i widzi już `Active`). Odbiór (planowany) zmieni tylko kupon `Active`. Blokada klienta nie jest potrzebna - aktywacja zmienia tylko wiersz kuponu.
- `clock_timestamp()`, nie `now()`: `now()` to początek transakcji idempotencji, nie chwila aktywacji.
- Kod to parametr zapytania, więc logowany SQL go nie zawiera. Kolizja kodu (128 losowych bitów) kończy się `500` bez zapisu; idempotencja nie zapisuje 5xx, więc ten sam klucz można ponowić.
- Ponowienie tym samym kluczem odtwarza zapisaną odpowiedź (`200`, `Active`) także wtedy, gdy kupon został już odebrany. Bieżący stan pokazuje `GET`.
- Oferta wyłączona po wymianie nie blokuje aktywacji (kupon ma własną kopię warunków). Kupon nie ma terminu ważności.

:::caution[Planowane - interfejs]
Ekran „Moje kupony”: po aktywacji frontend odświeża kupon przez `GET`, jak przy karcie.
:::

## Zabezpieczenia

- Rabat liczy serwer z kopii parametrów w kuponie; klient nie wysyła kwoty rabatu.
- Kody kuponów nie trafiają do logów; w żądaniach tylko w treści (POST), nie w URL. Kod jest też w treści zapisanej odpowiedzi aktywacji w `IdempotencyRecords`; MVP nie usuwa tych wierszy (retencję trzeba ustalić przed produkcją, `PLAN-MVP.md`), więc dostęp do bazy i kopii zapasowych daje dostęp do kodów.
- Odpowiedzi grupy `/api/me/coupons` po sprawdzeniu dostępu i CSRF (także błędy domenowe i odtworzone odpowiedzi) mają `Cache-Control: no-store`; `401`/`403` go nie mają, ale nie zawierają kodu: przeglądarka i pośrednicy nie zapisują odpowiedzi z kodem. Filtr jest na grupie, czyli na zewnątrz filtra idempotencji - odtworzenie omija endpoint i jego własne filtry. Nagłówek nie dotyczy pamięci TanStack Query w karcie przeglądarki.
- Aktywacja tylko własnego kuponu: `UserId` jest w warunku `UPDATE`.

:::caution[Planowane]
Sprawdzanie przy odbiorze (kupon = właściciel karty), warunkowa zmiana statusu, idempotencja odbioru razem z zakupem i limit prób podglądu powstaną z endpointami.
:::

## Testy

| Plik | Sprawdza |
| --- | --- |
| `RewardsRulesTests.cs` - `CouponDiscountTests` | przykłady z tabeli, limit litrów, zaokrąglenie połówek (0,045 → 0,05; 0,005 → 0,01), ograniczenie do brutto i zapłata 0 → 0 pkt, punkty od kwoty zapłaconej, odrzucenie niezwalidowanych danych |
| `RewardsRulesTests.cs` - `CouponCodeTests` | 27 znaków z prefiksem `K`, część losowa to pełny kod w formacie karty, unikalność; kod karty ≠ kod kuponu (w obie strony); normalizacja wpisanych wariantów |
| `RewardsRulesTests.cs` - `RewardOfferRulesTests` | wymagana nazwa, długości, zakres kosztu, stawki i limitu; precyzja (0,205 zł/l, 50,0005 l) odrzucona |
| `RewardsModelConstraintsTests.cs` | wszystkie poprawne stany kuponu; `CHECK` zakresów oferty i snapshotu; pusta nazwa; **baza zaokrągla 0,205 → 0,21** (dlatego walidacja w aplikacji); 7 niespójnych stanów kuponu; aktywacja przed zakupem; zły format kodu; duplikat kodu; jeden kupon na zakup; oferty z kuponami nie da się usunąć; `Version < 1` odrzucone; równoległe zapisy przez EF wykryte, każdy podnosi wersję |
| `RewardExchangeTests.cs` | wymiana: `201`, kupon = migawka oferty, bez kodu, `Location`, saldo, odczyt kuponu i listy, wpis `CouponExchange` (`-koszt`, `balanceAfter`, `couponExchange`); całe saldo do wydania; brak punktów (499 i brak konta) → `409` bez zmian; kolejność: brak → `404`, wyłączona (z podniesioną wersją) → `offer_inactive`, zmieniona → `offer_changed`, bez obciążenia; `version` wymagane i `>= 1`; ten sam klucz → ten sam kupon, jedno obciążenie, także równolegle; **6 równoległych wymian przy saldzie na 2 → dokładnie 2 kupony, saldo 0, `balanceAfter` 500, 0**; **edycja admina czeka na trwającą wymianę (bramka), kupon ma warunki, które klient widział**, potem stara wersja → `offer_changed`; klient zajęty → `503` z `Retry-After`, bez zmian, ten sam klucz działa później; oferta zablokowana dłużej niż limit → `503 rewards.offer_busy`, bez zmian, ten sam klucz działa później; zakres `OfferLockTimeout`; cudzy kupon → `404`, lista tylko własnych; `401`, `403` kasjer, CSRF, brak klucza → `400` |
| `CouponActivationTests.cs` | aktywacja: `code` `null` przed, potem poprawny format, zapisane wartości = odpowiedź, kod w obu `GET`, saldo i historia bez zmian; `activatedAt` późniejsze niż początek transakcji (rekord idempotencji); ponowna aktywacja nowym kluczem → ten sam kod; ponowienie tym samym kluczem → odtworzenie z tym samym kodem; 6 równoległych aktywacji → jeden kod; równolegle jednym kluczem → jeden kod; odebrany → `409`, status i kod bez zmian; odtworzenie po odbiorze zwraca zapisaną odpowiedź, `GET` pokazuje `Redeemed`; kupon wyłączonej oferty da się aktywować; cudzy i nieistniejący → `404`, kupon nietknięty; `no-store` na aktywacji, odtworzeniu, `404` i obu `GET` (kontrola: `/api/me/wallet` bez nagłówka); kod nie trafia do logów (Trace, także SQL); `401`, `403` kasjer, CSRF, brak klucza |
| `RewardOfferTests.cs` | katalog: tylko aktywne, kolejność koszt → nazwa → `id` (także dla identycznego kosztu i nazwy), pola; dostęp: katalog tylko `User` (`401` anonim, `403` kasjer i sam Admin), część admina tylko `Admin`; tworzenie: `201` + `Location`, `version 1`, odczyt pojedynczy i na liście; idempotencja `POST` (jedna oferta), brak klucza → `400`; walidacja pól (w tym 0,205 zł/l i brak `isActive`) bez zapisu wiersza; brak liczb → `required_field` w `POST` i `PUT`; liczby jako tekst → `400`; zmiana przez EF podnosi wersję, więc starszy formularz dostaje `409`; `PUT` zastępuje formularz i podnosi wersję, odpowiedź = zapisany wiersz; **nieaktualny formularz → `409` bez nadpisania**; **8 równoległych `PUT` z tą samą wersją → dokładnie jeden `200`, 7 × `409`, wersja 2**; `version` wymagane i `>= 1`, walidacja przed konfliktem; nieznane `id` → `404`; wyłączenie ukrywa w katalogu, włączenie przywraca; edycja oferty nie zmienia wydanego kuponu; CSRF |
| `LoyaltyModelConstraintsTests.cs` | rabat równy brutto (zapłata 0, 0 pkt) przyjęty; rabat poza `0 - brutto` i `PaidAmount ≠ GrossAmount - Discount` odrzucone; znak `Delta` zgodny z typem |

Próby negatywne: zaokrąglenie bankierskie (`ToEven`) i brak ograniczenia do brutto wywracają testy obliczeń; `UPDATE` bez warunku na wersję wywraca testy nieaktualnego formularza i równoległej edycji; brak podnoszenia wersji w `SaveChanges` wywraca oba testy zapisu przez EF; wymiana bez warunku `Balance >= koszt`, bez `FOR SHARE` albo ze sprawdzeniem wersji przed wyłączeniem wywraca odpowiednie testy wymiany. Aktywacja bez warunku na status wywraca testy odebranego kuponu, ponownej aktywacji i równoległych aktywacji; `now()` zamiast `clock_timestamp()` wywraca test czasu; kod wklejony w SQL wywraca test logów; brak `no-store` albo filtr tylko na endpoincie (wewnątrz idempotencji) wywraca testy nagłówka.

Testy nagłówka używają hosta z domyślnym interwałem sprawdzania sesji (`ApiFactory.WithoutPerRequestSessionRenewal`). Główny host testowy sprawdza sesję przy każdym żądaniu, więc odnawia ciasteczko, a handler ciasteczek dodaje wtedy `no-store` do każdej odpowiedzi i ukryłby brak nagłówka.
