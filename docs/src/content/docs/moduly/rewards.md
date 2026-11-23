---
title: Rewards (oferty i kupony)
description: Oferty z rabatem na litr i nagrody produktowe, kupony klientów, kod kuponu, obliczenie rabatu i wydanie nagrody w kasie.
sidebar:
  order: 3
---

:::caution[W trakcie]
Zrealizowane: model danych, ograniczenia w bazie, reguły (kod kuponu, obliczenie rabatu, walidacja oferty), API ofert (katalog, zarządzanie przez Admin z wersją), wymiana punktów na kupon, odczyt własnych kuponów, aktywacja kuponu, podgląd kuponu dla kasjera z limitem prób, odbiór razem z zakupem, interfejs katalogu z wymianą, ekran „Moje kupony” z aktywacją i kodem QR, kupon w panelu kasjera z podglądem, rabat w historii, panel administratora ofert, nagrody produktowe z wydaniem w kasie ([ADR 0013](/decyzje/0013-nagrody-produktowe/)).
:::

## Co robi

Klient wymienia punkty na kupon z rabatem na paliwo, aktywuje go w telefonie i pokazuje przy kasie razem z kartą. Kasa nalicza rabat na litr do zapisywanego zakupu. Decyzja: [ADR 0006](/decyzje/0006-model-korzysci-kuponu/).

