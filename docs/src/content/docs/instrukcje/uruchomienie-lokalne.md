---
title: Uruchomienie lokalne
description: PostgreSQL, API i frontend na własnym komputerze.
sidebar:
  order: 1
---

**Wymagania:** .NET SDK 10, Node.js 22, Docker.

## 1. Baza danych

```sh
docker compose up -d postgres
```

PostgreSQL 17 nasłuchuje na `127.0.0.1:5433`, baza `fuelclub`, użytkownik `fuelclub`, hasło `fuelclub_local` (tylko lokalnie).

## 2. Connection string

API czyta `ConnectionStrings:DefaultConnection`. Ustaw go w user-secrets (poza repozytorium):

```sh
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5433;Database=fuelclub;Username=fuelclub;Password=fuelclub_local" \
  --project apps/api
```

Bez tej wartości API nie wystartuje (`Connection string 'DefaultConnection' is not configured.`).

## 3. Migracje

```sh
dotnet tool restore
dotnet restore
dotnet ef database update --project apps/api
```

Na świeżym klonie `dotnet ef` bez `dotnet restore` kończy się błędem `NETSDK1004` (brak `project.assets.json`).

API nie migruje bazy samo. Bez tego kroku nie wystartuje i wskaże brakujące migracje. Szczegóły: [Migracje](/instrukcje/migracje/).

## 4. API

```sh
dotnet run --project apps/api --launch-profile http
```

- Adres: `http://localhost:5271`.
- Przy starcie API sprawdza, czy wszystkie migracje z kodu są w historii bazy, i tworzy role `User` i `Admin`.
- W Development: dokument OpenAPI pod `/openapi/v1.json`, `/health/db` i interfejs do próbowania endpointów **Scalar** pod `http://localhost:5271/scalar`.
  - Logowanie: `POST /api/auth/login`. Ciasteczko sesji zostaje w przeglądarce.
  - Metody zmieniające dane wymagają nagłówka `X-CSRF-TOKEN` z `GET /api/auth/csrf`; pobierz token po zalogowaniu.
  - Operacje idempotentne wymagają też `Idempotency-Key` (dowolny UUID).
  - Gotowe żądania dla IDE: `apps/api/FuelClub.Api.http`.

## 5. Frontend

```sh
cd apps/web
npm install
npm run dev
```

- Adres: `http://localhost:3000`.
- `/api/*` jest przekazywane do `API_INTERNAL_URL` (domyślnie `http://localhost:5271`).

## 6. Dokumentacja

```sh
cd docs
npm install
npm run dev
```

## Sprawdzenie

- `curl http://localhost:5271/health/db` zwraca `{"database":"connected"}`.
- `http://localhost:3000` przekierowuje na `/login`.

Konto administratora: [Utworzenie administratora](/instrukcje/administrator/).
