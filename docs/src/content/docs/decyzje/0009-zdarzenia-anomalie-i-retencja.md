---
title: "0009 - Zdarzenia bezpieczeństwa, anomalie i retencja"
description: Rejestr odrzuconych żądań, konfigurowalne reguły anomalii, kolejka przeglądu tur, retencja śladów i uzupełnione limity.
sidebar:
  order: 9
---

## Status

Decyzja przyjęta (2026-11-22), zrealizowana w P7b. Ocena tur przez judge: P7c.

## Problem

Ślady tur ([ADR 0008](/decyzje/0008-slady-i-evale-czatu/)) mówią, co zrobił asystent. SPEC §3 wymaga więcej:

- metryk całego ruchu;
- reguł anomalii dla odmów dostępu, konfliktów kluczy, powtarzanych odbiorów, limitów narzędzi i błędów AI;
- kolejki podejrzanych tur dla admina;
- jawnej retencji;
- przeglądu limitów logowania, realizacji i czatu.

Wszystko to bez treści rozmów, kodów, adresów IP i cudzych danych osobowych.

## Rozwiązanie

### Zdarzenia bezpieczeństwa

- `SecurityEventsMiddleware` jest pierwszy w potoku. Po wysłaniu odpowiedzi (`OnCompleted`, własny zakres DI, limit 2 s) zapisuje do `security_events` odrzucone żądania. Klient na to nie czeka.
- Rodzaje i źródła:

| Rodzaj | Kiedy |
| --- | --- |
| `AccessDenied` | każde `403` (rola, CSRF, personel na danych klienta) |
| `LoginFailed` | `401` z `/api/auth/login` |
| `IdempotencyConflict` | `409 idempotency.key_reused` |
| `RepeatedRedemption` | `409 rewards.coupon_redeemed` (aktywacja, podgląd, zakup) |
| `RateLimited` | każde `429` |

- Anonimowe `401` w innych miejscach (np. `/api/auth/me` przed zalogowaniem) to normalna odpowiedź i nie jest zapisywane.
- Zapisywane są: czas, rodzaj, status, kod błędu, **wzorzec trasy** (`/api/me/coupons/{id:guid}/activate`, bez identyfikatorów z URL) i pseudonim zalogowanego. Bez IP, nagłówków i treści.
- Kod błędu: własny `IProblemDetailsService` (`ProblemCodeCapture`) zapisuje `code` z ProblemDetails do `HttpContext.Items`. Bez `AddProblemDetails` (jak teraz) odmawia zapisu, więc `ProblemHttpResult` pisze odpowiedź tak jak wcześniej - bez zmiany kształtu (pełny zestaw testów API bez zmian). Gdy ktoś doda `AddProblemDetails`, zarejestrowane writery piszą dalej jak w usłudze frameworka.
- Logowanie rozpoznaje **dopasowana trasa**, nie surowa ścieżka - `/api/auth/login/` też jest liczone.
- Limit zapisu: najwyżej `MaxSecurityEventsPerMinute` (600) wierszy na minutę. Nadmiar jest tylko liczony w logu, więc zalew odrzuconych żądań nie zamienia się w zalew INSERT-ów. Limit jest dużo wyższy od progów reguł, więc atak nadal je przekracza.
- Odtworzona odpowiedź idempotencji (zapisane bajty) nie przechodzi przez ProblemDetails, więc jedno zdarzenie nie liczy się dwa razy.

### Reguły anomalii

- Osiem reguł o stałych nazwach. Próg, okno, minimalna próbka, czas wyciszenia i waga są w `Observability:Rules:<reguła>`; nieznana reguła lub zły zakres zatrzymuje start.

| Reguła | Wartość | Domyślnie |
| --- | --- | --- |
| `access_denied`, `login_failed`, `rate_limited` | liczba zdarzeń | ≥ 20 w 15 min |
| `idempotency_conflict` | liczba zdarzeń | ≥ 5 w 15 min |
| `repeated_redemption` | liczba zdarzeń | ≥ 3 w 15 min |
| `tool_limit` | tury z `assistant.tool_limit` | ≥ 3 w 15 min |
| `rejected_tools` | tury z odrzuconym narzędziem (np. próby cudzego kuponu) | ≥ 5 w 15 min |
| `ai_errors` | udział `provider_error` + `timeout` wśród zakończonych tur | ≥ 20 % przy ≥ 10 turach, **krytyczna** |

- Tury przerwane przez klienta nie liczą się do błędów AI. Porzucenie rozmowy to sygnał, nie porażka.
- `ObservabilityWorker` (co `MonitorInterval`, 1 min) ocenia reguły. Przekroczony próg zapisuje w logu ostrzeżenie (regułę krytyczną jako błąd), najwyżej raz na `Cooldown` (30 min). Pamięć wyciszeń jest w procesie, więc restart może powtórzyć alert.
- Kanały zewnętrzne (Slack) nie są skonfigurowane - wyłączone bez konfiguracji.
- Naruszenia izolacji i mutacje bez zgody nie mają reguły: są blokowane konstrukcyjnie (narzędzia bez parametru użytkownika, aktywacja tylko przyciskiem) i sprawdzane evalami. Próby widać w `rejected_tools` i `access_denied`.