Drugi rodzaj oferty to nagroda (kawa, hot dog, myjnia): ta sama wymiana i aktywacja, ale kasjer wydaje ją bez tankowania i bez punktów - [niżej](#nagrody-produktowe).

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
| Aktywacja kuponu | `Coupons/ActivateEndpoint.cs` (HTTP) → `Coupons/CouponActivation.cs` (wspólna z potwierdzeniem czatu) |
| Podgląd kuponu (kasjer) | `Cashier/CouponPreviewEndpoint.cs` |
| Wydanie nagrody | `Cashier/RewardHandoverEndpoint.cs` |
| Odbiór z zakupem | `Cashier/CouponRedemption.cs` (implementuje `Modules/Loyalty/Cashier/ICouponRedemption.cs`), wywołanie w `Modules/Loyalty/Cashier/FuelPurchaseEndpoint.cs` |
| Limit prób (429) | `apps/api/Infrastructure/RateLimiting.cs`, polityka w `RewardsModule.cs` |
| `Cache-Control: no-store` | `apps/api/Infrastructure/NoStore.cs` |
| Frontend: katalog i wymiana | `apps/web/features/rewards/` (`services.ts` → `hooks.ts` → `components/RewardsCatalog.tsx`), strona `app/(app)/oferty/page.tsx` (z `BalanceCard` z `features/wallet`) |
| Frontend: moje kupony | `apps/web/features/coupons/` (`services.ts` → `hooks.ts` → `components/CouponsList.tsx`), strona `app/(app)/kupony/page.tsx`; QR i kopiowanie wspólne z kartą: `components/code/` |
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
| `Kind` | `varchar(16)` | `FuelDiscount` albo `Product` (`CK_*_Kind`); niezmienny po utworzeniu |
| `DiscountPerLiter` | `numeric(4,2)` NULL | `CHECK (> 0 AND <= 10)`; `CK_*_KindTerms`: wymagane przy `FuelDiscount`, puste przy `Product` |
| `MaxLiters` | `numeric(7,3)` NULL | `CHECK (> 0 AND <= 1000)`; jak wyżej |
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
| `OfferName`, `PointsCost`, `Kind`, `DiscountPerLiter`, `MaxLiters` | | snapshot z chwili wymiany, te same `CHECK` co oferta; FK `(OfferId, Kind)` → `RewardOffers (Id, Kind)`: rodzaj kuponu = rodzaj oferty |
| `Status` | `varchar(16)` | `Purchased`, `Active`, `Redeemed` |
| `Code` | `char(27)` | `NULL` przed aktywacją; `CHECK (~ '^K[0-7][0-9A-HJKMNP-TV-Z]{25}$')`, `UNIQUE` |
| `PurchasedAt`, `ActivatedAt`, `RedeemedAt` | `timestamptz` | `CHECK` kolejności |
| `RedeemedPurchaseId` | `uuid` | złożony FK `(RedeemedPurchaseId, UserId)` → `FuelPurchases (Id, UserId)` (RESTRICT): kupon tylko na zakup własnego właściciela; `UNIQUE` - jeden kupon na zakup |

| `RedeemedHandoverId` | `uuid` | wydanie nagrody; złożony FK `(RedeemedHandoverId, Id, UserId)` → `RewardHandovers (Id, CouponId, UserId)`: wydanie tego kuponu i tego właściciela; `UNIQUE` |

`CK_Coupons_State` wiąże status z polami:

| Status | `Code` | `ActivatedAt` | `RedeemedAt` | `RedeemedPurchaseId` | `RedeemedHandoverId` |
| --- | --- | --- | --- | --- | --- |
| `Purchased` | brak | brak | brak | brak | brak |
| `Active` | jest | jest | brak | brak | brak |
| `Redeemed`, `FuelDiscount` | jest | jest | jest | jest | brak |
| `Redeemed`, `Product` | jest | jest | jest | brak | jest |

Nagroda nie może więc trafić na zakup paliwa, a rabat - na wydanie.

### `RewardHandovers`

Ślad wydania nagrody: `Id`, `CouponId` (`UNIQUE`), `UserId` (FK), `CashierUserId` (FK), `StationCode`, `RegisterCode` (format jak w `CashierStations`), `HandedOverAt` (`clock_timestamp()`). Brak FK z wydania do kuponu - kupon wskazuje wydanie, więc usuwanie nie ma cyklu.

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

### Interfejs - administracja ofert

`apps/web/features/admin-rewards/` (`services.ts` → `hooks.ts` → `schemas.ts`, `components/`), strony `app/(app)/admin/oferty/` (lista), `nowa/` (tworzenie), `[id]/` (edycja). Skrót „Administracja ofert” na ekranie głównym tylko dla roli `Admin`.

- **Strażnik** `app/(app)/admin/layout.tsx` (RSC): konto bez roli `Admin` → przekierowanie na `/`, brak sesji → `/login`. To wygoda - o dostępie decyduje API (`403` dla klienta i kasjera).
- Formularz (RHF + zod) z tymi samymi granicami co `RewardOfferRules` (długości, zakresy, precyzja 2/3 miejsc); błędy pól z API (`errors[field]`) trafiają pod pola. Wyłączenie i włączenie to pole „Aktywna” w edycji (cały formularz, `PUT`).
- **Tworzenie**: `useIdempotentMutation` z `persist` - ten sam formularz → ten sam klucz, trzymany w `sessionStorage` do ostatecznej odpowiedzi. Nieznany wynik blokuje formularz i pozwala tylko „Ponów utworzenie” tym samym kluczem (zmiana pól oznaczałaby nowy klucz i możliwy duplikat). Po wyjściu z ekranu albo przeładowaniu „Nowa oferta” pokazuje niedokończone tworzenie (nazwa i warunki) z tym samym ponowieniem i zablokowanym formularzem. Gdy i to ponowienie zgubi odpowiedź, kolejne „Ponów utworzenie” wysyła zachowaną ofertę, nie pusty formularz.
- **Edycja**: formularz pamięta wersję, z którą go wczytano (`base`), i wysyła ją w `PUT`. Brak automatycznych ponowień.
  - `409` → panel konfliktu: aktualna wersja z `GET` obok zachowanych zmian (formularz zablokowany). „Wczytaj aktualną wersję” zastępuje formularz; „Zapisz moje zmiany na wersji N” to świadome nadpisanie.
  - Nieznany wynik (brak odpowiedzi, `5xx`) → „Wynik zapisu nieznany”, formularz zablokowany, „Sprawdź aktualny stan”: wersja `base + 1` z wysłanymi wartościami → „Zapisano”; wersja `base` → „nie została zapisana”, można zapisać ponownie; inna → panel konfliktu.
- Późniejsze odświeżenia oferty nie nadpisują wpisywanych danych (formularz inicjowany raz).

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

### Interfejs - katalog i wymiana

`/oferty` (odnośnik „Oferty” na ekranie głównym klienta): saldo i lista aktywnych ofert (nazwa, opis, koszt, „0,20 zł/l, do 50 l (maks. 10,00 zł)”). Stany: szkielet ładowania, pusta lista, błąd z ponowieniem, ostrzeżenie o nieudanym odświeżeniu przy pokazanych danych.

- „Wymień punkty” otwiera potwierdzenie z kosztem i warunkami. Wysyłana jest wersja z tego potwierdzenia, nie z listy odświeżonej w międzyczasie. Przycisk potwierdzenia dostaje fokus (Enter potwierdza).
- Klucz: `useIdempotentMutation` z danymi `{ id, version }` - ten sam klucz do ostatecznej odpowiedzi.
- Po każdej próbie: odświeżenie salda, historii, kuponów i katalogu; odpowiedź (także odtworzona) nie jest źródłem salda.
- `409 offer_changed` zamyka potwierdzenie z komunikatem, katalog pokazuje nowe warunki - wymiana wymaga ponownego potwierdzenia (nowa wersja = nowy klucz). `offer_inactive`/`offer_not_found`, `insufficient_points` → komunikat „Punkty nie zostały pobrane”.
- Wynik nieznany (brak odpowiedzi, `5xx`, `503` zajęty klient/oferta, `idempotency.in_progress`): komunikat i tylko „Ponów wymianę” z tym samym kluczem; pozostałe wymiany zablokowane do rozstrzygnięcia. Klucz, oferta i wersja są w `sessionStorage` tej karty (`useIdempotentMutation` z `persist`), więc wyjście z ekranu albo przeładowanie nie gubi ich - po powrocie ekran pokazuje „mogła się nie zakończyć” i ponowienie z tym samym kluczem. Wpis należy do konta, które go utworzyło: wylogowanie i wygaśnięcie sesji go nie usuwają (`401` przy ponowieniu też nie), a ponowne zalogowanie tego konta wraca do „Ponów wymianę” z tym samym kluczem. Zalogowanie innego konta usuwa wpis ([ADR 0001](/decyzje/0001-architektura-frontendu/)). Bez dostępu do `sessionStorage` (tryb prywatny, blokada) klucz żyje tylko w komponencie.
- Maksymalny rabat liczony w groszach i mililitrach (liczby całkowite), z zaokrągleniem połówek w górę - jak `CouponDiscount` na serwerze (0,15 zł/l × 50,5 l = 7,58 zł).
- Wygasła sesja przy błędzie katalogu: odnośnik „Zaloguj się” zamiast ponowienia.
- Po sukcesie: „Kupiono kupon „…”. Aktywuj go w telefonie przed tankowaniem.”

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

### Interfejs - moje kupony

Ekran `/kupony` (skrót na ekranie głównym, odnośnik w komunikacie po wymianie):

- Lista od najnowszych: nazwa, warunki z migawki (te same co w katalogu, `lib/format.ts`), data zakupu, koszt, daty aktywacji i odbioru; status „Do aktywacji” / „Aktywny” / „Wykorzystany”.
- QR (liczony lokalnie, `CodeQr`), kod w grupach po 4 znaki i „Kopiuj kod” tylko dla kuponu `Active`. Kopiowanie i QR używają surowego kodu.
- Aktywacja: `useIdempotentMutation` (ten sam kupon → ten sam klucz do ostatecznej odpowiedzi). Odpowiedź nie jest wyświetlana - po każdej próbie lista jest czytana przez `GET`, a przycisk czeka na ten odczyt. Nieznany wynik → „Ponów aktywację” tym samym kluczem; gdy `GET` pokazuje już `Active`, ostrzeżenie znika (wynik znany). Klucz nie jest trwały jak przy wymianie: ponowna aktywacja nowym kluczem zwraca ten sam kod, więc nie ma czego dublować.
- Pusta lista → „Zobacz oferty”; wygasła sesja → „Zaloguj się”.

## Podgląd kuponu (kasjer)

`POST /api/cashier/coupons/preview` (rola `Cashier`, Admin nie dziedziczy, CSRF, bez `Idempotency-Key`), treść `{ cardCode, couponCode, amount?, liters? }`. Kody tylko w treści, nigdy w URL.

1. Walidacja (`400 rewards.validation_failed`): oba kody wymagane i w formacie (wpisane ręcznie są normalizowane jak przy kasie; kod karty w polu kuponu i odwrotnie → `invalid_format`); `amount` i `liters` razem albo wcale (brak jednego → `rewards.required_field`), zakresy i precyzja jak przy zakupie (`FuelPurchaseRules.ValidateQuantities`, kody `loyalty.*`).
2. Aktywna karta → właściciel; brak albo unieważniona → `404 loyalty.card_not_found`.
3. Kupon o tym kodzie **należący do właściciela karty**; cudzy i nieistniejący → identyczne `404 rewards.coupon_not_found` - kod nie zdradza, czyj jest kupon.
4. Odebrany → `409 rewards.coupon_redeemed`. Kupon `Purchased` nie ma kodu, więc nie da się go znaleźć.
5. `200 { offerName, discountPerLiter, maxLiters, calculation }`; `calculation` = `null` bez kwoty i litrów, inaczej `{ discount, paidAmount, pointsAwarded }` z `CouponDiscount` i `PointsPolicy` - tych samych co przy zakupie. Bez danych klienta i `id` kuponu.

Podgląd niczego nie zmienia ani nie rezerwuje (bez blokad); zakup sprawdza kupon i liczy rabat od nowa.

**Limit prób**: stałe okno na kasjera (partycja po id konta), `Rewards:PreviewPermitLimit` (domyślnie 30) prób na `Rewards:PreviewWindow` (domyślnie 1 min). Liczą się wszystkie próby, także nieudane; `401`/`403` z autoryzacji nie (middleware limitu działa po niej). Przekroczenie → `429 rate_limit.exceeded` z `Retry-After`. Kodu kuponu (128 bitów) nie da się zgadnąć - limit utrudnia głównie sprawdzanie kodów kart. Licznik jest w pamięci procesu (jedna instancja API).

## Odbiór razem z zakupem

Opcjonalne `couponCode` w `POST /api/cashier/fuel-purchases` (przepływ zakupu: [Loyalty](/moduly/loyalty/#przepływ)). Brak pola albo `null` = zakup bez kuponu; pusty albo niepoprawny tekst → `400 loyalty.validation_failed` z `errors.couponCode` = `rewards.invalid_format` (błędnego kodu nie ignorujemy).

Loyalty zna tylko interfejs `ICouponRedemption`; implementację (`CouponRedemption`, scoped) dostarcza Rewards. Obie metody działają na `AppDbContext` żądania i w transakcji zakupu:

1. `HoldAsync` - po blokadzie klienta, ponownym sprawdzeniu karty i sprawdzeniu paragonu: `SELECT … FOR UPDATE` po kodzie **i właścicielu karty**, z `SET LOCAL lock_timeout = Rewards:CouponLockTimeout`. Cudzy i nieistniejący → identyczne `404 rewards.coupon_not_found`; odebrany → `409 rewards.coupon_redeemed`; przekroczony czas → `503 rewards.coupon_busy` z `Retry-After` (niezapisywane, ten sam klucz działa później). Rabat liczy `CouponDiscount` z migawki kuponu.
2. Zapis zakupu z `Discount` i `PaidAmount`; punkty od `PaidAmount`.
3. `MarkRedeemedAsync` - warunkowe `UPDATE … SET Status = 'Redeemed', RedeemedAt = clock_timestamp(), RedeemedPurchaseId = … WHERE Id AND UserId AND Status = 'Active'`; brak zmienionego wiersza → wyjątek (`500`, wszystko wycofane). Odrzucone także bez blokady w bieżącej transakcji.

- Duplikat paragonu jest sprawdzany przed kuponem: ponowienie z nowym kluczem dostaje `409 loyalty.receipt_already_registered`, nie błąd kuponu. Niczego nie zapisujemy, zanim kupon nie okaże się ważny; `UNIQUE` paragonu nadal rozstrzyga wyścig między klientami.
- Zakupy jednego klienta szereguje blokada klienta, więc z dwóch zakupów z tym samym kuponem (różne klucze) drugi widzi `Redeemed`. Trwająca aktywacja nie ma jeszcze zatwierdzonego kodu, więc zakup jej nie znajdzie (`404`, zapisane pod kluczem) i nie czeka na nią; aktywacja zatwierdzona, gdy zakup czekał na blokadę klienta, jest widoczna. `503` daje tylko blokada wiersza trzymana przez inną transakcję. Kupon w stanie innym niż `Active` i `Redeemed` → `404`.
- `clock_timestamp()`, nie `now()`: `now()` to początek transakcji zakupu, który może być wcześniejszy niż `ActivatedAt` aktywacji, na którą czekał (`CK_Coupons_Timeline`).
- Odbiór widać w „Moich kuponach” (`Redeemed`, `redeemedAt`). Zakup w całości opłacony kuponem (0 zł, 0 pkt) nie ma wpisu w historii punktów.

### Interfejs - kupon w kasie

`features/cashier/components/CouponSection.tsx` w formularzu zakupu (pod kwotą i litrami):

- Pole „Kod kuponu” (wpisanie, wklejenie, czytnik QR kuponu). Normalizacja jak na serwerze (spacje, myślniki, wielkość liter, `O`→`0`, `I`/`L`→`1`); format sprawdzany przed wysłaniem.
- **Warunki** (`POST /api/cashier/coupons/preview` bez kwoty i litrów) pobierane dopiero, gdy kod karty i kod kuponu są kompletne - wpisywanie znak po znaku daje jedno zapytanie. `useCouponTerms`: klucz zapytania zawiera oba kody, więc spóźniona odpowiedź dla starej pary nie trafi do bieżącej; bez ponowień, bez odświeżania przy powrocie do okna.
- **Rabat** tylko po „Przelicz rabat” (podgląd z kwotą i litrami): rabat, do zapłaty, punkty. Zmiana kwoty lub litrów oznacza wyliczenie jako nieaktualne (bez nowego zapytania); zmiana kodu usuwa je. Każde przeliczenie ma numer - odpowiedź na porzucony wpis jest ignorowana.
- Błędy podglądu: kupon nieznaleziony/nieaktywny, wykorzystany, karta nieznaleziona, `429` (limit na kasjera; zakup można zapisać bez podglądu), brak połączenia.
- Zapis wysyła **kod**, nigdy wyliczony rabat: serwer sprawdza kupon ponownie. Po zapisie podglądy kuponów są usuwane z pamięci (kupon mógł zostać odebrany).
- Po każdej próbie zapisu z ostatecznym wynikiem (sukces albo błąd) i po „Odblokuj formularz” podglądy są zapominane, a sekcja kuponu czyszczona - kupon mógł zostać odebrany. Warunki dla tej samej pary kodów są trzymane w pamięci do 5 min, żeby ponowne wpisanie nie zużywało limitu.
- Czytniki karty i kuponu nie działają naraz: gdy jeden używa kamery, drugi jest wyłączony.
- Błędy zapisu: `coupon_not_found`, `coupon_redeemed` nad formularzem, `invalid_format` pod polem; `coupon_busy` (`503`) jak inne nieznane wyniki - formularz zablokowany, ponowienie tym samym kluczem.

Migracja `CouponRedeemedPurchaseOwner` przed dodaniem złożonego FK sprawdza istniejące dane: kupon odebrany na zakup innego klienta zatrzymuje migrację z liczbą takich wierszy i zapytaniem, które je wypisuje. Danych nie przepisuje.

## Nagrody produktowe

Decyzja: [ADR 0013](/decyzje/0013-nagrody-produktowe/).

- Admin wybiera rodzaj przy tworzeniu (`kind`, domyślnie `FuelDiscount`). Nieznany rodzaj → `kind invalid_format` bez błędów „wymagane” dla stawki i limitu; błędy wartości (zakres, precyzja) zostają. Nagroda ze stawką albo limitem → `400 rewards.not_for_product`; inny `kind` w `PUT` → `400 rewards.kind_immutable` (brak pola = bez zmiany).
- Wymiana i aktywacja bez zmian. Kupon kopiuje rodzaj, nazwę i koszt.
- `POST /api/cashier/reward-handovers` (`Cashier`, CSRF, `Idempotency-Key`, limit jak zakup), body `{ cardCode, couponCode }`:
  1. stanowisko kasjera, aktywna karta, blokada klienta (`503 loyalty.customer_busy`), ponowne sprawdzenie karty;
  2. kupon po kodzie **i właścicielu karty** - cudzy i nieznany → ten sam `404 rewards.coupon_not_found`;
  3. dopiero potem rodzaj: rabat → `409 rewards.coupon_wrong_kind`; wydany → `409 rewards.coupon_redeemed` z `handedOverAt`, `stationCode` i `registerCode` (tylko właściciel karty dochodzi do tego miejsca);
  4. wiersz `RewardHandovers` i warunkowy `UPDATE` kuponu (`Status = 'Active'`) w jednej transakcji.
- Bez zakupu, bez wpisu w historii punktów. Ten sam klucz odtwarza wynik; nowy klucz po wydaniu → `409`.
- Nagroda w zakupie paliwa → `409 rewards.coupon_wrong_kind`, nic nie zapisane. Podgląd zwraca `kind` i `null` w warunkach; kwota i litry przy nagrodzie → `409 coupon_wrong_kind` przed sprawdzeniem odbioru, jak w zakupie.
- UI: „Oferty” w sekcjach „Rabaty na paliwo” i „Nagrody”; „Moje kupony i nagrody” z przyciskiem „Aktywuj odbiór”; w kasie po wpisaniu kodu nagrody przycisk „Wydaj nagrodę” zamiast wyliczenia rabatu (nieznany wynik → „Ponów wydanie” tym samym kluczem); admin wybiera rodzaj, pola stawki i limitu tylko dla rabatu, na edycji rodzaj jako tekst.
- Konta demo: Kawa 150, Hot dog 250, Myjnia 600 pkt (`DemoAccountsSeeder`, tylko gdy brak oferty o tej nazwie).
- Kasa, wynik wydania:
  - `429` i `loyalty.customer_busy` przy pierwszej próbie to pewna odmowa („nie została wydana - spróbuj za chwilę”);
  - po nieznanym wyniku (brak odpowiedzi, `5xx`) zostaje „Nie wiadomo, czy nagroda została wydana” także przy `429` na ponowieniu - limit działa przed idempotencją, więc nie mówi nic o pierwszej próbie; ponowienie tym samym kluczem;
  - także `401` po nieznanym wyniku go nie kasuje (dopisek „Sesja wygasła”);
  - nieznany wynik żyje tylko w panelu tej pary kodów (stan komponentu; przetrwa ukrycie trasy przez `Activity`) - decyzja właściciela po ósmym review. Ponowienie idzie tym samym kluczem, dopóki panel jest zamontowany; próba zapisu zakupu albo odświeżony podgląd zaczynają od nowa (nowy klucz). Formularz nie jest blokowany: kasjer może obsłużyć następnego klienta, a każda późniejsza próba wydania z tym kodem (albo świeży podgląd - nie ten z pamięci) dostaje od serwera `409` z czasem, stanowiskiem i kasą wydania. Kody nie trafiają do `sessionStorage`; poza panelem klient ich nie trzyma (poza zwykłym cache podglądu i mutacji TanStack Query w pamięci karty);
  - rozstrzyga serwer: `409 rewards.coupon_redeemed` nagrody (wydanie i podgląd) niesie `handedOverAt`, `stationCode` i `registerCode` (typ `RewardsConflictProblem` w OpenAPI). Kasa porównuje je ze swoim stanowiskiem: „…wydana 22.11.2026, 14:32 na tej kasie. Jeśli to przed chwilą, odpowiedź poprzedniej próby nie dotarła” albo „…na stanowisku S-01, kasa K2”. Działa też po ponownym logowaniu;
  - formularz jest zablokowany w trakcie wydania, więc spóźniony sukces nie wyczyści kodu następnego klienta; ostateczna odpowiedź zostaje w panelu (bez ponownego podglądu, który zużyłby limit). Ponowne wpisanie kodu w ciągu 5 min pokazuje warunki z pamięci i znowu „Wydaj nagrodę”; dopiero kliknięcie dostaje `409` z czasem i miejscem (nic się nie wydaje drugi raz);
  - ostateczna odpowiedź kończy operację: kolejna próba nie dziedziczy ostrzeżenia;
  - inna para karta + kod to nowa operacja (bez błędu poprzedniej); komunikat „Wydano nagrodę” jest kasowany przy zmianie karty i nie wraca po jej ponownym wpisaniu.
- „Moje kupony i nagrody”: wydana nagroda - „Nagroda wydana w kasie”.
- Czat: oferty i kupony z `kind` (prompt `gemini-v3` opisuje rodzaje); `compare_coupons` tylko rabaty; karta potwierdzenia „Aktywować odbiór nagrody…”.

## Zabezpieczenia

- Rabat liczy serwer z kopii parametrów w kuponie; klient nie wysyła kwoty rabatu.
- Kody kuponów nie trafiają do logów; w żądaniach tylko w treści (POST), nie w URL. Kod jest też w treści zapisanej odpowiedzi aktywacji w `IdempotencyRecords`; MVP nie usuwa tych wierszy (retencję trzeba ustalić przed produkcją, `PLAN-MVP.md`), więc dostęp do bazy i kopii zapasowych daje dostęp do kodów.
- Odpowiedzi grupy `/api/me/coupons` po sprawdzeniu dostępu i CSRF (także błędy domenowe i odtworzone odpowiedzi) mają `Cache-Control: no-store`; `401`/`403` go nie mają, ale nie zawierają kodu: przeglądarka i pośrednicy nie zapisują odpowiedzi z kodem. Filtr jest na grupie, czyli na zewnątrz filtra idempotencji - odtworzenie omija endpoint i jego własne filtry. Nagłówek nie dotyczy pamięci TanStack Query w karcie przeglądarki.
- Aktywacja tylko własnego kuponu: `UserId` jest w warunku `UPDATE`.
- Podgląd: kupon musi należeć do właściciela zeskanowanej karty; limit prób na kasjera; kody nie trafiają do logów (test do poziomu Trace).
- Odbiór: kupon właściciela karty w zapytaniu i w warunku `UPDATE`, a w bazie złożony FK właściciela; jednorazowość przez warunek `Status = 'Active'` i `UNIQUE (RedeemedPurchaseId)`; kody nie trafiają do logów.

## Testy

| Plik | Sprawdza |
| --- | --- |
| `RewardsRulesTests.cs` - `CouponDiscountTests` | przykłady z tabeli, limit litrów, zaokrąglenie połówek (0,045 → 0,05; 0,005 → 0,01), ograniczenie do brutto i zapłata 0 → 0 pkt, punkty od kwoty zapłaconej, odrzucenie niezwalidowanych danych |
| `RewardsRulesTests.cs` - `CouponCodeTests` | 27 znaków z prefiksem `K`, część losowa to pełny kod w formacie karty, unikalność; kod karty ≠ kod kuponu (w obie strony); normalizacja wpisanych wariantów |
| `RewardsRulesTests.cs` - `RewardOfferRulesTests` | wymagana nazwa, długości, zakres kosztu, stawki i limitu; precyzja (0,205 zł/l, 50,0005 l) odrzucona |
| `RewardsModelConstraintsTests.cs` | kupon odebrany na zakup innego klienta odrzucony przez złożony FK; sprawdzenie z migracji przechodzi na spójnych danych i zatrzymuje się na niespójnych (stan sprzed migracji odtworzony w wycofanej transakcji); wszystkie poprawne stany kuponu; `CHECK` zakresów oferty i snapshotu; pusta nazwa; **baza zaokrągla 0,205 → 0,21** (dlatego walidacja w aplikacji); 7 niespójnych stanów kuponu; aktywacja przed zakupem; zły format kodu; duplikat kodu; jeden kupon na zakup; oferty z kuponami nie da się usunąć; `Version < 1` odrzucone; równoległe zapisy przez EF wykryte, każdy podnosi wersję |
| `RewardExchangeTests.cs` | wymiana: `201`, kupon = migawka oferty, bez kodu, `Location`, saldo, odczyt kuponu i listy, wpis `CouponExchange` (`-koszt`, `balanceAfter`, `couponExchange`); całe saldo do wydania; brak punktów (499 i brak konta) → `409` bez zmian; kolejność: brak → `404`, wyłączona (z podniesioną wersją) → `offer_inactive`, zmieniona → `offer_changed`, bez obciążenia; `version` wymagane i `>= 1`; ten sam klucz → ten sam kupon, jedno obciążenie, także równolegle; **6 równoległych wymian przy saldzie na 2 → dokładnie 2 kupony, saldo 0, `balanceAfter` 500, 0**; **edycja admina czeka na trwającą wymianę (bramka), kupon ma warunki, które klient widział**, potem stara wersja → `offer_changed`; klient zajęty → `503` z `Retry-After`, bez zmian, ten sam klucz działa później; oferta zablokowana dłużej niż limit → `503 rewards.offer_busy`, bez zmian, ten sam klucz działa później; zakres `OfferLockTimeout`; cudzy kupon → `404`, lista tylko własnych; `401`, `403` kasjer, CSRF, brak klucza → `400` |
| `CouponActivationTests.cs` | aktywacja: `code` `null` przed, potem poprawny format, zapisane wartości = odpowiedź, kod w obu `GET`, saldo i historia bez zmian; `activatedAt` późniejsze niż początek transakcji (rekord idempotencji); ponowna aktywacja nowym kluczem → ten sam kod; ponowienie tym samym kluczem → odtworzenie z tym samym kodem; 6 równoległych aktywacji → jeden kod; równolegle jednym kluczem → jeden kod; odebrany → `409`, status i kod bez zmian; odtworzenie po odbiorze zwraca zapisaną odpowiedź, `GET` pokazuje `Redeemed`; kupon wyłączonej oferty da się aktywować; cudzy i nieistniejący → `404`, kupon nietknięty; `no-store` na aktywacji, odtworzeniu, `404` i obu `GET` (kontrola: `/api/me/wallet` bez nagłówka); kod nie trafia do logów (Trace, także SQL); `401`, `403` kasjer, CSRF, brak klucza |
| `CouponPreviewTests.cs` | podgląd: same warunki bez kwoty; obliczenie = tabela rabatów (limit litrów, połówka w górę, rabat do brutto i 0 pkt); kody wpisane ręcznie normalizowane; tylko jedno z `amount`/`liters` → `required_field`, zakresy i precyzja jak przy zakupie; wymagane kody, zamienione kody → `invalid_format`; nieznana i unieważniona karta → `404`; **cudzy kupon → odpowiedź identyczna jak dla nieistniejącego**; odebrany → `409`, cudzy odebrany → `404`; po podglądzie kupon, saldo, zakupy i historia bez zmian; **limit na kasjera liczy też nieudane próby, potem `429` z `Retry-After`, inny kasjer działa**; kody nie trafiają do logów; `403` dla User i Admin, `401`, CSRF; zakres `PreviewPermitLimit` i `PreviewWindow` |
| `CouponRedemptionTests.cs` | odbiór z zakupem: rabat i zapłata w odpowiedzi i zakupie, punkty od zapłaconej kwoty, kupon `Redeemed` z `RedeemedPurchaseId`, `redeemedAt` po `activatedAt` i po początku transakcji (rekord idempotencji), historia z `grossAmount`/`discount`/`paidAmount`, „Moje kupony”; rabat = cała kwota → 0 pkt bez wpisu; brak pola i `null` → zwykły zakup; pusty, same spacje i zły format → `400` bez zapisu; kod wpisany ręcznie normalizowany; **cudzy kupon → odpowiedź identyczna jak dla nieistniejącego**; kupon nieaktywowany nieosiągalny; odebrany → `409` bez zmian; **duplikat paragonu ma pierwszeństwo przed odebranym kuponem**, a z aktywnym kuponem nie rusza go; **2 równoległe zakupy z jednym kuponem → jeden odbiór**; ponowienie tym samym kluczem → odtworzenie, jeden zakup; trwająca aktywacja → `404`; aktywacja zatwierdzona w trakcie czekania na blokadę klienta → odbiór; zablokowany wiersz kuponu → `503 rewards.coupon_busy` bez zapisu, ten sam klucz działa później; **błąd zapisu zakupu albo wpisu historii (trigger) → kupon aktywny, saldo, historia i zakupy bez zmian, ten sam klucz działa później**; kody nie trafiają do logów; odbiór bez blokady w bieżącej transakcji albo z innym właścicielem odrzucony; zakres `CouponLockTimeout` |
| `apps/web/e2e/rewards.spec.ts` (mobile + desktop) | warunki i koszt w katalogu, potwierdzenie, jeden kupon, saldo po wymianie, wpis „Wymiana na kupon” `-500 pkt` w historii; za mało punktów → komunikat, saldo i kupony bez zmian; **zmiana oferty przez admina w trakcie potwierdzenia → komunikat, nowe warunki, brak obciążenia, ponowne potwierdzenie kupuje po nowej cenie**; **odpowiedź zerwana po zatwierdzeniu → ponowienie tym samym kluczem, jeden kupon, jedno obciążenie**; **wyjście z ekranu i przeładowanie przy nieznanym wyniku → blokada i ten sam klucz, jeden kupon**; błąd ładowania katalogu z ponowieniem. Oferty tworzone przez testy są wyłączane po teście (fixture `staff`) |
| `apps/web/e2e/coupons.spec.ts` (mobile + desktop) | status i warunki przed aktywacją, brak QR; aktywacja → kod w formacie `K…`, **QR zdekodowany ze zrzutu = kod**, ten sam kod po przeładowaniu i w API; odbiór w kasie (rabat 8 zł) → „Wykorzystany”, bez QR, kodu i przycisków; **odpowiedź zerwana po zatwierdzeniu → `GET` pokazuje zapisany kod, jedno żądanie**; błąd przed serwerem → ponowienie tym samym kluczem; **dwa konta widzą tylko swoje kupony, cudzy przez API → `404`**; pusta lista z odnośnikiem do ofert |
| `apps/web/e2e/cashier-coupon.spec.ts` (mobile + desktop) | **kod wpisywany znak po znaku → jedno zapytanie o warunki, bez wyliczenia**; „Przelicz” → 8,00 zł / 92,00 zł; zmiana litrów → wyliczenie nieaktualne bez zapytania, ponowne → 10,00 zł; zapis → wynik z rabatem i 90 pkt, **wysłany kod, bez rabatu**, formularz wyczyszczony; klient: kupon „Wykorzystany”, historia z rabatem; wykorzystany kupon → komunikat w podglądzie i przy zapisie, bez zapisu; **utracona odpowiedź zakupu z kuponem → ponowienie tym samym kluczem, jeden odbiór**; **spóźniona odpowiedź „Przelicz” po ponownym wpisaniu kuponu ignorowana**; wykorzystany kupon przy zapisie czyści podgląd; `429` (zasymulowane) → komunikat, zapis nadal działa; skan QR kuponu: czytnik karty go ignoruje, czytnik kuponu wypełnia tylko pole kuponu, bez `POST`; w trakcie skanu drugi czytnik wyłączony |
| `apps/web/e2e/admin.spec.ts` (mobile + desktop) | tworzenie przez formularz (walidacja 0,205 zł/l po stronie klienta), oferta w katalogu klienta, wyłączenie → znika z katalogu; **dwóch adminów edytuje tę samą ofertę: drugi dostaje konflikt z aktualną wersją, formularz zachowany i zablokowany, „Wczytaj aktualną wersję”, potem pierwszy świadomie nadpisuje na wersji 3**; **utracona odpowiedź `PUT` → „Wynik zapisu nieznany”, sprawdzenie stanu → „Zapisano (wersja 2)”, jeden `PUT`**; **utracona odpowiedź tworzenia → wyjście z ekranu i powrót: blokada i to samo tworzenie, ponowienie tym samym kluczem, jedna oferta**; klient i kasjer: brak skrótu, przekierowanie z `/admin/...`, API `403` |
| `RewardOfferTests.cs` | katalog: tylko aktywne, kolejność koszt → nazwa → `id` (także dla identycznego kosztu i nazwy), pola; dostęp: katalog tylko `User` (`401` anonim, `403` kasjer i sam Admin), część admina tylko `Admin`; tworzenie: `201` + `Location`, `version 1`, odczyt pojedynczy i na liście; idempotencja `POST` (jedna oferta), brak klucza → `400`; walidacja pól (w tym 0,205 zł/l i brak `isActive`) bez zapisu wiersza; brak liczb → `required_field` w `POST` i `PUT`; liczby jako tekst → `400`; zmiana przez EF podnosi wersję, więc starszy formularz dostaje `409`; `PUT` zastępuje formularz i podnosi wersję, odpowiedź = zapisany wiersz; **nieaktualny formularz → `409` bez nadpisania**; **8 równoległych `PUT` z tą samą wersją → dokładnie jeden `200`, 7 × `409`, wersja 2**; `version` wymagane i `>= 1`, walidacja przed konfliktem; nieznane `id` → `404`; wyłączenie ukrywa w katalogu, włączenie przywraca; edycja oferty nie zmienia wydanego kuponu; CSRF |
| `ProductRewardsTests.cs` | admin: nagroda bez warunków, warunki przy nagrodzie → `not_for_product`, rabat bez warunków i nieznany rodzaj → `400`, zmiana rodzaju → `kind_immutable`, edycja bez `kind` zachowuje rodzaj; wymiana, aktywacja i wydanie: snapshot nazwy i kosztu po zmianie oferty, ponowienie tym samym kluczem, nowy klucz → `coupon_redeemed`, wydanie z kasjerem, stacją i kasą, bez zakupu i bez wpisu w historii; rabat przy wydaniu i nagroda przy zakupie → `coupon_wrong_kind` (bez zakupu); **cudzy kupon rabatowy → `404`, nie „zły rodzaj”**; podgląd nagrody; klient → `403`; **2 równoległe wydania różnymi kluczami → jedno**; baza odrzuca warunki przy nagrodzie, kupon innego rodzaju niż oferta i odebraną nagrodę bez wydania |
| `ProductRewardsMigrationTests.cs` | cofnięcie `ProductRewards` na bazie tymczasowej: bez nagród działa (brak `RewardHandovers`), ponowne `Up` bez `DEFAULT` w stawce i limicie; z nagrodą i kuponem → `P0001`, historia i dane bez zmian |
| `apps/web/e2e/product-rewards.spec.ts` (mobile + desktop) | sekcja „Nagrody”, wymiana, „Aktywuj odbiór”, kasjer wpisuje kartę i kod → „Wydaj nagrodę”, komunikat i wyczyszczone pole; ten sam kod ponownie → „wydana … na tej kasie”; klient widzi „Wykorzystany”; **zgubiona odpowiedź, potem `429` → nadal „Nie wiadomo”, nigdy „nie została wydana”; trzecie ponowienie tym samym kluczem → jedno wydanie**; **ostateczne `409` z innej kasy zostaje w panelu („stanowisko S-99, kasa K9”), nie znika po ponownym podglądzie**; **nieznany wynik zostaje w panelu bez blokady formularza; kolejny kod startuje czysto, powrót do pierwszego → `409` „na tej kasie”; `401` go nie kasuje, ponowienie tym samym kluczem odtwarza wydanie; wyjście z kasy i powrót - ostrzeżenie i „Ponów wydanie” zostają**; formularz zablokowany w trakcie wydania; komunikat kasowany przy zmianie karty, nie wraca dla tej samej karty |
| `apps/web/e2e/admin.spec.ts` - nagroda | wybór „Nagroda (produkt)” ukrywa pola stawki i limitu, lista pokazuje „nagroda”, edycja pokazuje rodzaj bez wyboru i zapisuje wersję 2 |
| `LoyaltyModelConstraintsTests.cs` | rabat równy brutto (zapłata 0, 0 pkt) przyjęty; rabat poza `0 - brutto` i `PaidAmount ≠ GrossAmount - Discount` odrzucone; znak `Delta` zgodny z typem |

Próby negatywne: zaokrąglenie bankierskie (`ToEven`) i brak ograniczenia do brutto wywracają testy obliczeń; `UPDATE` bez warunku na wersję wywraca testy nieaktualnego formularza i równoległej edycji; brak podnoszenia wersji w `SaveChanges` wywraca oba testy zapisu przez EF; wymiana bez warunku `Balance >= koszt`, bez `FOR SHARE` albo ze sprawdzeniem wersji przed wyłączeniem wywraca odpowiednie testy wymiany. Aktywacja bez warunku na status wywraca testy odebranego kuponu, ponownej aktywacji i równoległych aktywacji; `now()` zamiast `clock_timestamp()` wywraca test czasu; kod wklejony w SQL wywraca test logów; brak `no-store` albo filtr tylko na endpoincie (wewnątrz idempotencji) wywraca testy nagłówka. Podgląd bez warunku na właściciela kuponu wywraca testy cudzego kuponu; bez sprawdzenia statusu - test odebranego; bez `RequireRateLimiting` - test limitu. Katalog bez obsługi nieznanego wyniku wywraca test utraconej odpowiedzi wymiany; wymiana bez `persist` wywraca test wyjścia z ekranu; aktywacja bez odczytu `GET` po próbie wywraca testy aktywacji; przeliczenie bez numeru żądania wywraca test spóźnionej odpowiedzi; edycja traktująca brak odpowiedzi jak zwykły błąd wywraca test utraconego `PUT`; tworzenie bez `persist` wywraca test utraconej odpowiedzi tworzenia. Odbiór z `now()` zamiast `clock_timestamp()` wywraca test czasu i test aktywacji zatwierdzonej w trakcie czekania (`CK_Coupons_Timeline`); bez sprawdzenia paragonu przed kuponem - test pierwszeństwa paragonu; bez właściciela w `UPDATE` - test odbioru z innym właścicielem; pusty kod traktowany jak brak - test walidacji; bez obsługi `lock_timeout` - test `503`; bez sprawdzenia statusu - testy odebranego kuponu i równoległych zakupów.

Testy nagłówka używają hosta z domyślnym interwałem sprawdzania sesji (`ApiFactory.WithoutPerRequestSessionRenewal`). Główny host testowy sprawdza sesję przy każdym żądaniu, więc odnawia ciasteczko, a handler ciasteczek dodaje wtedy `no-store` do każdej odpowiedzi i ukryłby brak nagłówka.
