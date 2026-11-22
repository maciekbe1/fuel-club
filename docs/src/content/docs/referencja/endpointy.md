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
| `POST /api/cashier/fuel-purchases` | rola `Cashier` (Admin nie dziedziczy), `Idempotency-Key` | tak; opcjonalne `couponCode` | `201 { purchaseId, pointsAwarded, discount, paidAmount }` | `400 loyalty.validation_failed`, `400 idempotency.*`, `401`, `403`, `404 loyalty.cashier_station_missing`, `404 loyalty.card_not_found`, `404 rewards.coupon_not_found`, `409 loyalty.receipt_already_registered`, `409 rewards.coupon_redeemed`, `409 idempotency.*`, `503 loyalty.customer_busy`, `503 rewards.coupon_busy`, `429 rate_limit.exceeded` (limit na kasjera) |
| `POST /api/cashier/coupons/preview` | rola `Cashier` (Admin nie dziedziczy), limit prób na kasjera | `{ cardCode, couponCode, amount?, liters? }` | `200 { offerName, discountPerLiter, maxLiters, calculation }`; `calculation` `null` bez kwoty i litrów | `400 rewards.validation_failed`, `401`, `403`, `403 csrf.invalid`, `404 loyalty.card_not_found`, `404 rewards.coupon_not_found`, `409 rewards.coupon_redeemed`, `429 rate_limit.exceeded` |
| `GET /api/cashier/station` | rola `Cashier` (Admin nie dziedziczy) | - | `200 { stationCode, registerCode }` | `401`, `403`, `404 loyalty.cashier_station_missing` |
| `POST /api/chat` | rola `User`, CSRF, limit tur na użytkownika | `{ message, history? }` | `200 text/event-stream`: `meta`, `tool`, `delta`, `confirmation`, potem `done` albo `error` ([ADR 0007](/decyzje/0007-czat-i-potwierdzenia/)) | `400 assistant.validation_failed`, `401`, `403`, `403 csrf.invalid`, `429 rate_limit.exceeded` |
| `POST /api/chat/confirmations` | rola `User`, CSRF, `Idempotency-Key` | `{ token }` z zdarzenia `confirmation` | `200` kupon z `code` (wspólna aktywacja) | `400` brak/zły klucz, `401`, `403`, `403 csrf.invalid`, `404 assistant.confirmation_invalid`, `404 rewards.coupon_not_found`, `409 rewards.coupon_redeemed`, `409 idempotency.*`, `410 assistant.confirmation_expired` |
| `GET /api/admin/observability?hours=` | rola `Admin` | - | `200`: metryki czatu, zdarzenia bezpieczeństwa, stan reguł ([ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/)) | `400 observability.validation_failed`, `401`, `403` |
| `GET /api/admin/observability/review` | rola `Admin` | - | `200 { items[] }`: tury do przejrzenia (pseudonimy, metadane) | `401`, `403` |
| `POST /api/admin/observability/review/{id}` | rola `Admin`, CSRF | - | `204` (powtórzenie zachowuje pierwszy przegląd) | `401`, `403`, `403 csrf.invalid`, `404 observability.turn_not_found` |
| `GET /health/db` | brak, tylko Development | - | `200 { database }` | `503` |
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
