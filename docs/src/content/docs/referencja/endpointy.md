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
| `POST /api/auth/register` | brak | tak | `201` | `400`, `403 csrf.invalid` |
| `POST /api/auth/login` | brak | tak | `204` + cookie sesji | `400`, `401`, `403 csrf.invalid` |
| `POST /api/auth/logout` | zalogowany | tak | `204` | `401`, `403 csrf.invalid` |
| `GET /api/auth/me` | zalogowany | - | `200 { id, email, displayName, roles }` | `401` |
| `GET /api/admin/access` | rola `Admin` | - | `204` | `401`, `403` |
| `GET /api/me/wallet` | rola `User` | - | `200 { balance }` (0 bez konta) | `401`, `403` |
| `GET /api/me/transactions?limit=&cursor=` | rola `User` | - | `200 { items[], nextCursor }`; zakupy za 0 pkt nie występują | `400 loyalty.validation_failed`, `401`, `403` |
| `GET /api/me/loyalty-card` | rola `User` | - | `200 { code, issuedAt }` | `401`, `403`, `404 loyalty.card_missing` |
| `POST /api/me/loyalty-card` | rola `User`, `Idempotency-Key` | tak | `201 { code, issuedAt }` | `400 idempotency.*`, `401`, `403`, `409 loyalty.card_already_issued`, `409 idempotency.*`, `503 loyalty.customer_busy` |
| `POST /api/me/loyalty-card/rotate` | rola `User`, `Idempotency-Key` | tak | `200 { code, issuedAt }` | `400 idempotency.*`, `401`, `403`, `404 loyalty.card_missing`, `409 idempotency.*`, `503 loyalty.customer_busy` |
| `POST /api/cashier/fuel-purchases` | rola `Cashier` (Admin nie dziedziczy), `Idempotency-Key` | tak | `201 { purchaseId, pointsAwarded }` | `400 loyalty.validation_failed`, `400 idempotency.*`, `401`, `403`, `404 loyalty.cashier_station_missing`, `404 loyalty.card_not_found`, `409 loyalty.receipt_already_registered`, `409 idempotency.*`, `503 loyalty.customer_busy` |
| `GET /api/cashier/station` | rola `Cashier` (Admin nie dziedziczy) | - | `200 { stationCode, registerCode }` | `401`, `403`, `404 loyalty.cashier_station_missing` |
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
Nagrody, kupony, panel kasjera (realizacje kuponów), panel administratora (oferty), czat SSE i obserwowalność - patrz `PLAN-MVP.md`, sekcja „API - proponowane granice”.
:::