### Panel admina

- `GET /api/admin/observability?hours=` (1-720, domyślnie 24) zwraca:
  - liczby tur według wyniku, kody błędów;
  - p95 czasu do pierwszego zdarzenia i całej tury (`percentile_cont`);
  - narzędzia według stanu;
  - tokeny (sumy jako `long`) tylko z tur, które je mają, oraz liczbę tur bez danych;
  - zdarzenia bezpieczeństwa według rodzaju;
  - stan reguł.
- `GET /api/admin/observability/review` - kolejka: nieprzejrzane tury z błędem albo (poza przerwanymi przez klienta) z narzędziem w stanie innym niż `ok`, najnowsze najpierw, do 50. Powody: `error`, `rejected_tool`, `tool_failure`, `repeated_errors` (pseudonim z ≥ 3 błędami w okresie retencji).
- `POST /api/admin/observability/review/{id}` oznacza turę jako przejrzaną (kto i kiedy). Powtórzenie zachowuje pierwszy przegląd.
- Rola `Admin`, CSRF. W odpowiedziach tylko pseudonimy i metadane. Ekran: `/admin/obserwowalnosc`.

### Retencja

- `Observability:TraceRetention` i `SecurityEventRetention` (domyślnie 30 dni). Worker usuwa starsze wiersze co `PurgeInterval` (1 h).
- Kolejka przeglądu sięga tylko w okres retencji.

### Limity

| Ścieżka | Przed | Po |
| --- | --- | --- |
| logowanie | blokada konta Identity (5 prób, 5 min) | + `RateLimits:AuthPermitLimit` prób na adres IP w `AuthWindow` (10/min) |
| rejestracja | brak | ten sam limit na adres IP |
| zakup / odbiór kuponu u kasjera | brak | `RateLimits:PurchasePermitLimit` na kasjera w `PurchaseWindow` (60/min) |
| podgląd kuponu | 30/min na kasjera | bez zmian |
| czat | 20 tur/min na użytkownika | bez zmian |

- Przeglądarka łączy się z API przez rewrite Nexta, więc adres gniazda to serwer Nexta. Limit używa adresu z `X-Forwarded-For` (`UseForwardedHeaders`, `ClientAddress.cs`) - tylko od zaufanych proxy: loopback domyślnie (Next na tym samym hoście), inne w `ForwardedHeaders:KnownProxies`. Brany jest ostatni wpis (`ForwardLimit` 1).
- Rewrite Nexta **nie** ustawia `X-Forwarded-For` i przepuszcza nagłówek podany przez klienta (sprawdzone serwerem zwracającym nagłówki). Dlatego `proxy.ts` usuwa go (i `X-Forwarded-Proto`, [ADR 0010](/decyzje/0010-obrazy-i-hosting/)) dla `/api/*`, chyba że `TRUST_FORWARDED_FOR=true`. Tę wartość ustaw tylko wtedy, gdy przed Nextem stoi proxy hostingu, które ten nagłówek ustawia lub dopisuje (P9).
- Bez takiego proxy (lokalnie) wszyscy klienci dzielą jeden limit adresu serwera Next: 10 nieudanych prób na minutę blokuje logowanie wszystkim. W demo na jednym komputerze to akceptowalne. Na serwerze wymagane jest proxy z `X-Forwarded-For`. Blokada konta Identity działa niezależnie od adresu.
- `429` przy ponowieniu zakupu nie kasuje klucza idempotencji na froncie: limit działa przed odtworzeniem zapisanej odpowiedzi, więc zakup mógł się już zapisać.

## Alternatywy

- **`AddProblemDetails` z `CustomizeProblemDetails`**: domyślny writer dodaje `traceId` i zmienia odpowiedzi. Rejestrator, który odmawia zapisu, nie zmienia nic.
- **Liczniki w pamięci zamiast tabeli**: znikają przy restarcie i nie działają przy wielu instancjach. Tabela daje też historię dla kolejki.
- **Zdarzenia agregowane w pamięci i zrzucane okresowo**: mniej zapisów, ale utrata przy restarcie i trudniejsza kolejka. Limit na minutę wystarcza.
- **Alerty push (Slack) od razu**: SPEC traktuje je jako opcjonalne. Log z wyciszeniem wystarcza lokalnie.

## Konsekwencje

- Każde odrzucone żądanie to jeden INSERT po odpowiedzi, najwyżej 600 na minutę. Powyżej tej liczby liczby w panelu są zaniżone (nadmiar jest tylko w logu).
- Test retencji wywołuje `PurgeAsync` z datą w przyszłości i czyści przy tym stare wiersze bazy testowej - baza testowa nie przechowuje niczego trwałego.
- E2E i testy API podnoszą `RateLimits:AuthPermitLimit`, bo logują wiele kont z jednego adresu. Test limitów obniża go na własnym hoście.
