---
title: Loyalty
description: Punkty za tankowanie - model danych i reguła naliczania.
sidebar:
  order: 2
---

:::caution[W trakcie]
Zrealizowane (API): model danych i ograniczenia w bazie, reguła naliczania i walidacja, rola Cashier ze stanowiskiem, karta lojalnościowa (odczyt, wydanie, wymiana), Idempotency-Key ([idempotencja](/wyjasnienia/idempotencja/)), zakup przez kasę z naliczeniem punktów, saldo i historia. Frontend: ekran główny z saldem, karta z kodem QR, historia, panel kasjera z czytnikiem QR. Planowane: wymuszenie niezmienności historii w bazie.
:::

## Co robi

Docelowo nalicza punkty za zakup przesłany przez kasę po skanie karty klienta. W demo kasę zastępuje panel zalogowanego kasjera (Cashier). Administrator zarządza ofertami; użytkownik nie może sam dopisać sobie punktów.

Kod karty identyfikuje odbiorcę punktów, nie uwierzytelnia wykonawcy. Kod aktywnego kuponu jest osobnym tokenem realizacji. Szczegóły: [ADR 0004](/decyzje/0004-karta-i-kasa/).

### Reguła naliczania

**1 punkt za każdy pełny 1 zł zapłacony**: `points = floor(PaidAmount)`, gdzie `PaidAmount = kwota brutto - rabat z kuponu`. Bez kuponu rabat wynosi 0. Zapłata 0 zł (rabat równy kwocie) daje 0 pkt i nie jest błędem ([ADR 0006](/decyzje/0006-model-korzysci-kuponu/)).

| Kwota | Punkty |
| --- | --- |
| 0,99 zł | 0 |
| 1,00 zł | 1 |
| 123,99 zł | 123 |
| 5000,00 zł | 5000 |

### Reguły tankowania

| Pole | Reguła | Kod błędu |
| --- | --- | --- |
| kwota | `0 < kwota ≤ 5000 zł` | `loyalty.amount_out_of_range` |
| kwota | maks. 2 miejsca po przecinku | `loyalty.amount_precision` |
| litry | obowiązkowe, `0 < litry ≤ 9 999 999,999` (pojemność `numeric(10,3)`) | `loyalty.liters_out_of_range` |
| litry | maks. 3 miejsca po przecinku | `loyalty.liters_precision` |
| data sprzedaży | wymagana, `default(DateOnly)` (0001-01-01) odrzucana | `loyalty.required_field` |
| data sprzedaży | nie później niż dziś w strefie stacji (`Loyalty:StationTimeZone`, demo `Europe/Warsaw`); starsze daty dozwolone. Sprawdzane w aplikacji: `CHECK` z `now()` jest w PostgreSQL dozwolony, ale ocenia wiersz tylko przy zapisie i nie daje stale aktualnej walidacji czasu | `loyalty.sale_date_in_future` |
| stacja, kasa | wymagane, maks. 32 znaki, znaki `A-Z 0-9 -` | `loyalty.required_field`, `loyalty.too_long`, `loyalty.invalid_format` |
| numer paragonu | wymagany, maks. 64 znaki, znaki `A-Z 0-9 - /` | `loyalty.required_field`, `loyalty.too_long`, `loyalty.invalid_format` |

Limit 5000 zł to jawna reguła biznesowa (`FuelPurchaseRules.MaxAmount`). Chroni przed pomyłką przy wpisywaniu. Limit litrów wynika z pojemności kolumny, nie z reguły biznesowej.

### Normalizacja paragonu

Stacja, kasa i numer paragonu są przed walidacją i zapisem **przycinane i zamieniane na wielkie litery** (`FuelPurchaseRules.Normalize`). `" st-001 "` i `"ST-001"` to ten sam paragon, więc spacje i wielkość liter nie ominą unikalności.

Kolejność jest stała: `Normalize` → `Validate` → zapis. `Validate` odrzuca postać nieznormalizowaną (`loyalty.invalid_format`), więc pominięcie `Normalize` kończy się błędem, nie duplikatem. Sprawdza dwie rzeczy: wartość musi być identyczna z wynikiem normalizacji i pasować do wzorca. Sam wzorzec nie wystarcza, bo w .NET `$` dopasowuje się także przed końcowym `\n`, więc przepuściłby `"ST-001\n"`. Baza przyjmuje tylko postać znormalizowaną (`CHECK` z tym samym wzorcem; w PostgreSQL `$` oznacza koniec tekstu, więc `\n` jest tam odrzucany).

Dozwolone znaki to założenie MVP. Wykluczają spacje w środku i znaki Unicode podobne do liter łacińskich (np. cyrylickie `А`). Prawdziwe paragony z innymi znakami wymagają rozszerzenia wzorca (`CodePattern`, `ReceiptNumberPattern`) i migracji.

## Gdzie jest kod

| Element | Ścieżka |
| --- | --- |
| Reguła punktów | `apps/api/Modules/Loyalty/Domain/PointsPolicy.cs` |
| Walidacja tankowania | `Domain/FuelPurchaseRules.cs` |
| Encje | `Domain/Entities.cs` |
| Mapowanie i ograniczenia bazy | `Infrastructure/LoyaltyModelConfiguration.cs` |
| Kody błędów | `LoyaltyErrorCodes.cs` |
| Karta: kod (generowanie, normalizacja) | `Domain/LoyaltyCard.cs` (`LoyaltyCardCode`) |
| Karta: endpointy | `Card/LoyaltyCardEndpoints.cs` |
| Saldo i historia | `Wallet/WalletEndpoints.cs` |
| Frontend: saldo | `apps/web/features/wallet/` (`services.ts` → `hooks.ts` → `components/BalanceCard.tsx`), ekran główny `apps/web/components/home/HomeScreen.tsx` |
| Kasjer: zakup | `Cashier/FuelPurchaseEndpoint.cs` |
| Kasjer: stanowisko | `Domain/CashierStation.cs`, `Cashier/CashierStationEndpoint.cs` |
| Kasjer: tworzenie konta | `Infrastructure/CashierProvisioning.cs` (`BootstrapCashier`) |
| Trasy i rejestracja modułu | `LoyaltyModule.cs` |
| Konta demo (wspólne) | `apps/api/Infrastructure/DemoData/DemoAccountsSeeder.cs`, `DemoPasswordReset.cs` (komenda `reset-demo-passwords`), `SimulationPasswordReset.cs` (komenda `reset-simulation-passwords` - hasła kont symulatora z nowego `Simulation:Secret`) |
| Migracje | `apps/api/Migrations/*_LoyaltyModel.cs`, `*_LoyaltyReceiptNormalization.cs`, `*_CashierStations.cs`, `*_LoyaltyCards.cs`, `*_LedgerSequence.cs` |

