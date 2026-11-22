---
title: Baza danych
description: Tabele i migracje.
sidebar:
  order: 5
---

Jeden `AppDbContext` (EF Core + Npgsql), dziedziczy po `IdentityDbContext<ApplicationUser>`.

## Migracje

| Migracja | Zawartość |
| --- | --- |
| `20261121113851_InitialIdentity` | tabele ASP.NET Core Identity |
| `*_LoyaltyModel` | `LoyaltyAccounts`, `FuelPurchases`, `PointsLedgerEntries` |
| `*_LoyaltyReceiptNormalization` | limit litrów, data sprzedaży, format (postać znormalizowana) części paragonu |
| `*_CashierStations` | `CashierStations` - stanowisko kasjera |
| `*_LedgerSequence` | `PointsLedgerEntries.Sequence` (identity, `UNIQUE`, indeks `(UserId, Sequence)`); jawne uzupełnienie istniejących wpisów według `(CreatedAt, Id)` - przybliżenie |
| `*_RewardsModel` | `RewardOffers`, `Coupons`; `FuelPurchases.Amount` → `GrossAmount` (zmiana nazwy, dane zachowane), nowe `Discount` i `PaidAmount` uzupełnione jawnie (`0`, `GrossAmount`); typ `CouponExchange` i znak `Delta` w historii punktów |
| `*_RewardOfferVersion` | `RewardOffers.Version` (`integer`, `CHECK >= 1`) zamiast `xmin`; istniejące oferty dostają `1`, bez wartości domyślnej |
| `*_LoyaltyCards` | `LoyaltyCards` - karty, jedna aktywna na użytkownika |
| `*_IdempotencyRecords` | `IdempotencyRecords` - klucze idempotencji i zapisane odpowiedzi |
| `*_CouponRedeemedPurchaseOwner` | złożony FK `Coupons (RedeemedPurchaseId, UserId)` → `FuelPurchases (Id, UserId)` zamiast FK po samym `Id`, `UNIQUE (Id, UserId)` w `FuelPurchases`; najpierw sprawdza dane - kupon odebrany na cudzy zakup zatrzymuje migrację, bez przepisywania |
| `*_AssistantTurns` | `assistant_turns` - metadane tur czatu ([ADR 0008](/decyzje/0008-slady-i-evale-czatu/)) |
| `*_Observability` | `security_events`; `assistant_turns.ReviewedAt`, `ReviewedBy` ([ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/)) |

## Tabele

| Tabela | Zawartość |
| --- | --- |
| `AspNetUsers` | konta. Poza polami Identity: `DisplayName`. Hasło jako hash. |
| `AspNetRoles` | role `User`, `Admin`, `Cashier` (tworzone przy starcie) |
| `AspNetUserRoles` | przypisanie ról |
| `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` | standard Identity, obecnie nieużywane |

`UserName` = e-mail. Unikalność e-maila wymusza Identity (`RequireUniqueEmail`).

## Loyalty i Rewards

| Tabela | Zawartość |
| --- | --- |
| `LoyaltyAccounts` | saldo użytkownika, `CHECK (Balance >= 0)`, `xmin` jako token współbieżności |
| `FuelPurchases` | tankowania; `UNIQUE (StationCode, RegisterCode, SaleDate, ReceiptNumber)`; `GrossAmount`, `Discount`, `PaidAmount` |
| `RewardOffers` | oferty (rabat na litr); bez usuwania, tylko wyłączanie - [Rewards](/moduly/rewards/) |
| `Coupons` | kupony klientów ze snapshotem oferty; `CHECK` stanu, `UNIQUE (Code)`, `UNIQUE (RedeemedPurchaseId)`; odbiór tylko na zakup właściciela (złożony FK) - [Rewards](/moduly/rewards/#dane) |
| `PointsLedgerEntries` | historia zmian salda, tylko dopisywana; `UNIQUE (Type, SourceId)`; kolejność po `Sequence` |
| `LoyaltyCards` | karty; `UNIQUE (Code)`, `UNIQUE (UserId) WHERE RevokedAt IS NULL` |
| `assistant_turns` | metadane tury czatu, bez treści: `CorrelationId` (`UNIQUE`), `UserPseudonym` (HMAC), dostawca, model, wersja promptu, `Demo`, `StartedAt`, `FirstEventMs`, `TotalMs` (`CHECK >= 0`), `Outcome` (`Done`/`Error`/`Cancelled`), `ErrorCode`, `ToolCalls` (`CHECK >= 0`), `Tools` (`jsonb`: nazwa, stan, czas), `InputTokens`/`OutputTokens` (`null` = nieznane). Indeksy `StartedAt` i `(UserPseudonym, StartedAt)`. `ReviewedAt`, `ReviewedBy` (kolejka przeglądu). Bez FK do użytkownika; retencja `Observability:TraceRetention` - [Assistant](/moduly/assistant/#dane) |
| `security_events` | odrzucone żądania: `OccurredAt`, `Kind` (`AccessDenied`, `LoginFailed`, `IdempotencyConflict`, `RepeatedRedemption`, `RateLimited`), `Status`, `Code`, `Route` (wzorzec trasy), `UserPseudonym`. Bez IP i treści. Indeksy `(Kind, OccurredAt)`, `OccurredAt`; retencja `Observability:SecurityEventRetention` - [Observability](/moduly/observability/) |
| `IdempotencyRecords` | PK `(UserId, IdempotencyKey)`; `Fingerprint bytea` (`CHECK` 32 bajty), `StatusCode` (`CHECK 200-499`), `ContentType`, `Location`, `Body bytea`; FK `AspNetUsers` RESTRICT. Bez retencji w MVP. Szczegóły: [idempotencja](/wyjasnienia/idempotencja/) |

Kolumny i ograniczenia: [moduł Loyalty](/moduly/loyalty/#dane), [moduł Rewards](/moduly/rewards/#dane). Tabele odwołują się do `AspNetUsers` z `ON DELETE RESTRICT`.

:::caution[Planowane]
`ChatTrace` (czat) - opis w `PLAN-MVP.md`, sekcja „Dane i niezmienniki”.
:::
