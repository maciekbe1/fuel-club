---
title: Mapa projektu
description: Co jest w którym folderze.
sidebar:
  order: 1
---

```text
apps/
  api/                          ASP.NET Core 10, modularny monolit
    Program.cs                  rejestracja usług i endpointów
    Infrastructure/             AppDbContext, kontrola migracji przy starcie, CSRF, health
    Modules/Accounts/           konta, sesja, role
    Modules/Loyalty/            punkty: Domain/ (reguły, encje), Infrastructure/ (mapowanie EF)
    Modules/Rewards/            oferty i kupony: Domain/ (encje, kod kuponu, rabat), Infrastructure/ (mapowanie EF)
    Migrations/                 migracje EF Core
  api.tests/                    testy integracyjne (xUnit + PostgreSQL)
  web/                          Next.js 16 App Router
    app/(public)/               logowanie, rejestracja - bez sesji
    app/(app)/                  ekrany po zalogowaniu, bramka sesji w layout.tsx
    features/<nazwa>/           services.ts, hooks.ts, schemas.ts, components/
    components/ui/              shadcn
    components/form, brand, layout
    lib/api/                    klient HTTP, CSRF, błędy, schema.d.ts (generowany)
    lib/server/                 odczyt z API w Server Components
    proxy.ts                    przekierowanie na /login bez ciasteczka sesji
docs/                           ta dokumentacja (Astro Starlight)
tests/                          planowane: evals (E2E: apps/web/e2e)
compose.yml                     postgres (5433), postgres-test (5434)
dotnet-tools.json               dotnet-ef
PLAN-MVP.md                     zakres i niezmienniki MVP
CLAUDE.md                       zasady dla asystenta AI
```

## Moduł backendu - układ

```text
Modules/<Nazwa>/
  <Nazwa>Endpoints.cs           mapowanie grupy tras
  <Przypadek>/                  endpoint + request (np. Login/, Register/)
  Application/                  interfejsy usług, wyniki operacji
  Infrastructure/               implementacje (Identity, EF), hosted services
```

## Feature frontendu - układ

`app -> features -> lib`. Feature nie importuje innego feature. `services.ts` nie importuje Reacta. Szczegóły: [ADR 0001](/decyzje/0001-architektura-frontendu/).
