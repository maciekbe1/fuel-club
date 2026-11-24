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
- Wdraża dokładnie commit sprawdzony przez CI (`head_sha`), tag obrazu = skrót commitu. Bramka: commit musi być aktualnym `main` (ponowne uruchomienie starego CI nie nadpisze nowszego kodu), a przy uruchomieniu ręcznym CI dla tego commitu musi mieć wynik `success`.
- Kolejność: obrazy api i web → push do Artifact Registry → job migracji z nowym obrazem (`--wait`; błąd zatrzymuje wdrożenie) → `fuelclub-api` → `fuelclub-web` (każdy z `--to-latest`, co znosi ręcznie przypięty ruch) → smoke test z ponowieniami i limitem czasu (`/login` 200, `/api/auth/me` przez web 401, api z internetu 404).
- Nieudane wdrożenie albo smoke test: ruch wraca na rewizje sprzed wdrożenia (zapamiętane przed migracją).
- Jedno wdrożenie naraz, bez przerywania w trakcie (`concurrency`, `cancel-in-progress: false`).
- Uwierzytelnienie bez kluczy: token OIDC GitHuba wymieniany przez Workload Identity Federation (pula `github`, dostawca `github-oidc`). Warunek dostawcy: repozytorium `maciekbe1/fuel-club` i `refs/heads/main` - PR ani inna gałąź nie dostaną tokenu.
- Konto `github-deployer` ma tylko: `artifactregistry.writer` na repozytorium `fuelclub`, `run.developer` w projekcie, `iam.serviceAccountUser` na kontach `fuelclub-api` i `fuelclub-web`. Bez dostępu do Secret Managera i bazy - sekrety czyta rewizja, nie wdrożenie.

## Alternatywy

- **Klucz JSON konta usługi w sekretach GitHuba**: prostszy, ale długowieczny klucz do wycieku i rotacji.
- **Cloud Build trigger z GitHuba**: wdrożenie poza CI, osobne uprawnienia i drugi system do obserwowania.
- **Job `deploy` w `ci.yml`**: `cancel-in-progress` CI przerwałby wdrożenie w trakcie migracji.

## Konsekwencje

- Brak środowiska staging i canary: zielone CI idzie od razu na produkcję demo. Rollback: przełączenie ruchu na poprzednią rewizję (automatycznie po nieudanym smoke teście albo ręcznie `gcloud run services update-traffic ... --to-revisions <rewizja>=100`).
- Migracje muszą być zgodne wstecz: przez chwilę stara rewizja api działa na nowym schemacie, a rollback rewizji nie cofa migracji. Job migracji zostaje na nowym obrazie.
- Kontrakt HTTP musi być zgodny wstecz przez jedno wydanie: api wdraża się przed web, więc przez chwilę stary web rozmawia z nowym api.
- Dostawca WIF sprawdza `repository` i `ref`, nie `sub` - `environment: production` zmienia `sub`, ale nie blokuje tokenu.
- Evale na Gemini nie są bramką wdrożenia (koszt, klucz) - tylko evale na modelu demo w CI.
