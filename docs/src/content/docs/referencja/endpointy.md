---
title: Endpointy API
description: Zrealizowane trasy HTTP.
sidebar:
  order: 2
---

Pełny, aktualny kontrakt: `GET /openapi/v1.json` (Development). Ta tabela opisuje zachowanie, którego OpenAPI nie pokazuje.

| Metoda i trasa | Autoryzacja | CSRF | Sukces | Błędy |
| --- | --- | --- | --- | --- |
| `GET /api/auth/csrf` | brak | - | `200 { token }` | - |
| `GET /api/simulation` | brak | - | `200 { runId, provider, model, demo }`; trasa istnieje tylko przy `Simulation:Enabled` (instancja demo, Development) | `404` poza symulacją |
| `POST /api/auth/register` | brak | tak | `201` | `400`, `403 csrf.invalid`, `429 rate_limit.exceeded` (limit na adres IP) |
| `POST /api/auth/login` | brak | tak | `204` + cookie sesji | `400`, `401`, `403 csrf.invalid`, `429 rate_limit.exceeded` (limit na adres IP) |
| `POST /api/auth/logout` | zalogowany | tak | `204` | `401`, `403 csrf.invalid` |
| `GET /api/auth/me` | zalogowany | - | `200 { id, email, displayName, roles }` | `401` |
| `GET /api/admin/access` | rola `Admin` | - | `204` | `401`, `403` |
| `GET /api/rewards` | rola `User` | - | `200 { items[] }`: aktywne oferty z `version`, kolejność koszt → nazwa → `id` | `401`, `403` |
| `POST /api/rewards/{id}/exchange` | rola `User`, CSRF, `Idempotency-Key` | `{ version }` | `201 { coupon, balance }` + `Location` | `400 rewards.validation_failed`, `400` brak/zły klucz, `401`, `403`, `403 csrf.invalid`, `404 rewards.offer_not_found`, `409 rewards.offer_inactive`, `409 rewards.offer_changed`, `409 rewards.insufficient_points`, `409 idempotency.*`, `503 loyalty.customer_busy`, `503 rewards.offer_busy` |
| `GET /api/me/coupons` | rola `User` | - | `200 { items[] }`: własne kupony od najnowszych | `401`, `403` |
| `GET /api/me/coupons/{id}` | rola `User` | - | `200` kupon | `401`, `403`, `404 rewards.coupon_not_found` |
| `POST /api/me/coupons/{id}/activate` | rola `User`, CSRF, `Idempotency-Key` | - | `200` kupon z `code` | `400` brak/zły klucz, `401`, `403`, `403 csrf.invalid`, `404 rewards.coupon_not_found`, `409 rewards.coupon_redeemed`, `409 idempotency.*` |
| `GET /api/admin/rewards` | rola `Admin` | - | `200 { items[] }`: wszystkie oferty z `isActive`, `version` | `401`, `403` |
| `GET /api/admin/rewards/{id}` | rola `Admin` | - | `200` oferta z `version` | `401`, `403`, `404 rewards.offer_not_found` |
| `POST /api/admin/rewards` | rola `Admin`, CSRF, `Idempotency-Key` | `{ name, description, pointsCost, discountPerLiter, maxLiters, isActive }` | `201` + `Location`, `version: 1` | `400 rewards.validation_failed`, `400` brak/zły klucz idempotencji, `401`, `403`, `403 csrf.invalid`, `409 idempotency.key_reused`, `409 idempotency.in_progress` |
| `PUT /api/admin/rewards/{id}` | rola `Admin`, CSRF | cały formularz + `version` | `200` oferta z nową `version` | `400 rewards.validation_failed`, `401`, `403`, `403 csrf.invalid`, `404 rewards.offer_not_found`, `409 rewards.offer_version_conflict` |
| `GET /api/me/wallet` | rola `User` | - | `200 { balance }` (0 bez konta) | `401`, `403` |
| `GET /api/me/transactions?limit=&cursor=` | rola `User` | - | `200 { items[], nextCursor }`; zakupy za 0 pkt nie występują | `400 loyalty.validation_failed`, `401`, `403` |
| `GET /api/me/loyalty-card` | rola `User` | - | `200 { code, issuedAt }` | `401`, `403`, `404 loyalty.card_missing` |
| `POST /api/me/loyalty-card` | rola `User`, `Idempotency-Key` | tak | `201 { code, issuedAt }` | `400 idempotency.*`, `401`, `403`, `409 loyalty.card_already_issued`, `409 idempotency.*`, `503 loyalty.customer_busy` |
| `POST /api/me/loyalty-card/rotate` | rola `User`, `Idempotency-Key` | tak | `200 { code, issuedAt }` | `400 idempotency.*`, `401`, `403`, `404 loyalty.card_missing`, `409 idempotency.*`, `503 loyalty.customer_busy` |
| `POST /api/cashier/fuel-purchases` | rola `Cashier` (Admin nie dziedziczy), `Idempotency-Key` | tak; opcjonalne `couponCode` | `201 { purchaseId, pointsAwarded, discount, paidAmount }` | `400 loyalty.validation_failed`, `400 idempotency.*`, `401`, `403`, `404 loyalty.cashier_station_missing`, `404 loyalty.card_not_found`, `404 rewards.coupon_not_found`, `409 loyalty.receipt_already_registered`, `409 rewards.coupon_redeemed`, `409 rewards.coupon_wrong_kind` (kod nagrody), `409 idempotency.*`, `503 loyalty.customer_busy`, `503 rewards.coupon_busy`, `429 rate_limit.exceeded` (limit na kasjera) |
| `POST /api/cashier/reward-handovers` | rola `Cashier`, CSRF, `Idempotency-Key`, limit jak zakup | `{ cardCode, couponCode }` | `200 { couponId, offerName, handedOverAt }` | `400 rewards.validation_failed`, `400 idempotency.*`, `401`, `403`, `404 loyalty.cashier_station_missing`, `404 loyalty.card_not_found`, `404 rewards.coupon_not_found`, `409 rewards.coupon_wrong_kind`, `409 rewards.coupon_redeemed`, `409 idempotency.*`, `503 loyalty.customer_busy`, `429 rate_limit.exceeded` |
| `POST /api/cashier/coupons/preview` | rola `Cashier` (Admin nie dziedziczy), limit prób na kasjera | `{ cardCode, couponCode, amount?, liters? }` | `200 { offerName, kind, discountPerLiter, maxLiters, calculation }`; `calculation` `null` bez kwoty i litrów; przy nagrodzie warunki `null` | `400 rewards.validation_failed`, `401`, `403`, `403 csrf.invalid`, `404 loyalty.card_not_found`, `404 rewards.coupon_not_found`, `409 rewards.coupon_redeemed`, `409 rewards.coupon_wrong_kind` (nagroda z kwotą), `429 rate_limit.exceeded` |
| `GET /api/cashier/station` | rola `Cashier` (Admin nie dziedziczy) | - | `200 { stationCode, registerCode }` | `401`, `403`, `404 loyalty.cashier_station_missing` |
| `POST /api/chat/conversations` | rola `User`, CSRF, `Idempotency-Key`, limit startów na użytkownika | - | `201` `{ id, startedAt, endedAt, endReason, turns }`; ten sam klucz - ta sama rozmowa | `400` brak/zły klucz, `401`, `403`, `403 csrf.invalid`, `409 idempotency.*`, `429 rate_limit.exceeded` |
| `POST /api/chat/conversations/{id}/end` | rola `User`, CSRF | - | `200` rozmowa; idempotentne (pierwszy powód zostaje, bezczynna kończy się jako `Idle`) | `401`, `403`, `403 csrf.invalid`, `404 assistant.conversation_not_found` (nieznana lub cudza) |
| `POST /api/chat` | rola `User`, CSRF, limit tur na użytkownika | `{ message, history?, conversationId }` | `200 text/event-stream`: `meta`, `tool`, `delta`, `confirmation`, `handoff`, potem `done` albo `error` ([ADR 0007](/decyzje/0007-czat-i-potwierdzenia/)) | `400 assistant.validation_failed` (też brak `conversationId`), `401`, `403`, `403 csrf.invalid`, `404 assistant.conversation_not_found`, `409 assistant.conversation_ended`, `409 assistant.handoff_active`, `429 rate_limit.exceeded` |
| `POST /api/chat/confirmations` | rola `User`, CSRF, `Idempotency-Key` | `{ token }` z zdarzenia `confirmation` | `200` kupon z `code` (wspólna aktywacja) | `400` brak/zły klucz, `401`, `403`, `403 csrf.invalid`, `404 assistant.confirmation_invalid`, `404 rewards.coupon_not_found`, `409 rewards.coupon_redeemed`, `409 assistant.handoff_active`, `409 idempotency.*`, `410 assistant.confirmation_expired`, `503 loyalty.customer_busy` |
| `POST /api/support/cases` | rola `User`, CSRF, `Idempotency-Key`, limit tworzenia na użytkownika | `{ message, history?, conversationId? }` - przekazywana rozmowa czatu, wiązana raz ([ADR 0017](/decyzje/0017-ruch-syntetyczny-i-rozmowy/)) | `201 { id, status, createdAt, closedAt }` | `400 support.validation_failed` (też `conversationId`: `support.conversation_not_found` - nieznana lub cudza), `400 idempotency.*`, `401`, `403`, `403 csrf.invalid`, `409 support.case_open` (z `caseId`), `409 idempotency.*`, `429 rate_limit.exceeded`, `503 loyalty.customer_busy` |
| `GET /api/support/cases/current` | rola `User` | - | `200 { id, status, createdAt, closedAt }` otwartego zgłoszenia albo `204` | `401`, `403` |
| `GET /api/support/cases/{id}/messages?after=` | rola `User` | - | `200 { caseId, status, lastSeq, items[] }` - do 100 wiadomości po `after` (domyślnie 0) | `400 support.validation_failed`, `401`, `403`, `404 support.case_not_found` (też cudze) |
| `POST /api/support/cases/{id}/messages` | rola `User`, CSRF, `Idempotency-Key`, limit wiadomości | `{ text }` 1-1000 | `201 { seq }` | `400 support.validation_failed`, `400 idempotency.*`, `401`, `403`, `403 csrf.invalid`, `404 support.case_not_found`, `409 support.case_closed`, `409 idempotency.*`, `429 rate_limit.exceeded` |
| `POST /api/support/cases/{id}/close` | rola `User`, CSRF | - | `200 { id, status, createdAt, closedAt }`; powtórzenie zwraca stan | `401`, `403`, `403 csrf.invalid`, `404 support.case_not_found` |
| `GET /api/support/agent/queue` | rola `Support` | - | `200 { waiting[], mine[] }` (razem do 100, moje pierwsze): id, stan, nazwa klienta, czasy, pierwsza wiadomość klienta | `401`, `403` |
| `GET /api/support/agent/cases/{id}/messages?after=` | rola `Support`, tylko opiekun | - | jak u klienta | `400`, `401`, `403`, `403 support.not_assignee`, `404 support.case_not_found` |
| `POST /api/support/agent/cases/{id}/messages` | rola `Support`, CSRF, `Idempotency-Key`, limit wiadomości, tylko opiekun otwartego | `{ text }` 1-1000 | `201 { seq }` | `400`, `401`, `403`, `403 csrf.invalid`, `403 support.not_assignee`, `404 support.case_not_found`, `409 support.case_closed`, `409 idempotency.*`, `429 rate_limit.exceeded` |
| `POST /api/support/agent/cases/{id}/release` | rola `Support`, CSRF, tylko opiekun | - | `200` zgłoszenie (`Waiting`) | `401`, `403`, `403 csrf.invalid`, `403 support.not_assignee`, `404 support.case_not_found`, `409 support.case_closed` |
| `POST /api/support/agent/cases/{id}/close` | rola `Support`, CSRF, tylko opiekun | - | `200` zgłoszenie (`Closed`); powtórzenie przez tego opiekuna zwraca stan | `401`, `403`, `403 csrf.invalid`, `403 support.not_assignee`, `404 support.case_not_found`, `409 support.case_closed` |
| `/api/support/hub` (SignalR) | rola `User` albo `Support`, origin z `Support:HubAllowedOrigins` | metoda `Typing(caseId)` | zdarzenia `messageAdded`, `caseChanged`, `queueChanged`, `typing` (`SupportHubContract`) | `401`, `403`, `403 support.origin_not_allowed` |
| `POST /api/support/agent/cases/{id}/claim` | rola `Support`, CSRF | - | `200 { id, status, customerName, createdAt, assignedToMe }`; ponowienie przez opiekuna też `200` | `401`, `403`, `403 csrf.invalid`, `404 support.case_not_found`, `409 support.case_taken`, `409 support.case_closed` |
| `GET /api/admin/observability?hours=&traffic=&runId=&model=&promptVersion=` | rola `Admin` | - | `200`: tury (p50/p95, kroki modelu, wywołania narzędzi, tokeny), koszt (nie dotyczy / nieznany / znany z wersją cennika), rozmowy (przekazanie raz na rozmowę), zdarzenia bezpieczeństwa, stan reguł, listy wartości filtrów; `traffic` = `real` (domyślnie) / `synthetic` / `all` ([ADR 0018](/decyzje/0018-metryki-rozmow-i-koszt/)) | `400 observability.validation_failed` (`hours`, `traffic`, puste lub > 100 znaków filtry), `401`, `403` |
| `GET /api/admin/observability/review` | rola `Admin` | - | `200 { items[] }`: tury do przejrzenia (pseudonimy, metadane) | `401`, `403` |
| `POST /api/admin/observability/review/{id}` | rola `Admin`, CSRF | - | `204` (powtórzenie zachowuje pierwszy przegląd) | `401`, `403`, `403 csrf.invalid`, `404 observability.turn_not_found` |
| `GET /health/db` | brak, tylko Development | - | `200 Healthy` (tekst, wbudowany health check EF Core) | `503 Unhealthy` |
| `GET /openapi/v1.json` | brak, tylko Development | - | `200` | - |

## Treść żądań

```json
// POST /api/auth/register
{ "email": "ania@example.test", "password": "Bardzo-Dobre1!", "displayName": "Ania" }

// POST /api/auth/login
{ "email": "ania@example.test", "password": "Bardzo-Dobre1!" }
```

Liczby w JSON muszą być liczbami (`10`, nie `"10"`): API ma `JsonNumberHandling.Strict`, więc OpenAPI i typy frontendu opisują je jako `number`. Liczba w cudzysłowie → `400`.

Format błędów: [Kody błędów](/referencja/kody-bledow/).

:::caution[Planowane]
Interfejs ofert, kuponów i kuponu w kasie, czat SSE i obserwowalność - patrz `PLAN-MVP.md`, sekcja „API - proponowane granice”.
:::
