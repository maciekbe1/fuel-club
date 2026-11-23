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
| `BootstrapSupport:Email` / `Password` | nie | konsultant startowy (rola `Support`), oba albo żaden; sekrety: env / user-secrets. Tworzy tylko nowe konto - istniejące z tym e-mailem nie dostaje roli (ostrzeżenie w logu z id konta, bez e-maila) |
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
| `Assistant:ChatPermitLimit` / `ChatWindow` | nie | `20` / `00:01:00`; tur czatu na użytkownika w oknie, potem `429`. Start rozmowy ma osobny limit o tych samych liczbach |
| `Assistant:ConversationIdleTimeout` | nie | `00:30:00` (1 min - 1 dzień); rozmowa bez tury tak długo kończy się jako `Idle` ([ADR 0017](/decyzje/0017-ruch-syntetyczny-i-rozmowy/)) |
| `Assistant:Provider` | nie | `demo` (domyślnie, bez sieci) albo `gemini` ([ADR 0011](/decyzje/0011-adapter-gemini/)) |
| `Assistant:Gemini:ApiKey` | przy `gemini` | klucz Gemini API - sekret: user-secrets / `Assistant__Gemini__ApiKey` |
| `Assistant:Gemini:Model` / `RequestTimeout` | nie | `gemini-3.5-flash-lite` / `00:00:20` (≤ `TurnTimeout`; łącznie z ponowieniami, [ADR 0011](/decyzje/0011-adapter-gemini/)) |
| `Assistant:Gemini:Endpoint` | nie | `https://generativelanguage.googleapis.com/v1beta/` - musi kończyć się `/` |
| `Assistant:PseudonymKey` | poza `Development` tak (inaczej API nie startuje) | sekret (env / user-secrets), base64 ≥ 32 bajty; klucz HMAC pseudonimów w śladach. W `Development` bez niego: losowy na czas procesu |
| `Assistant:TraceSaveTimeout` | nie | `00:00:02` (do 30 s); jak długo koniec tury czeka na zapis śladu |
| `DataProtection:Certificate` / `CertificatePassword` | poza Development | base64 PKCS#12 szyfrujący klucze Data Protection w bazie; bez niego start poza Development się nie powiedzie ([ADR 0010](/decyzje/0010-obrazy-i-hosting/)) |
| `ForwardedHeaders:KnownProxies` / `KnownNetworks` | nie | adresy IP / zakresy CIDR proxy, którym API wierzy w `X-Forwarded-For` i `X-Forwarded-Proto` (poza loopback). Na hostingu zakres sieci prywatnej z Nextem ([ADR 0010](/decyzje/0010-obrazy-i-hosting/)). Adres klienta służy limitom logowania - [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/#limity) |
| `Observability:MaxSecurityEventsPerMinute` | nie | `600`; więcej zdarzeń bezpieczeństwa w minucie jest tylko liczonych w logu |
| `RateLimits:AuthPermitLimit` / `AuthWindow` | nie | `10` / `00:01:00`; próby logowania i rejestracji na adres IP, potem `429` (blokada konta Identity działa niezależnie) |
| `RateLimits:PurchasePermitLimit` / `PurchaseWindow` | nie | `60` / `00:01:00`; zakupy (z odbiorem kuponu) na kasjera, potem `429` |
| `Observability:TraceRetention` / `SecurityEventRetention` | nie | `30.00:00:00` (1 h - 365 dni); po tym czasie worker usuwa ślady tur i zdarzenia bezpieczeństwa |
| `Observability:PurgeInterval` / `MonitorInterval` | nie | `01:00:00` / `00:01:00`; jak często działa retencja i ocena reguł |
| `Observability:WorkerEnabled` | nie | `true`; testy wyłączają i wywołują retencję i reguły same |
| `Observability:RepeatedErrorsPerUser` / `ReviewQueueLimit` | nie | `3` / `50`; od ilu błędów pseudonim dostaje powód `repeated_errors`; długość kolejki |
| `Observability:Rules:<reguła>:Enabled`, `Threshold`, `Window`, `MinSample`, `Cooldown`, `Severity` | nie | reguły i domyślne wartości: [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/#reguły-anomalii); nieznana reguła lub zły zakres zatrzymuje start |
| `Support:WaitingExpiry` / `AssignedIdleTimeout` / `Retention` | nie | `7.00:00:00` / `1.00:00:00` / `30.00:00:00`; zgłoszenie czekające bez aktywności wygasa; przejęte bez wiadomości wraca do kolejki; zamknięte i wygasłe są usuwane z wiadomościami ([Support](/moduly/support/)) |
| `Support:CasePermitLimit` / `CaseWindow` | nie | `5` / `01:00:00`; zgłoszeń na klienta w oknie, potem `429` |
| `Support:MessagePermitLimit` / `MessageWindow` | nie | `30` / `00:01:00`; wiadomości na użytkownika (klient i konsultant), potem `429` |
| `Support:HubAllowedOrigins` | dla huba | originy, z których przeglądarka może połączyć się z hubem SignalR (np. `https://app.example`); lokalnie w `appsettings.Development.json` (`:3000`, `:3100`); każdy wpis `scheme://host[:port]` bez ścieżki i ukośnika (inaczej API nie startuje); pusto = hub niedostępny |
| `Support:WorkerEnabled` / `WorkerInterval` | nie | `true` / `00:01:00` (1 s - 1 h); testy wyłączają worker i wywołują porządki same |
| `Telemetry:OtlpEndpoint` | nie | adres OTLP/HTTP (np. `http://localhost:6006/v1/traces` dla Phoenix); pusty = bez eksportu OpenTelemetry. Nagłówki eksportera tylko z `OTEL_EXPORTER_OTLP_HEADERS` |
| `Simulation:Enabled` / `RunId` | nie | instancja ruchu syntetycznego ([ADR 0017](/decyzje/0017-ruch-syntetyczny-i-rozmowy/)): tylko `Development` (inaczej API nie startuje), `RunId` 1-40 znaków `a-z0-9-` wymagany; włącza `GET /api/simulation`. Ustawiane w środowisku instancji demo, nie przez żądanie - [Symulacja ruchu](/instrukcje/symulacja-ruchu/) |
| `ASPNETCORE_ENVIRONMENT` | nie | `Development` włącza OpenAPI i `/health/db` |

## Frontend

| Zmienna | Domyślnie | Opis |
| --- | --- | --- |
| `TRUST_FORWARDED_FOR` | brak | `true` tylko za proxy hostingu, które ustawia `X-Forwarded-For` i `X-Forwarded-Proto`; inaczej `proxy.ts` usuwa oba nagłówki z żądań `/api/*` ([ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/#limity)) |
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
| PostgreSQL demo (symulacja) | 5435 |
| API instancji demo | 5301 |

Sekrety nigdy nie trafiają do repozytorium ani do logów.
