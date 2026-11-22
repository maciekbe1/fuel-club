---
title: Observability (zdarzenia i anomalie)
description: Zdarzenia bezpieczeństwa, reguły anomalii, kolejka przeglądu tur czatu i retencja.
sidebar:
  order: 5
---

## Co robi

Liczy odrzucone żądania (odmowy, nieudane logowania, konflikty kluczy, ponowne odbiory, limity) i razem ze śladami tur czatu pokazuje adminowi metryki, stan reguł anomalii i tury do przejrzenia. Usuwa dane starsze niż retencja. Decyzja: [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/). Ślady tur: [ADR 0008](/decyzje/0008-slady-i-evale-czatu/).

## Gdzie jest kod

| Co | Plik |
| --- | --- |
| Rejestracja, trasy, middleware | `apps/api/Modules/Observability/ObservabilityModule.cs` |
| Zdarzenie i tabela | `SecurityEvent.cs` |
| Przechwycenie kodu i zapis zdarzeń | `SecurityEvents.cs` (`ProblemCodeCapture`, `SecurityEventsMiddleware`) |
| Ustawienia i reguły | `ObservabilityOptions.cs`, `AnomalyRules.cs` (`AnomalyRules`, `AnomalyAlerts`) |
| Retencja i worker | `ObservabilityJobs.cs` |
| Endpointy admina | `ObservabilityEndpoints.cs` |
| Limity logowania i zakupów, adres klienta | `apps/api/Infrastructure/RateLimiting.cs`, `apps/api/Infrastructure/ClientAddress.cs` |
| Ekran | `apps/web/app/(app)/admin/obserwowalnosc/page.tsx`, `apps/web/features/admin-observability/` |

## Przepływ

1. Każde żądanie przechodzi przez `SecurityEventsMiddleware`. Po odpowiedzi `403`, `401` logowania, `409` z kodem klucza lub odbioru albo `429` powstaje wiersz `security_events`.
2. Co minutę worker ocenia reguły i loguje przekroczenia (z wyciszeniem), a co godzinę usuwa stare wiersze.
3. Admin otwiera „Obserwowalność” (skrót na ekranie startowym). Widzi metryki w oknie 1 h / 24 h / 7 dni, reguły w ich własnych oknach i kolejkę. „Oznacz jako przejrzane” usuwa turę z kolejki.

## Dane

- `security_events`: czas, rodzaj, status, kod, wzorzec trasy, pseudonim (dla zalogowanych). Bez IP i treści.
- `assistant_turns`: `ReviewedAt`, `ReviewedBy` (id admina).

Szczegóły: [baza danych](/referencja/baza-danych/).

## Zabezpieczenia

- Endpointy tylko dla roli `Admin`, z CSRF. Ekran za strażnikiem `admin/layout.tsx`.
- W odpowiedziach tylko pseudonimy i metadane. Treści rozmów nie ma w bazie.
- Trasy jako wzorce - identyfikatory kuponów i ofert z URL nie trafiają do zdarzeń.
- Zapis zdarzenia nie wpływa na odpowiedź: idzie po jej wysłaniu, a błąd zapisu trafia tylko do logu.

## Testy

| Plik | Sprawdza |
| --- | --- |
| `apps/api.tests/ObservabilityTests.cs` | **`403`, `401` logowania, `409` klucza i ponownego odbioru (prawdziwy odbiór przy kasie) → zdarzenia z wzorcem trasy i pseudonimem**, bez id z URL i id użytkownika; anonimowe `401 /api/auth/me` pomijane; **limit logowań na adres klienta z `X-Forwarded-For` (inny adres nie jest blokowany), `/api/auth/login/` liczone jak logowanie, limit zakupów na kasjera → `429` + `Retry-After` + zdarzenie**; limit zapisu zdarzeń na minutę; reguły: okno, próg, minimalna próbka udziału błędów AI, przerwane tury poza mianownikiem, `tool_limit`, `rejected_tools`; wyciszenie alertów; walidacja konfiguracji reguł; podsumowanie: liczby, p95 (`percentile_cont`), narzędzia, tokeny „brak danych”, zdarzenia, 8 reguł, brak id i e-maili, `403` klient, `401` anonim, `400` złe okno; kolejka: błąd i odrzucone narzędzie tak, udana i przerwana (także w trakcie narzędzia) nie, `repeated_errors`, oznaczenie (CSRF, idempotentne, `404`), kto i kiedy, `403` klient; retencja usuwa tylko starsze niż okres |
| `apps/web/e2e/observability.spec.ts` | tura klienta z odrzuconym narzędziem trafia do kolejki admina (po `correlationId`), bez e-maila klienta; metryki, 8 reguł, przełącznik okna; „Oznacz jako przejrzane” usuwa ją z kolejki; klient nie wejdzie na ekran (przekierowanie) ani do API (`403`) |

Próby negatywne: bez `UseForwardedHeaders` pada test limitów; bez wykluczenia przerwanych tur pada test kolejki; bez `ProblemCodeCapture` pada test zdarzeń (brak konfliktu i ponownego odbioru); ocena reguły udziału bez minimalnej próbki wywraca test reguł.
