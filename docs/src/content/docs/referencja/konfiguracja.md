---
title: Konfiguracja
description: Klucze konfiguracji API i frontendu.
sidebar:
  order: 4
---

## API

Źródła: `appsettings*.json`, user-secrets (Development), zmienne środowiskowe (`:` zastępujemy `__`).

| Klucz | Wymagany | Opis |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | tak | PostgreSQL. Bez niego API nie startuje. |
| `BootstrapAdmin:Email` | nie | e-mail administratora startowego, w parze z hasłem |
| `BootstrapAdmin:Password` | nie | hasło administratora startowego |
| `BootstrapCashier:Email`, `Password`, `StationCode`, `RegisterCode` | nie | kasjer startowy ze stanowiskiem; wszystkie cztery albo żaden |
| `DemoAccounts:Enabled` | nie | `true` włącza tworzenie kont demo; ustawiane w user-secrets; tworzy konta tylko w środowisku Development |
| `DemoAccounts:Password` | gdy `Enabled` | wspólne hasło kont demo, tylko w user-secrets; bez niego API przy `Enabled=true` nie startuje |
| `Idempotency:LockTimeout` | nie | `00:00:05`, zakres 1 ms - 30 s (inaczej API nie startuje); jak długo żądanie czeka na równoległe żądanie z tym samym kluczem, potem `409 idempotency.in_progress` |
| `Loyalty:CustomerLockTimeout` | nie | `00:00:05`, zakres 1 ms - 30 s (inaczej API nie startuje); jak długo operacja czeka na blokadę klienta, potem `503 loyalty.customer_busy` |
| `Loyalty:StationTimeZone` | nie | `Europe/Warsaw`; strefa IANA stacji, względem której liczona jest data „dziś” dla daty sprzedaży; nieznana strefa zatrzymuje start |
| `Rewards:OfferLockTimeout` | nie | `00:00:05`, zakres 1 ms - 30 s (inaczej API nie startuje); jak długo wymiana punktów czeka na trwającą edycję oferty, potem `503 rewards.offer_busy` |
| `Rewards:CouponLockTimeout` | nie | `00:00:05`, zakres 1 ms - 30 s (inaczej API nie startuje); jak długo zakup z kuponem czeka na zablokowany wiersz kuponu, potem `503 rewards.coupon_busy` |
| `Rewards:PreviewPermitLimit` | nie | `30`, zakres 1 - 1000 (inaczej API nie startuje); ile podglądów kuponu kasjer może zrobić w oknie, potem `429 rate_limit.exceeded` |
| `Rewards:PreviewWindow` | nie | `00:01:00`, zakres 1 s - 1 h; długość stałego okna limitu podglądu |
| `Assistant:MaxMessageLength` | nie | `1000` (1 - 10 000); maks. długość wiadomości czatu |
| `Assistant:MaxHistoryItems` / `MaxHistoryTextLength` | nie | `20` (0 - 100) / `2000` (1 - 20 000); historia w żądaniu |
| `Assistant:MaxToolCalls` | nie | `4` (1 - 20); wywołań narzędzi w jednej turze, potem `error assistant.tool_limit` |
| `Assistant:TurnTimeout` / `ToolTimeout` | nie | `00:00:30` (do 5 min) / `00:00:05` (≤ `TurnTimeout`) |
| `Assistant:HeartbeatInterval` | nie | `00:00:15` (do 1 min); komentarz `: ping` w strumieniu |
| `Assistant:ConfirmationLifetime` | nie | `00:10:00` (30 s - 1 h); ważność tokenu potwierdzenia aktywacji |
| `Assistant:ChatPermitLimit` / `ChatWindow` | nie | `20` / `00:01:00`; tur czatu na użytkownika w oknie, potem `429` |
| `ASPNETCORE_ENVIRONMENT` | nie | `Development` włącza OpenAPI i `/health/db` |

## Frontend

| Zmienna | Domyślnie | Opis |
| --- | --- | --- |
| `API_INTERNAL_URL` | `http://localhost:5271` | prywatny adres API dla rewrite i Server Components. Czytany przy buildzie. |
| `OPENAPI_URL` | `http://localhost:5271/openapi/v1.json` | źródło dla `npm run api:types` |

## Testy

| Zmienna | Opis |
| --- | --- |
| `FUELCLUB_TEST_DATABASE` | connection string do bazy, która musi nazywać się `fuelclub_test` |

## Porty lokalne

| Usługa | Port |
| --- | --- |
| Frontend | 3000 |
| API | 5271 (http), 7029 (https) |
| PostgreSQL dev | 5433 |
| PostgreSQL test | 5434 |

Sekrety nigdy nie trafiają do repozytorium ani do logów.
