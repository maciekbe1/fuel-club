---
title: Rewards (oferty i kupony)
description: Oferty z rabatem na litr, kupony klientów, kod kuponu i obliczenie rabatu.
sidebar:
  order: 3
---

:::caution[W trakcie]
Zrealizowane: model danych, ograniczenia w bazie, reguły (kod kuponu, obliczenie rabatu, walidacja oferty), API ofert (katalog, zarządzanie przez Admin z wersją), wymiana punktów na kupon, odczyt własnych kuponów, aktywacja kuponu, podgląd kuponu dla kasjera z limitem prób, odbiór razem z zakupem, interfejs katalogu z wymianą, ekran „Moje kupony” z aktywacją i kodem QR, kupon w panelu kasjera z podglądem, rabat w historii. Planowane: panel administratora.
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
| Podgląd kuponu (kasjer) | `Cashier/CouponPreviewEndpoint.cs` |
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
| `RedeemedPurchaseId` | `uuid` | złożony FK `(RedeemedPurchaseId, UserId)` → `FuelPurchases (Id, UserId)` (RESTRICT): kupon tylko na zakup własnego właściciela; `UNIQUE` - jeden kupon na zakup |

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

### Interfejs - katalog i wymiana

`/oferty` (odnośnik „Oferty” na ekranie głównym klienta): saldo i lista aktywnych ofert (nazwa, opis, koszt, „0,20 zł/l, do 50 l (maks. 10,00 zł)”). Stany: szkielet ładowania, pusta lista, błąd z ponowieniem, ostrzeżenie o nieudanym odświeżeniu przy pokazanych danych.

- „Wymień punkty” otwiera potwierdzenie z kosztem i warunkami. Wysyłana jest wersja z tego potwierdzenia, nie z listy odświeżonej w międzyczasie. Przycisk potwierdzenia dostaje fokus (Enter potwierdza).
- Klucz: `useIdempotentMutation` z danymi `{ id, version }` - ten sam klucz do ostatecznej odpowiedzi.
- Po każdej próbie: odświeżenie salda, historii, kuponów i katalogu; odpowiedź (także odtworzona) nie jest źródłem salda.
- `409 offer_changed` zamyka potwierdzenie z komunikatem, katalog pokazuje nowe warunki - wymiana wymaga ponownego potwierdzenia (nowa wersja = nowy klucz). `offer_inactive`/`offer_not_found`, `insufficient_points` → komunikat „Punkty nie zostały pobrane”.
- Wynik nieznany (brak odpowiedzi, `5xx`, `503` zajęty klient/oferta, `idempotency.in_progress`): komunikat i tylko „Ponów wymianę” z tym samym kluczem; pozostałe wymiany zablokowane do rozstrzygnięcia. Klucz, oferta i wersja są w `sessionStorage` tej karty (`useIdempotentMutation` z `persist`), więc wyjście z ekranu albo przeładowanie nie gubi ich - po powrocie ekran pokazuje „mogła się nie zakończyć” i ponowienie z tym samym kluczem. Logowanie, rejestracja i wylogowanie czyszczą zapisane klucze (należą do poprzedniego konta). Bez dostępu do `sessionStorage` (tryb prywatny, blokada) klucz żyje tylko w komponencie.
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
| `RewardOfferTests.cs` | katalog: tylko aktywne, kolejność koszt → nazwa → `id` (także dla identycznego kosztu i nazwy), pola; dostęp: katalog tylko `User` (`401` anonim, `403` kasjer i sam Admin), część admina tylko `Admin`; tworzenie: `201` + `Location`, `version 1`, odczyt pojedynczy i na liście; idempotencja `POST` (jedna oferta), brak klucza → `400`; walidacja pól (w tym 0,205 zł/l i brak `isActive`) bez zapisu wiersza; brak liczb → `required_field` w `POST` i `PUT`; liczby jako tekst → `400`; zmiana przez EF podnosi wersję, więc starszy formularz dostaje `409`; `PUT` zastępuje formularz i podnosi wersję, odpowiedź = zapisany wiersz; **nieaktualny formularz → `409` bez nadpisania**; **8 równoległych `PUT` z tą samą wersją → dokładnie jeden `200`, 7 × `409`, wersja 2**; `version` wymagane i `>= 1`, walidacja przed konfliktem; nieznane `id` → `404`; wyłączenie ukrywa w katalogu, włączenie przywraca; edycja oferty nie zmienia wydanego kuponu; CSRF |
| `LoyaltyModelConstraintsTests.cs` | rabat równy brutto (zapłata 0, 0 pkt) przyjęty; rabat poza `0 - brutto` i `PaidAmount ≠ GrossAmount - Discount` odrzucone; znak `Delta` zgodny z typem |

Próby negatywne: zaokrąglenie bankierskie (`ToEven`) i brak ograniczenia do brutto wywracają testy obliczeń; `UPDATE` bez warunku na wersję wywraca testy nieaktualnego formularza i równoległej edycji; brak podnoszenia wersji w `SaveChanges` wywraca oba testy zapisu przez EF; wymiana bez warunku `Balance >= koszt`, bez `FOR SHARE` albo ze sprawdzeniem wersji przed wyłączeniem wywraca odpowiednie testy wymiany. Aktywacja bez warunku na status wywraca testy odebranego kuponu, ponownej aktywacji i równoległych aktywacji; `now()` zamiast `clock_timestamp()` wywraca test czasu; kod wklejony w SQL wywraca test logów; brak `no-store` albo filtr tylko na endpoincie (wewnątrz idempotencji) wywraca testy nagłówka. Podgląd bez warunku na właściciela kuponu wywraca testy cudzego kuponu; bez sprawdzenia statusu - test odebranego; bez `RequireRateLimiting` - test limitu. Katalog bez obsługi nieznanego wyniku wywraca test utraconej odpowiedzi wymiany; wymiana bez `persist` wywraca test wyjścia z ekranu; aktywacja bez odczytu `GET` po próbie wywraca testy aktywacji; przeliczenie bez numeru żądania wywraca test spóźnionej odpowiedzi. Odbiór z `now()` zamiast `clock_timestamp()` wywraca test czasu i test aktywacji zatwierdzonej w trakcie czekania (`CK_Coupons_Timeline`); bez sprawdzenia paragonu przed kuponem - test pierwszeństwa paragonu; bez właściciela w `UPDATE` - test odbioru z innym właścicielem; pusty kod traktowany jak brak - test walidacji; bez obsługi `lock_timeout` - test `503`; bez sprawdzenia statusu - testy odebranego kuponu i równoległych zakupów.

Testy nagłówka używają hosta z domyślnym interwałem sprawdzania sesji (`ApiFactory.WithoutPerRequestSessionRenewal`). Główny host testowy sprawdza sesję przy każdym żądaniu, więc odnawia ciasteczko, a handler ciasteczek dodaje wtedy `no-store` do każdej odpowiedzi i ukryłby brak nagłówka.