## Przepływ

`POST /api/cashier/fuel-purchases` (`Cashier/FuelPurchaseEndpoint.cs`), rola `Cashier`, CSRF, `Idempotency-Key`. Treść: `cardCode`, `amount`, `liters`, `saleDate`, `receiptNumber`, opcjonalnie `couponCode` (odbiór kuponu: [Rewards](/moduly/rewards/#odbiór-razem-z-zakupem)). Stacji i kasy nie ma w treści - backend bierze je z `CashierStations`; pola o tych nazwach w treści są ignorowane.

Kolejność w jednej transakcji (otwiera ją idempotencja):

1. **Rezerwacja klucza idempotencji** (filtr) i savepoint.
2. **Walidacja** bez zmian w bazie: kod karty (`LoyaltyCardCode.Normalize` + format), `FuelPurchaseRules.Normalize`/`Validate` ze stacją i kasą kasjera, `couponCode` (jeśli jest w treści i nie jest `null`) przez `ICouponRedemption.Validate`. Błąd → `400 loyalty.validation_failed` z `errors[field][]`; niezapisywany, więc poprawione żądanie może użyć tego samego klucza. Kasjer bez stanowiska → `404 loyalty.cashier_station_missing`.
3. **Wyszukanie aktywnej karty** po kodzie → właściciel. Brak albo karta unieważniona → `404 loyalty.card_not_found` (bez rozróżnienia, który przypadek).
4. **Blokada klienta** (właściciela karty). Limit → `503 loyalty.customer_busy` z `Retry-After`.
5. **Ponowne sprawdzenie karty** po blokadzie. Unieważniona w międzyczasie → `404 loyalty.card_not_found`.
6. **Sprawdzenie paragonu**: istniejący paragon → `409 loyalty.receipt_already_registered`, bez informacji o właścicielu - przed kuponem, więc błąd kuponu nie ukrywa duplikatu.
7. **Kupon** (gdy podany): `ICouponRedemption.HoldAsync` blokuje kupon właściciela karty i liczy rabat; `404`/`409`/`503` jak w [Rewards](/moduly/rewards/#odbiór-razem-z-zakupem). Punkty od kwoty po rabacie.
8. **Zapis zakupu** (`FuelPurchase` z `Discount` i `PaidAmount`, przypisany do właściciela karty, `RegisteredByUserId` = kasjer). Wyścig o ten sam paragon między klientami rozstrzyga `UX_FuelPurchases_Receipt` → ten sam `409`. Potem `MarkRedeemedAsync` (kupon `Active → Redeemed`).
9. Gdy punkty > 0: **saldo** jednym `INSERT ... ON CONFLICT (UserId) DO UPDATE SET Balance = Balance + EXCLUDED.Balance RETURNING Balance` i **wpis historii** z `BalanceAfter` z tego wyniku. Zakup za mniej niż 1 zł (0 pkt) zostaje zapisany bez zmiany salda i bez wpisu (wpis musi mieć `Delta <> 0`).
10. Odpowiedź `201 { purchaseId, pointsAwarded, discount, paidAmount }` - bez salda klienta; bez kuponu `discount` = 0, `paidAmount` = `amount`. Odtworzenie przez idempotencję zwraca treść zapisaną przy pierwszym wykonaniu: odpowiedź zapisana przed dodaniem tych pól ich nie ma.

Błędy domenowe (`404`, `409`) są zapisywane przez idempotencję: ponowienie z tym samym kluczem dostaje ten sam wynik, a częściowe zmiany są wycofane do savepointu.

## Dane

### `LoyaltyAccounts` - saldo

| Kolumna | Typ | Ograniczenia |
| --- | --- | --- |
| `UserId` | `text` | PK, FK `AspNetUsers` (RESTRICT) |
| `Balance` | `integer` | `CHECK (Balance >= 0)` |
| `CreatedAt` | `timestamptz` | domyślnie `now()` |
| `xmin` | systemowa | token współbieżności (optymistyczna blokada) |

Konto powstaje przy pierwszym naliczeniu. Brak wiersza oznacza saldo 0, więc moduł Accounts nie zależy od Loyalty.

### `FuelPurchases` - tankowania

| Kolumna | Typ | Ograniczenia |
| --- | --- | --- |
| `Id` | `uuid` | PK, nadawany przez aplikację |
| `UserId` | `text` | FK `AspNetUsers` (RESTRICT) |
| `StationCode`, `RegisterCode` | `varchar(32)` | `CHECK (~ '^[A-Z0-9-]+$')` |
| `SaleDate` | `date` | `CHECK (SaleDate > DATE '0001-01-01')` |
| `ReceiptNumber` | `varchar(64)` | `CHECK (~ '^[A-Z0-9/-]+$')` |
| `GrossAmount` | `numeric(10,2)` | `CHECK (> 0 AND <= 5000)` - kwota z paragonu; w API pole `amount` |
| `Discount` | `numeric(10,2)` | `CHECK (>= 0 AND <= GrossAmount)`; 0 bez kuponu |
| `PaidAmount` | `numeric(10,2)` | `CHECK (= GrossAmount - Discount)`; podstawa punktów |
| `Liters` | `numeric(10,3)` | `CHECK (Liters > 0 AND Liters <= 9999999.999)` |
| `PointsAwarded` | `integer` | `CHECK (>= 0)`, zapis z chwili naliczenia |
| `RegisteredByUserId` | `text` | FK `AspNetUsers` - wykonawca operacji (docelowo kasjer); sam FK nie sprawdza roli |
| `CreatedAt` | `timestamptz` | domyślnie `now()` |

**Identyfikacja paragonu:** `UNIQUE (StationCode, RegisterCode, SaleDate, ReceiptNumber)`. Sam numer paragonu nie jest unikalny, nawet na jednej stacji. Unikalność działa na postaci znormalizowanej, którą wymuszają `CHECK` formatu.

### `PointsLedgerEntries` - historia zmian

| Kolumna | Typ | Ograniczenia |
| --- | --- | --- |
| `Id` | `uuid` | PK |
| `UserId` | `text` | FK `AspNetUsers` (RESTRICT) |
| `Delta` | `integer` | `CHECK (Delta <> 0)`; znak zależny od typu: `FuelPurchase` > 0, `CouponExchange` < 0 |
| `Type` | `varchar(32)` | `CHECK (Type IN ('FuelPurchase', 'CouponExchange'))` |
| `SourceId` | `uuid` | id źródła, np. tankowania |
| `BalanceAfter` | `integer` | `CHECK (>= 0)` |
| `CreatedAt` | `timestamptz` | domyślnie `now()` - **początek transakcji**, nie moment zapisu |
| `Sequence` | `bigint` | `GENERATED ALWAYS AS IDENTITY`, `UNIQUE`, indeks `(UserId, Sequence)`; kolejność historii |

`UNIQUE (Type, SourceId)`: jedno źródło daje najwyżej jeden wpis.

`Sequence` jest nadawany przy wstawieniu wiersza, a wpis powstaje już po blokadzie klienta, więc kolejność `Sequence` jednego klienta to kolejność zapisów. `CreatedAt` może jej nie odpowiadać: transakcja, która zaczęła się wcześniej, mogła dostać blokadę później. Niezmiennik: **każdy zapis historii punktów odbywa się pod `CustomerLock` tego klienta**. Luki w numeracji po rollbackach są normalne.

Migracja `*_LedgerSequence` nadała numery istniejącym wpisom jawnie, według `(CreatedAt, Id)`, niczego nie kasując. To **przybliżenie**: dla wpisów sprzed migracji prawdziwa kolejność zapisu nie jest znana i migracja jej nie odtwarza. Baza dev i testowa nie miały wtedy wpisów.

### Niezmienniki

- Saldo nigdy nie jest ujemne (`CHECK`).
- Saldo równa się sumie `Delta` użytkownika. Pilnuje tego transakcja zapisu: zakup, saldo i wpis historii w jednym commicie.
- Historia jest tylko dopisywana: aplikacja nie aktualizuje ani nie usuwa wpisów.
- Punkty są zapisane przy tankowaniu, więc zmiana reguły nie zmienia historii.

## Karta lojalnościowa

Kod karty wskazuje odbiorcę punktów przy kasie. Nie zawiera danych osobowych i nie uwierzytelnia nikogo. Kody nie trafiają do logów.

| Endpoint | Działanie |
| --- | --- |
| `GET /api/me/loyalty-card` | odczyt aktywnej karty; brak → `404 loyalty.card_missing`. Niczego nie tworzy. |
| `POST /api/me/loyalty-card` | wydanie pierwszej karty → `201`; aktywna już istnieje → `409 loyalty.card_already_issued` (zapisywany) |
| `POST /api/me/loyalty-card/rotate` | wymiana: stara karta unieważniona w tym samym commicie, nowa aktywna → `200`; brak karty → `404 loyalty.card_missing` (zapisywany) |

- Tylko rola `User` (klient). Konta z samą rolą `Cashier` lub `Admin` dostają `403`.
- Obie mutacje: CSRF + `Idempotency-Key`. Ponowienie z tym samym kluczem zwraca tę samą kartę i niczego nie zmienia - także ponowienie wymiany nie unieważnia właśnie wydanej karty.
- Odtworzenie wydania **po** późniejszej wymianie zwraca pierwotną odpowiedź, czyli stary, już nieważny kod. Tak działa idempotencja (zwraca wynik tamtej operacji); aktualną kartę podaje `GET`.
- Po wydaniu lub wymianie frontend zawsze pobiera kartę przez `GET`. Odpowiedź mutacji może być odtworzona przez idempotencję i wtedy opisuje historyczną operację, nie aktualny stan.
- Mutacje kart jednego klienta są serializowane blokadą klienta (`CustomerLock`, niżej). Bez niej dwie równoległe wymiany odczytałyby tę samą aktywną kartę i jedna skończyłaby się `500` na indeksie unikalnym.

### Blokada klienta

`CustomerLock` (`Infrastructure/CustomerLock.cs`) to `pg_advisory_xact_lock` na kluczu `loyalty-customer:<UserId>`, trzymana do końca transakcji (commit albo rollback, także po rollbacku do savepointu).

- Czekanie ma limit `Loyalty:CustomerLockTimeout` (domyślnie 5 s, zakres 1 ms - 30 s sprawdzany przy starcie; `lock_timeout = 0` czekałoby bez końca) i reaguje na anulowanie żądania.
- Po limicie: `503 loyalty.customer_busy` z `Retry-After`. To 5xx, więc idempotencja go nie zapisuje - klient ponawia z tym samym kluczem.
- Wymaga otwartej transakcji (inaczej wyjątek); w endpointach daje ją idempotencja.
- Kolizja skrótu `hashtextextended` dla dwóch klientów oznacza tylko, że poczekają na siebie.

Tę samą blokadę bierze zakup przez kasę (blokada właściciela karty, potem ponowne sprawdzenie karty):

- wymiana zatwierdzona, zanim zakup dostał blokadę → nowe zapytanie widzi unieważnienie, stary kod odrzucony,
- zakup trzyma blokadę pierwszy → naliczenie na starej (jeszcze ważnej) karcie jest poprawne, wymiana czeka na koniec zakupu.

Blokada chroni też saldo przed równoległymi zakupami tego klienta; wiersz salda i tak blokuje `INSERT ... ON CONFLICT`.

Bierze ją też `PointsSpending` (`Points/PointsSpending.cs`) - jedyna droga, którą inne moduły pobierają punkty (wymiana na kupon w Rewards): blokada → `UPDATE ... WHERE "Balance" >= punkty` → wpis historii z ujemnym `Delta`, w transakcji wywołującego.

### `LoyaltyCards`

| Kolumna | Typ | Ograniczenia |
| --- | --- | --- |
| `Id` | `uuid` | PK |
| `UserId` | `text` | FK `AspNetUsers` (RESTRICT); `UNIQUE (UserId) WHERE RevokedAt IS NULL` - jedna aktywna karta |
| `Code` | `char(26)` | `UNIQUE` (także unieważnione - kod nie wraca), `CHECK (~ '^[0-7][0-9A-HJKMNP-TV-Z]{25}$')` |
| `IssuedAt` | `timestamptz` | |
| `RevokedAt` | `timestamptz NULL` | `CHECK (RevokedAt IS NULL OR RevokedAt >= IssuedAt)` |

Format kodu: 16 losowych bajtów (CSPRNG) jako 26 znaków Crockford Base32, pierwszy znak `0-7`. Kasa przyjmuje wpisany ręcznie wariant (`LoyaltyCardCode.Normalize`: myślniki, spacje, małe litery, `O`→`0`, `I`/`L`→`1`).

Kod jest też w treści zapisanej odpowiedzi w `IdempotencyRecords` (odtworzenie musi go zwrócić). Kod nie jest hasłem, ale identyfikuje klienta, więc ani kod, ani treść tej odpowiedzi nie trafia do logów. EF Core loguje SQL z maskowanymi parametrami (`EnableSensitiveDataLogging` jest wyłączone); test sprawdza to na wszystkich logach do poziomu Trace.

## Saldo i historia

| Endpoint | Działanie |
| --- | --- |
| `GET /api/me/wallet` | `200 { balance }`; brak konta lojalnościowego → `balance: 0` |
| `GET /api/me/transactions?limit=&cursor=` | `200 { items, nextCursor }`, od najnowszych |

Pozycja historii: `{ id, type, delta, balanceAfter, createdAt, fuelPurchase, couponExchange }`. `fuelPurchase` (`purchaseId`, `stationCode`, `saleDate`, `amount`, `liters`, `grossAmount`, `discount`, `paidAmount`; `amount` = `grossAmount`, zostawione dla zgodności) jest wypełnione tylko dla `FuelPurchase`, `couponExchange` (`couponId`, `offerName`) tylko dla `CouponExchange`; drugie pole jest `null`. Zakup i kupon pokazują się tylko wtedy, gdy należą do zalogowanego klienta - samo dopasowanie `SourceId` nie wystarcza. Kupon jest czytany z tabeli Rewards (wspólna baza).

:::note[Zakupy bez punktów]
Historia to zmiany punktów, nie lista zakupów. Zakup za mniej niż 1 zł (np. 0,99 zł, 0 pkt), także po rabacie z kuponu, nie ma wpisu w historii i **nie pojawia się** w `GET /api/me/transactions`. Saldo zawsze równa się sumie `delta` z historii. Osobna lista zakupów nie jest częścią MVP.
:::

Stronicowanie:

- `limit` domyślnie 20, zakres 1-100; inaczej `400 loyalty.validation_failed` z `errors.limit` = `loyalty.limit_out_of_range`.
- Sortowanie po `Sequence` malejąco. Następna strona: `Sequence < sequence ostatniej pozycji`, zawsze z filtrem zalogowanego klienta - kursor innej osoby nie odsłania cudzych danych.
- Zapytanie pobiera `limit + 1` wierszy; `nextCursor` jest ustawiony tylko, gdy istnieje kolejna strona.
- Kursor jest nieprzezroczysty (base64url). Niepoprawny → `errors.cursor` = `loyalty.invalid_cursor`.
- To nie jest migawka: wpisy dodane w trakcie przeglądania pojawią się po odświeżeniu pierwszej strony; kolejne strony ich nie powtórzą ani nie przesuną.

Dostęp: rola `User` wymagana. Konto, które ma dodatkowo `Cashier` lub `Admin`, nadal czyta własne dane; same role personelu dostają `403`. Odczyty `GET` nie wymagają CSRF.

## Frontend - ekran główny

`app/(app)/page.tsx` renderuje `HomeScreen` (`components/home/`), który łączy `features/auth` (role) i `features/wallet` - funkcje nie importują się nawzajem.

- Saldo: `useWallet` (`queryKeys.wallet`) → `GET /api/me/wallet`, liczba w formacie `pl-PL` (np. „12 500 pkt”; 4 cyfry bez spacji zgodnie z polską normą).
- Ładowanie: szkielet, `aria-busy`, tekst dla czytników ekranu.
- Błąd: brak sieci, sesja wygasła (link do logowania), inny błąd; przycisk „Spróbuj ponownie”. Komunikat pojawia się po ponowieniach z `Providers` (2 dodatkowe próby dla błędów sieci i 5xx).
- Saldo 0: zachęta do pierwszego tankowania z kartą.
- Odnośniki do `/karta`, `/historia`, `/oferty` i `/kupony`. Zasada: link pojawia się dopiero razem z ekranem - żaden aktywny odnośnik nie prowadzi do 404.
- Konto bez roli `User` (sam personel): bez zapytania o saldo (API dałoby `403`), krótki komunikat.

Stany ładowania i błędu salda sprawdzone ręcznie (telefon 390 px i desktop). E2E sprawdza odnośniki i saldo 0.

## Frontend - karta

`/karta` (`app/(app)/karta/page.tsx`) renderuje `LoyaltyCardPanel` z `features/loyalty-card/` (`services.ts` → `hooks.ts` → `components/`).

- Odczyt: `useLoyaltyCard` (`queryKeys.loyaltyCard`) → `GET /api/me/loyalty-card`; `404 loyalty.card_missing` to stan „brak karty” (`null`), nie błąd. Odczyt nigdy nie tworzy karty.
- Kod wyświetlany w grupach po 4 znaki; „Kopiuj kod” kopiuje surowy kod (26 znaków).
- Wydanie i wymiana: `useIdempotentMutation`. Odpowiedź mutacji **nie jest wyświetlana** - odtworzona odpowiedź opisuje pierwotną operację, a nie bieżący stan. Po każdej próbie (sukces lub błąd) karta jest pobierana ponownie przez `GET`; mutacja pozostaje w toku do końca tego odczytu.
- Wymiana wymaga potwierdzenia („Tak, wymień” / „Anuluj”). „Anuluj” jest tylko **przed wysłaniem**. Przy nieznanym wyniku nie ma czego anulować - serwer mógł już wymienić kod - więc zostaje wyłącznie „Ponów wymianę” z informacją, że ponowienie jest bezpieczne.
- Mutacje (`useIssueCard`, `useRotateCard`) są w `LoyaltyCardPanel`, nad zmiennymi widokami (`NoCard`, `ActiveCard`) (ładowanie, błąd, brak karty, karta). Klucz niezakończonej operacji przetrwa więc nieudany odczyt karty między próbami. Nieudane odświeżenie przy znanej karcie pokazuje ostrzeżenie „pokazany stan może być nieaktualny” zamiast zastępować widok; pełny ekran błędu jest tylko wtedy, gdy karta nie została jeszcze ani razu wczytana.
- Brak odpowiedzi (błąd sieci): komunikat, że wynik jest nieznany, i przycisk „Ponów”, który wysyła **ten sam** `Idempotency-Key`.

Klucz w `useIdempotentMutation` (`lib/idempotency.ts`):

| Wynik próby | Klucz |
| --- | --- |
| sukces | zwolniony - następna operacja dostaje nowy |
| brak odpowiedzi, `5xx`, `409 idempotency.in_progress` | **zachowany** - ponowienie używa tego samego |
| inny `4xx` (zapisany błąd domenowy, walidacja, `key_reused`) | zwolniony - odpowiedź jest ostateczna |
| inne dane wejściowe | nowy klucz |

Mutacje bez danych wejściowych (wydanie, wymiana) też dostają klucz - wcześniejsza wersja hooka nie nadawała go dla `undefined`.

Kod QR (`apps/web/components/code/CodeQr.tsx`, wspólny z kuponami):

- zawiera **wyłącznie surowy kod**: aktywnej karty (26 znaków) albo aktywnego kuponu (27 znaków, `K` + 26); tryb alfanumeryczny, wersja 2, korekcja M,
- liczony w przeglądarce biblioteką `qrcode` (`create` → macierz modułów → własny SVG, bez `innerHTML`); kod karty nie trafia do żadnego zewnętrznego serwisu,
- ciemne moduły na białym tle i strefa ciszy 4 modułów w każdym motywie,
- źródłem jest karta z `GET` - po wymianie QR zmienia się razem z tekstem.

Tekst kodu i „Kopiuj kod” zostają jako alternatywa (wpisanie ręczne, demo na jednym ekranie).

QR odczytuje czytnik w [panelu kasjera](#frontend---panel-kasjera).

## Frontend - historia

`/historia` renderuje `HistoryList` z `features/history/` (`services.ts` → `hooks.ts` → `components/`).

- `useHistory`: `useInfiniteQuery` (`queryKeys.transactions`), strona 20 wpisów, następna strona z `nextCursor`. „Pokaż więcej” znika, gdy `nextCursor` jest `null`.
- Wpis: typ („Tankowanie”, „Wymiana na kupon” z nazwą oferty), zmiana punktów ze znakiem, saldo po operacji, data i godzina zapisu (Europe/Warsaw). Dla tankowania: stacja, data sprzedaży, kwota brutto (zł), litry; przy kuponie dodatkowa linia „Rabat z kuponu X zł · zapłacono Y zł” (`grossAmount`, `discount`, `paidAmount`). Punkty liczone są od kwoty zapłaconej.
- Stany: szkielet przy ładowaniu; błąd pierwszej strony z „Spróbuj ponownie”; błąd kolejnej strony pod listą (wczytane wpisy zostają); pusta historia z odnośnikiem do karty.
- Pod listą informacja, że zakup za mniej niż 1 zł nie daje punktów i nie pojawia się w historii.

## Frontend - panel kasjera

`/kasa` renderuje `CashierPanel` z `features/cashier/` (`services.ts` → `hooks.ts` → `schemas.ts`, `components/`). Na ekranie głównym odnośnik „Kasa” widzi tylko konto z rolą `Cashier`. Konto bez stanowiska (np. klient) dostaje komunikat zamiast formularza - o dostępie i tak decyduje API.

- Zakup w toku (formularz, mutacja z kluczem, dane do ponowienia) to `usePurchaseFlow`, wywoływany w `CashierPanel` nad widokami stanowiska; `PurchaseForm` i `UnknownOutcome` dostają go z góry. Nieudane odświeżenie stanowiska (np. po powrocie do okna) pokazuje ostrzeżenie i nie usuwa formularza.
- Stanowisko: `GET /api/cashier/station`, tylko do odczytu. Formularz nie wysyła stacji ani kasy - API bierze je ze stanowiska zalogowanego kasjera.
- Pola: kod karty, kwota, litry, data sprzedaży (domyślnie dziś w Europe/Warsaw, bez przyszłych dat), numer paragonu, opcjonalny kod kuponu ([podgląd i odbiór kuponu](/moduly/rewards/#interfejs---kupon-w-kasie)). Puste pole kuponu = zakup bez kuponu (pole nie jest wysyłane). Kwota i litry przyjmują przecinek i są wysyłane jako **liczby JSON** (`"123,45"` → `123.45`); kod karty bez spacji, wielkimi literami.
- Wynik: „Zakup zapisany. Przyznano N pkt.” i linia z rabatem i kwotą do zapłaty (`discount`, `paidAmount` z odpowiedzi; przy 0 pkt dopisek o kwocie poniżej 1 zł); formularz i podglądy kuponów czyszczone dla następnego klienta. Kasjer nie widzi salda klienta.
- Błędy pól z API (`errors[field]`) trafiają pod pola; `card_not_found` pod kod karty, `receipt_already_registered` pod numer paragonu.
- **Nieznany wynik** (brak odpowiedzi, `5xx`, `in_progress`, `customer_busy`, `coupon_busy`): komunikat, formularz **zablokowany**, „Ponów zapis zakupu” wysyła te same dane z tym samym `Idempotency-Key`. „Odblokuj formularz…” wymaga potwierdzenia z ostrzeżeniem: zakup mógł już zostać zapisany, odblokowanie nie cofa zapisu, a **zmiana numeru paragonu omija ochronę przed duplikatem** (`UNIQUE` paragonu chroni tylko ten sam numer). Zalecane: najpierw ponowić albo ustalić wynik.

Czytnik QR (`components/QrScanner.tsx`):

- `getUserMedia` (tylna kamera) → klatki `<video>` na `<canvas>` → `jsQR`. Działa w każdej przeglądarce z kamerą (bez `BarcodeDetector`, którego Safari nie ma).
- Dwa czytniki: karty i kuponu. Każdy przyjmuje tylko swój format (`accept`): QR kuponu przy czytniku karty jest ignorowany i odwrotnie. Wynik trafia w pole i **nie zatwierdza zakupu**. Wpisywanie i wklejanie zostają dostępne.
- Kamera jest zatrzymywana (`track.stop()`) po skanie, przyciskiem „Zatrzymaj kamerę”, przy **zablokowaniu formularza** (zapis w toku lub nieznany wynik) i przy odmontowaniu (opuszczenie ekranu).
- Każde uruchomienie to próba z identyfikatorem; zatrzymanie ją unieważnia. Strumień, który przyjdzie po zatrzymaniu (zgoda udzielona później), jest od razu zatrzymywany, a spóźniony wynik skanu ignorowany.
- Komunikaty: brak zgody, brak kamery, kamera zajęta, brak HTTPS (`isSecureContext`). Kamera w przeglądarce działa tylko na `https://` albo `localhost` - test na telefonie: [HTTPS na telefonie](/instrukcje/kasjer-i-konta-demo/#kamera-na-telefonie-https).

## Testy E2E

`apps/web/e2e/` (`loyalty-card.spec.ts`, `history.spec.ts`, `session.spec.ts`, `cashier.spec.ts`; wymiana punktów: `rewards.spec.ts`, kupony: `coupons.spec.ts`, kupon w kasie: `cashier-coupon.spec.ts`, opis w [Rewards](/moduly/rewards/#testy)), projekty `mobile` (Pixel 7) i `desktop`; uruchomienie: [Uruchomienie testów](/instrukcje/testy/).

| Test | Sprawdza |
| --- | --- |
| wydanie, odczyt, wymiana | logowanie → brak karty → wydanie → kod po przeładowaniu (`GET`), **QR zdekodowany ze zrzutu ekranu (`jsQR`) równy kodowi** → wymiana z potwierdzeniem; pierwsza odpowiedź wymiany zostaje zerwana **po** zatwierdzeniu na serwerze; „Ponów wymianę” wysyła ten sam klucz, a wyświetlony kod i zdekodowany QR to kod z pierwszej (jedynej) wymiany, także po przeładowaniu; strona nie wysyła żądań poza `localhost` |
| utracona odpowiedź + nieudany odczyt | odpowiedź wymiany zerwana po zatwierdzeniu, kolejne `GET` karty też padają (po ponowieniach); po powrocie sieci „Ponów wymianę” wysyła ten sam klucz, kod i QR = wynik pierwszej wymiany. Brak „Anuluj” przy nieznanym wyniku. Na kodzie sprzed poprawki test padał (brak przycisku ponowienia) |
| kopiowanie | schowek zawiera surowy kod (tylko Chromium) |
| kasa: pełny przepływ | klient wydaje i pokazuje kartę → kasjer (osobne okno, logowanie formularzem) widzi stanowisko `E2E-01`, wkleja kod, `123,45` zł / `40,5` l → „Przyznano 123 pkt”; wysłany JSON ma `amount: 123.45`, `liters: 40.5` (liczby); klient po odświeżeniu: saldo 123 pkt, jeden wpis w historii z danymi tankowania |
| kasa: utracona odpowiedź | odpowiedź zerwana po zapisie → komunikat o nieznanym wyniku, pola zablokowane; „Odblokuj formularz…” pokazuje ostrzeżenie, „Wróć” zostawia blokadę → „Ponów” z tym samym kluczem → 123 pkt; saldo klienta 123 (nie 246), jeden wpis |
| kasa: skan QR | fałszywa kamera Chromium odtwarza plik `.y4m` z QR karty klienta (`e2e/camera.ts`) → pole kodu wypełnione, brak `POST`, kamera zatrzymana |
| kasa: opuszczenie ekranu | aktywna kamera, przejście na start → ścieżka zatrzymana |
| kasa: utracona odpowiedź + błąd stanowiska | po utraconej odpowiedzi odświeżenie stanowiska (powrót do okna, zegar przesunięty) pada → ostrzeżenie, formularz zostaje; ponowienie z tym samym kluczem, jeden wpis. Na kodzie, w którym błąd stanowiska zastępuje formularz, test pada |
| kasa: zatrzymanie w trakcie zgody | `getUserMedia` wstrzymane, „Zatrzymaj kamerę”, potem zgoda → spóźniony strumień zatrzymany, skaner nieaktywny |
| kasa: blokada formularza | skaner czeka na zgodę, kasjer zapisuje zakup; zgoda przychodzi w trakcie zapisu, kamera pokazuje QR innej karty → strumień zatrzymany, kod w polu bez zmian |
| kasa: brak zgody | `NotAllowedError` → komunikat, pole kodu nadal do wklejenia |
| kasa: konto klienta | brak odnośnika „Kasa”, `/kasa` pokazuje brak stanowiska | 
| anulowanie | brak żądania wymiany, kod bez zmian |
| ekran główny | cztery odnośniki (karta, historia, oferty, kupony), wszystkie prowadzą do istniejących stron (`200`), saldo 0 |
| historia: paginacja | 25 zakupów przez prawdziwy endpoint kasy; 20 wpisów, najnowszy z danymi tankowania i saldem po; „Pokaż więcej” → 25 wpisów, malejąco bez luk i duplikatów, przycisk znika |
| historia: izolacja | dwóch klientów w osobnych kontekstach przeglądarki widzi tylko własne wpisy |
| historia: pusta | także po zakupie za 0,99 zł (0 pkt) |
| historia: ładowanie i błąd | szkielet, błąd sieci po ponowieniach, odzyskanie po „Spróbuj ponownie” |
| sesja (`session.spec.ts`) | bez ciasteczka → `/login?next=/`; nieważne ciasteczko (`401`) → `/login`; awaria API (`503`, zerwane połączenie) → błąd z ponowieniem, bez wylogowania, po ponowieniu ekran główny |

Próby negatywne: hook nadający nowy klucz przy każdej próbie wywraca test wymiany (różne klucze); pokazywanie tylko ostatniej strony historii wywraca test paginacji (5 zamiast 25); QR liczony raz (bez zależności od kodu) wywraca test wymiany - po wymianie QR zawiera stary kod; kwota wysłana jako tekst wywraca test pełnego przepływu (API odrzuca `400`); skaner bez sprzątania przy odmontowaniu wywraca test opuszczenia ekranu.

## Kasjer i stanowisko

Konto kasjera ma rolę `Cashier` i dokładnie jedno stanowisko w `CashierStations`:

| Kolumna | Typ | Ograniczenia |
| --- | --- | --- |
| `UserId` | `text` | PK, FK `AspNetUsers` (RESTRICT) |
| `StationCode` | `varchar(32)` | `CHECK (~ '^[A-Z0-9-]+$')` |
| `RegisterCode` | `varchar(32)` | `CHECK (~ '^[A-Z0-9-]+$')` |

- Stanowisko pochodzi z konfiguracji (`BootstrapCashier:StationCode`, `RegisterCode`) albo z kont demo (`DEMO-01` / `K1`). Kody przechodzą tę samą normalizację i walidację co paragon.
- Ponowny start z inną konfiguracją aktualizuje stanowisko istniejącego kasjera.
- Kilku kasjerów może mieć tę samą kasę (zmiany). Stanowisko nie jest unikalne.
- `GET /api/cashier/station` zwraca stanowisko zalogowanego kasjera. Brak przypisania: `404 loyalty.cashier_station_missing`.
- Planowany zapis zakupu weźmie stację i kasę stąd, nie z formularza. Unikalność paragonu chroni przed duplikatem, nie przed celowym fałszowaniem pozostałych danych paragonu.

## Dwa osobne zabezpieczenia przed podwójnym naliczeniem

| Mechanizm | Chroni przed | Stan |
| --- | --- | --- |
| Unikalny paragon | ponownym zapisaniem tego samego zakupu, nawet innym żądaniem lub innym kluczem | zrealizowany |
| Idempotency-Key | skutkami ponowienia tego samego żądania (np. po błędzie sieci); zwraca pierwotny wynik | infrastruktura gotowa; endpointy Loyalty planowane |

## Zabezpieczenia

- Limit zakupów (z odbiorem kuponu) na kasjera: `RateLimits:PurchasePermitLimit` w `PurchaseWindow` (60/min), potem `429`.

- Reguły zakresu są w kodzie i w bazie (`CHECK`). Baza jest ostatnią linią obrony.
- `numeric(10,2)` w PostgreSQL **zaokrągla** wartość z większą liczbą miejsc, zamiast ją odrzucić. Precyzję sprawdza więc tylko `FuelPurchaseRules` i nie wolno jej pominąć.
- `xmin` wykrywa równoległą zmianę salda (`DbUpdateConcurrencyException`).
- Klucze obce `RESTRICT`: nie da się usunąć użytkownika z historią punktów.
- Odpowiedzi `/api/me/loyalty-card` po sprawdzeniu dostępu i CSRF (także błędy domenowe i odtworzone) mają `Cache-Control: no-store` - zawierają kod karty. Szczegóły: [Rewards - zabezpieczenia](/moduly/rewards/#zabezpieczenia).

:::caution[Planowane]
Niezmienność historii jest dziś regułą aplikacji. Wymuszenie w bazie (uprawnienia albo trigger blokujący `UPDATE`/`DELETE`) nie jest zrealizowane.
:::

## Testy

`apps/api.tests/`:

| Plik | Sprawdza |
| --- | --- |
| `LoyaltyRulesTests.cs` - `PointsPolicyTests` | `floor(zapłacono)` na brzegach (0 ... 5000; 0 → 0 pkt) i odrzucenie wartości ujemnych i powyżej 5000 |
| `LoyaltyRulesTests.cs` - `FuelPurchaseRulesTests` | zakres i precyzja kwoty i litrów (w tym 9 999 999,999 przyjęte, 10 000 000 odrzucone), zera na końcu, `default(DateOnly)`, normalizacja, odrzucenie postaci nieznormalizowanej i niedozwolonych znaków, wymagane części paragonu, długości |
| `CashierTests.cs` | `/api/cashier/station`: `401` anonim, `403` User i samo Admin, `200` kasjer; tworzenie kasjera tylko z rolą `Cashier` i znormalizowanym stanowiskiem; aktualizacja stanowiska; odmowa awansu konta User i Admin; odrzucenie złego stanowiska bez tworzenia konta; `BootstrapCashier` wszystko albo nic; konta demo tworzone tylko w Development (w Production seeder ich nie tworzy), idempotentnie, z właściwymi rolami |
| `DemoPasswordResetTests.cs` | reset haseł kont demo: zachowane ID, role i stanowisko; odmowa poza Development i bez hasła; hasło odrzucone przez Identity nie zmienia żadnego konta |
| `SimulationPasswordResetTests.cs` | reset haseł kont symulatora: to samo wyprowadzenie co symulator, nowy znacznik bezpieczeństwa, inne konta bez zmian; odmowa poza Development i bez sekretu (min. 16 znaków) |
| `LoyaltyRulesTests.cs` - `SaleDateRuleTests`, `StationClockTests` | dziś i przeszłość dozwolone, jutro odrzucone; granica doby w `Europe/Warsaw` zimą i latem (CET/CEST); nieznana strefa odrzucona przy starcie |
| `LoyaltyCardCodeTests.cs` | kody poprawne i różne; kodowanie wszystkich 128 bitów (zera, same jedynki, ostatni bit); normalizacja wpisanych wariantów; odrzucenie złej długości, pierwszego znaku `8`, znaków spoza alfabetu, małych liter, końcowego `\n` |
| `LoyaltyCardTests.cs` | `GET` nic nie tworzy; wydanie i odczyt; ponowienie wydania i wymiany z tym samym kluczem (jedna operacja); drugie wydanie innym kluczem → `409`; 6 równoległych wydań → jedna karta; 5 równoległych wymian różnymi kluczami → jedna aktywna; 5 równoległych wymian jednym kluczem → jedna wymiana; stara karta unieważniona; wymiana bez karty → zapisany `404`; odtworzenie wydania po wymianie; `no-store` na odczycie, `404`, wydaniu i odtworzeniu; izolacja kont (w tym ten sam klucz u dwóch osób); CSRF i klucz wymagane; `403` dla Cashier i Admin; `401` anonim; indeks jednej aktywnej karty, unikalny kod i format w bazie |
| `CustomerLockTests.cs` | zajęty klient → `503 loyalty.customer_busy` z `Retry-After`, nic nie zapisane, ponowienie tym samym kluczem się udaje; anulowanie czekania; blokada wymaga transakcji; kody kart i zapisane odpowiedzi nie trafiają do logów (Trace, także SQL); zakres `CustomerLockTimeout` i odmowa startu |
| `FuelPurchaseTests.cs` | naliczenie `floor(kwota)` na stanowisku kasjera (stacja i kasa znormalizowane, z formularza ignorowane), zakup, saldo i wpis z `BalanceAfter`; odpowiedź tylko `purchaseId` i `pointsAwarded`; kolejne zakupy się sumują; < 1 zł bez salda i historii; ponowienie tym samym kluczem raz; ten sam paragon innym kluczem (także małymi literami, dla innego klienta) → `409` bez danych właściciela; 6 równoległych zakupów z jednym paragonem → jedno naliczenie; 8 równoległych zakupów jednego klienta → pełna suma i spójny `BalanceAfter`; wpisany kod z myślnikami i małymi literami; kod unieważniony i nieznany → ten sam `404`; błędy pól niezapisywane (ten sam klucz po poprawce); kasjer bez stanowiska; `403` dla User, Admin i klienta na własnej karcie; CSRF i klucz; izolacja klientów; zajęty klient → `503` z `Retry-After: 1` przez filtr idempotencji, bez zapisu, ponowienie się udaje; **wyścig**: wymiana zatwierdzona pierwsza → stary kod odrzucony (zapisany `404`), zakup z blokadą pierwszy → naliczenie, potem wymiana i stary kod odrzucony; data sprzedaży względem doby w Warszawie (23:30 i 00:30 czasu stacji przy innej dacie UTC), daty przeszłe dozwolone; kod karty z żądania nie trafia do logów; kwota jako tekst (`"10"`) → `400` |
| `WalletTests.cs` | saldo 0 bez konta; saldo = suma `delta` = `balanceAfter` najnowszego wpisu; zakup za 0,99 zł nieobecny w historii; opis powiązanego zakupu; strony bez duplikatów i luk (45/20, 10/5, 3/100), brak `nextCursor` na ostatniej pełnej stronie, domyślny limit 20; wpis dodany w trakcie przeglądania widoczny dopiero po odświeżeniu; zły `limit` i kursor → błędy pól; **kolejność po `Sequence`, gdy transakcja A zaczęła się przed B, ale zapisała po niej** (wymuszone bramką; test sprawdza, że `CreatedAt` A < B); izolacja, także z cudzym kursorem; cudzy zakup pod własnym wpisem → `fuelPurchase: null`; `403` dla samych ról personelu, dostęp dla User + Cashier/Admin; `401` anonim |
| `LoyaltyModelConstraintsTests.cs` | `CHECK` dla kwoty, litrów, daty, formatu paragonu, salda i `Delta`; przepełnienie `numeric(10,3)`; unikalność paragonu i źródła wpisu; paragony różniące się spacjami lub wielkością liter kolidują po normalizacji; ten sam numer na innej kasie, stacji lub w inny dzień; wykrycie równoległej zmiany salda |

Testy ograniczeń zapisują dane bezpośrednio przez EF Core, z pominięciem walidacji, żeby udowodnić, że baza odrzuca zły stan sama.

Kolejność w testach wyścigu jest wymuszona, nie zależy od kolejki blokad PostgreSQL:

- „wymiana pierwsza”: test trzyma blokadę klienta w osobnym połączeniu, czeka (przez `pg_locks`), aż zakup na nią czeka, unieważnia kartę i zatwierdza,
- „zakup pierwszy”: `CustomerLockGate` (interceptor EF tylko w hoście testowym) zatrzymuje zakup tuż po zdobyciu blokady; dopiero wtedy startuje wymiana, test sprawdza, że czeka, i zwalnia zakup.

Data „dziś” pochodzi z `TimeProvider`; host testowy podstawia `MutableTimeProvider`, więc testy dat nie zależą od rzeczywistego dnia.
