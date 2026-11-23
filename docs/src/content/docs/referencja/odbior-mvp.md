---
title: Odbiór lokalnego MVP
description: Kryteria specyfikacji (specs/SPEC.md) z dowodem albo jawnym blockerem.
sidebar:
  order: 6
---

Stan na 2026-11-22. Dowód to test, który przechodzi, albo opisany ręczny przebieg. Liczby testów i przebiegi: `IMPLEMENTATION-STATE.md`.

## 1. Interfejs ofert i kuponów

| Kryterium | Dowód |
| --- | --- |
| Katalog: stany ładowania, pustej listy, błędu, ponowienia | błąd i ponowienie: `e2e/rewards.spec.ts`; ładowanie i pusta lista: obsłużone w `RewardsCatalog.tsx`, bez testu E2E |
| Wymiana: potwierdzenie, wersja oferty, zmienione warunki, utrata odpowiedzi bez drugiego kuponu, odświeżenie salda | `e2e/rewards.spec.ts`, `RewardExchangeTests` |
| Moje kupony: stany, aktywacja ≠ odbiór, GET po mutacji, QR i kopiowanie tylko dla aktywnego | `e2e/coupons.spec.ts`, `CouponActivationTests` |
| Kasa: osobne pola, skan tylko wypełnia, podgląd bez zużycia z limitem, „Przelicz”, nieaktualne wyliczenie, spóźniona odpowiedź, kod zamiast rabatu, nieznany wynik | `e2e/cashier-coupon.spec.ts`, `e2e/cashier.spec.ts`, `CouponPreviewTests`, `CouponRedemptionTests` |
| Historia: brutto, rabat, zapłacono | `e2e/cashier-coupon.spec.ts`, `e2e/history.spec.ts` |
| Admin: strażnik, lista, tworzenie (idempotencja), edycja (wersja, konflikt, nieznany wynik), wyłączanie | `e2e/admin.spec.ts`, `RewardOfferTests` |
| Polski UI, telefon i desktop, cache czyszczony przy zmianie konta | każdy E2E w projektach `mobile` i `desktop`; czyszczenie przy wylogowaniu: `e2e/coupons.spec.ts`; błąd API bez wylogowania: `e2e/session.spec.ts` |

## 2. Czat i narzędzia

| Kryterium | Dowód |
| --- | --- |
| Adapter demo jawnie oznaczony, bez sekretów | `AssistantTests`, `e2e/chat.spec.ts` (etykieta demo) |
| SSE przez fetch, zdarzenia, typy z OpenAPI, ramka podzielona na fragmenty | `e2e/chat.spec.ts` (`sse-split`, `sse-cut`), `ChatStreamContract` w `schema.d.ts` |
| Limity wejścia, czasu, narzędzi; anulowanie; heartbeat; brak buforowania; błąd bez sukcesu | `AssistantTests`, `e2e/chat.spec.ts`, [ADR 0007](/decyzje/0007-czat-i-potwierdzenia/) |
| Zamknięta lista narzędzi, tożsamość z sesji, walidacja argumentów | `AssistantTests`, evale `assistant-evals-v2` |
| Propozycja bez mutacji, potwierdzenie przyciskiem, wspólna aktywacja z Idempotency-Key, brak naliczania/wymiany/odbioru | `AssistantTests`, `e2e/chat.spec.ts`, `e2e/demo.spec.ts` |

## 3. Evals i obserwowalność

| Kryterium | Dowód |
| --- | --- |
| Wersjonowane przypadki wszystkich kategorii, asercje na narzędzia, właściciela i skutki w bazie | `AssistantEvals` (`assistant-evals-v2`, 21 przypadków) |
| Ślady: correlationId, pseudonim, narzędzia, czasy, tryb dostawcy; bez treści i kodów | `AssistantTracingTests`, `TurnRecorderTests`, [ADR 0008](/decyzje/0008-slady-i-evale-czatu/) |
| OpenTelemetry, opcjonalny Phoenix, niedostępny odbiorca nie blokuje | `AssistantTracingTests` (eksport OTLP, nieosiągalny kolektor); ręcznie: Phoenix w P7a |
| Metryki całego ruchu (p95, błędy, narzędzia, odmowy, limity) | `ObservabilityTests`, `e2e/observability.spec.ts` |
| Reguły anomalii: okno, minimalna próbka, wyciszenie, błędy AI krytyczne | `ObservabilityTests`, [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/) |
| Kolejka podejrzanych tur, dostęp rolą, retencja | `ObservabilityTests`, `e2e/observability.spec.ts` |
| Limity logowania, realizacji, czatu | `ObservabilityTests` (logowanie, zakupy), `AssistantTests` (czat), `CouponPreviewTests` (podgląd) |

:::caution[Planowane]
- **Ocena próbek przez judge** (udział, budżet, osobna próbka błędów, kalibracja) - P7c wymaga klucza dostawcy modelu, którego w tym środowisku nie ma. Kolejka nie ma więc powodu „niska ocena”.
- **Feedback klienta** - opcjonalny w specyfikacji, niezaimplementowany. Kolejka nie ma powodu „negatywny feedback”.
- **Alerty Slack** - opcjonalne, wyłączone; alerty idą do logu.
:::

## 4. Dostarczenie demo

| Kryterium | Dowód |
| --- | --- |
| Pełny przebieg klient + kasjer w osobnych kontekstach, aktywacja przez czat | `e2e/demo.spec.ts` |
| Utrata odpowiedzi i drugi odbiór nie podwajają efektu | `e2e/cashier-coupon.spec.ts`, `e2e/demo.spec.ts`, `CouponRedemptionTests`, `IdempotencyTests` |
| Drugie konto nie widzi pierwszego (UI, HTTP, narzędzia) | `e2e/history.spec.ts`, `e2e/coupons.spec.ts`, `AssistantTests`, evale (cudzy kupon) |
| README: świeży klon, konfiguracja, migracje, konta demo, start, kontrole | `README.md`. Świeży klon sprawdzony 2026-11-22 i znaleziony brak `dotnet restore` przed `dotnet ef` (poprawione) |
| CI: API na PostgreSQL, typecheck/lint, E2E, evale, docs | `.github/workflows/ci.yml` |

:::caution[Planowane]
CI nie było uruchomione na GitHubie: repozytorium nie jest wypychane bez zgody właściciela. Kroki workflow są uruchamiane lokalnie tymi samymi komendami.
:::

## 5. Hosting (P9)

| Kryterium | Dowód |
| --- | --- |
| Obrazy web/API, trwałe klucze Data Protection, migracje, backup/odtworzenie, SSE przez obraz | [Obrazy i hosting](/instrukcje/obrazy-i-hosting/) (lokalna próba produkcyjna), `DataProtectionTests` |
| Instrukcja hostingu (Cloud Run) | [Obrazy i hosting](/instrukcje/obrazy-i-hosting/#cloud-run) |

:::caution[Planowane]
Wdrożenie na Cloud Run, HTTPS na publicznej domenie, test SSE przez brzeg hostingu i kamery na telefonie. Wymaga konta; nie utworzono płatnych zasobów.
:::
