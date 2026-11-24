---
title: "0022 - Automatyczne wdrożenie na Cloud Run"
description: Po zielonym CI na main ten sam commit trafia na Cloud Run - GitHub Actions z Workload Identity Federation, bez kluczy.
sidebar:
  order: 22
---

## Status

Decyzja przyjęta (2026-11-24). Uzupełnia [ADR 0010](/decyzje/0010-obrazy-i-hosting/) i [instrukcję Cloud Run](/instrukcje/obrazy-i-hosting/#cloud-run).

## Problem

Wdrożenie było ręczne: build w Cloud Build, migracja, `gcloud run deploy` dla api i web. Łatwo wdrożyć niezacommitowany kod albo commit, który nie przeszedł CI.

## Rozwiązanie

- `.github/workflows/deploy.yml` startuje po zakończeniu CI (`workflow_run`) na `main`, tylko gdy CI przeszło i był to push. Ręcznie: `workflow_dispatch`.
- Wdraża dokładnie commit sprawdzony przez CI (`head_sha`), tag obrazu = skrót commitu.
- Kolejność: obrazy api i web → push do Artifact Registry → job migracji z nowym obrazem (`--wait`; błąd zatrzymuje wdrożenie) → `fuelclub-api` → `fuelclub-web` → smoke test (`/login` 200, `/api/auth/me` przez web 401, api z internetu 404).
- Jedno wdrożenie naraz, bez przerywania w trakcie (`concurrency`, `cancel-in-progress: false`).
- Uwierzytelnienie bez kluczy: token OIDC GitHuba wymieniany przez Workload Identity Federation (pula `github`, dostawca `github-oidc`). Warunek dostawcy: repozytorium `maciekbe1/fuel-club` i `refs/heads/main` - PR ani inna gałąź nie dostaną tokenu.
- Konto `github-deployer` ma tylko: `artifactregistry.writer` na repozytorium `fuelclub`, `run.developer` w projekcie, `iam.serviceAccountUser` na kontach `fuelclub-api` i `fuelclub-web`. Bez dostępu do Secret Managera i bazy - sekrety czyta rewizja, nie wdrożenie.

## Alternatywy

- **Klucz JSON konta usługi w sekretach GitHuba**: prostszy, ale długowieczny klucz do wycieku i rotacji.
- **Cloud Build trigger z GitHuba**: wdrożenie poza CI, osobne uprawnienia i drugi system do obserwowania.
- **Job `deploy` w `ci.yml`**: `cancel-in-progress` CI przerwałby wdrożenie w trakcie migracji.

## Konsekwencje

- Brak środowiska staging i canary: zielone CI idzie od razu na produkcję demo. Rollback: przełączenie ruchu na poprzednią rewizję (`gcloud run services update-traffic fuelclub-api --to-revisions <rewizja>=100`, tak samo web).
- Migracje muszą być zgodne wstecz: przez chwilę stara rewizja api działa na nowym schemacie, a rollback rewizji nie cofa migracji.
- Evale na Gemini nie są bramką wdrożenia (koszt, klucz) - tylko evale na modelu demo w CI.
