---
title: Kody błędów
description: Format ProblemDetails i znane kody.
sidebar:
  order: 3
---

Błędy domenowe mają format ProblemDetails z dwoma rozszerzeniami. Kształt buduje w jednym miejscu `Infrastructure/ApiProblems.cs` (moduły podają tylko kod i tytuł; błędy pól implementują `IFieldError`):

```json
{
  "status": 400,
  "title": "Registration failed",
  "code": "accounts.registration_failed",
  "errors": {
    "email": [{ "field": "email", "code": "accounts.email_already_exists" }]
  }
}
```

- `code` - kod całej operacji, stabilny, do tłumaczenia w UI.
- `errors` - błędy pól, klucz to nazwa pola z żądania.

Komunikaty po polsku mapuje frontend (`apps/web/features/auth/schemas.ts`). API nie zwraca tekstów dla użytkownika.

## Accounts

| Kod | Pole | Kiedy |
| --- | --- | --- |
| `accounts.required_fields` | `email`, `password`, `displayName` | puste pole |
| `accounts.invalid_credentials` | - | zły e-mail lub hasło, także przy blokadzie konta |
| `accounts.registration_failed` | - | kod operacji przy nieudanej rejestracji |
| `accounts.invalid_email` | `email` | niepoprawny adres |
| `accounts.email_already_exists` | `email` | adres zajęty |
| `accounts.password_too_short` | `password` | mniej niż 10 znaków |
| `accounts.password_requires_digit` | `password` | brak cyfry |
| `accounts.password_requires_lowercase` | `password` | brak małej litery |
| `accounts.password_requires_uppercase` | `password` | brak wielkiej litery |
| `accounts.password_requires_non_alphanumeric` | `password` | brak znaku specjalnego |
| `accounts.password_requires_unique_chars` | `password` | za mało różnych znaków |
| `accounts.invalid_registration` | `registration` | inny błąd Identity |

## Loyalty

Kody walidacji tankowania (`FuelPurchaseRules`). Zwróci je planowany endpoint tankowania.

| Kod | Pole | Kiedy |
| --- | --- | --- |
| `loyalty.required_field` | `stationCode`, `registerCode`, `receiptNumber`, `saleDate` | puste pole; data `0001-01-01` |
| `loyalty.too_long` | `stationCode`, `registerCode`, `receiptNumber` | ponad 32 znaki (stacja, kasa) lub 64 (paragon) po przycięciu |
| `loyalty.invalid_format` | jw. | niedozwolone znaki albo postać nieznormalizowana (spacje, małe litery) |
| `loyalty.amount_out_of_range` | `amount` | kwota `<= 0` lub `> 5000` |
| `loyalty.amount_precision` | `amount` | więcej niż 2 miejsca po przecinku |
| `loyalty.liters_out_of_range` | `liters` | litry `<= 0` lub `> 9 999 999,999` |
| `loyalty.liters_precision` | `liters` | więcej niż 3 miejsca po przecinku |
| `loyalty.sale_date_in_future` | `saleDate` | data sprzedaży późniejsza niż dziś w strefie stacji |
| `loyalty.cashier_station_missing` | - | `404`: konto kasjera bez przypisanego stanowiska |
| `loyalty.card_missing` | - | `404`: użytkownik nie ma aktywnej karty (odczyt, wymiana) |
| `loyalty.validation_failed` | - | `400`: błędy pól w `errors` (zakup: kody `loyalty.*` wyżej, także `cardCode`; historia: `limit`, `cursor`) |
| `loyalty.limit_out_of_range` | `limit` | `limit` historii poza zakresem 1-100 |
| `loyalty.invalid_cursor` | `cursor` | kursor historii niepoprawny |
| `rewards.validation_failed` | - | `400`: błędy pól oferty w `errors` |
| `rewards.required_field` | `name`, `pointsCost`, `discountPerLiter`, `maxLiters`, `isActive`, `version` | brak wartości (brak liczby to `required_field`, nie „poza zakresem”) |
| `rewards.too_long` | `name`, `description` | maks. 100 / 500 znaków |
| `rewards.points_cost_out_of_range` | `pointsCost` | 1 - 1 000 000 |
| `rewards.discount_per_liter_out_of_range` | `discountPerLiter` | `0 < stawka ≤ 10` zł/l |
| `rewards.discount_per_liter_precision` | `discountPerLiter` | maks. 2 miejsca po przecinku |
| `rewards.max_liters_out_of_range` | `maxLiters` | `0 < limit ≤ 1000` l |
| `rewards.max_liters_precision` | `maxLiters` | maks. 3 miejsca po przecinku |
| `rewards.version_out_of_range` | `version` | wersja `< 1` |
| `rewards.offer_not_found` | - | `404`: brak oferty |
| `rewards.offer_version_conflict` | - | `409`: wersja z formularza nie jest aktualna; nic nie zapisano |
| `rewards.offer_inactive` | - | `409`: oferta wyłączona; nic nie pobrano |
| `rewards.offer_busy` | - | `503` + `Retry-After`: edycja oferty trwa dłużej niż `Rewards:OfferLockTimeout`; ponów z tym samym kluczem (niezapisywany) |
| `rewards.offer_changed` | - | `409`: oferta zmieniła się od wyświetlenia (inna `version`); nic nie pobrano, pokaż nowe warunki |
| `rewards.insufficient_points` | - | `409`: saldo mniejsze niż koszt (także brak konta); nic nie pobrano |
| `rewards.coupon_not_found` | - | `404`: brak kuponu albo cudzy kupon |
| `rewards.coupon_redeemed` | - | `409`: kupon już odebrany, aktywacja niemożliwa |
| `loyalty.card_not_found` | - | `404`: kod karty nieznany albo unieważniony - celowo bez rozróżnienia |
| `loyalty.receipt_already_registered` | - | `409`: paragon (stacja, kasa, data, numer) już zapisany; bez informacji o właścicielu |
| `loyalty.customer_busy` | - | `503` + `Retry-After`: inna operacja na tym kliencie trwa dłużej niż `Loyalty:CustomerLockTimeout`; ponów z tym samym kluczem (niezapisywany) |
| `loyalty.card_already_issued` | - | `409`: wydanie karty, gdy aktywna już istnieje (aktualną podaje `GET`) |

## Idempotency

Dotyczą endpointów oznaczonych `RequireIdempotency()`. Zobacz [idempotencja](/wyjasnienia/idempotencja/).

| Kod | Status | Kiedy | Zapisywany |
| --- | --- | --- | --- |
| `idempotency.key_required` | `400` | brak nagłówka `Idempotency-Key` | nie |
| `idempotency.key_invalid` | `400` | klucz nie jest UUID w postaci `xxxxxxxx-xxxx-...` albo podano kilka nagłówków | nie |
| `idempotency.invalid_json` | `400` | treść `application/json` nie jest poprawnym JSON | nie |
| `idempotency.duplicate_json_property` | `400` | powtórzona nazwa pola w obiekcie JSON (na dowolnym poziomie, bez względu na wielkość liter) | nie |
| `idempotency.key_reused` | `409` | ten sam klucz użyty wcześniej dla innego żądania (metoda, trasa, wartości trasy, query lub treść) | nie |
| `idempotency.in_progress` | `409` + `Retry-After` | żądanie z tym kluczem wciąż trwa i nie skończyło się w `Idempotency:LockTimeout`; ponów z tym samym kluczem | nie |

## Ogólne

| Kod | Status | Kiedy |
| --- | --- | --- |
| `csrf.invalid` | `403` | brak lub nieważny token CSRF przy metodzie modyfikującej |
