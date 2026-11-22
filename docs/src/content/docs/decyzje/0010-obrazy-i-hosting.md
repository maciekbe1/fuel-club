---
title: "0010 - Obrazy, klucze i nagłówki za proxy"
description: Obrazy Docker API i web, migracje jako osobny program, klucze Data Protection w bazie, zaufanie do X-Forwarded-* od proxy.
sidebar:
  order: 10
---

## Status

Decyzja przyjęta (2026-11-22), P9. Obrazy i konfiguracja sprawdzone lokalnie. Wdrożenie na hosting nie zostało wykonane - brak konta.

## Problem

Hosting (SPEC §4) wymaga:

- obrazów;
- migracji poza startem API;
- sesji, tokenów CSRF i potwierdzeń czatu, które przeżyją wdrożenie i drugą instancję;
- bezpiecznych ciasteczek, gdy przeglądarka mówi HTTPS do brzegu, a Next woła API po HTTP w sieci prywatnej.

Przed P9:

- klucze Data Protection leżały w systemie plików procesu, więc nowy kontener je gubił;
- API nie wiedziało, że żądanie przyszło po HTTPS - ciasteczka nie dostawały flagi `Secure`.

## Rozwiązanie

- **Klucze Data Protection w PostgreSQL** (`PersistKeysToDbContext`, tabela `DataProtectionKeys`, nazwa aplikacji `FuelClub`), **szyfrowane certyfikatem**.
  - Certyfikat: `DataProtection:Certificate` (base64 PKCS#12) i `CertificatePassword`, ze zmiennych środowiskowych. Baza i jej kopie nie zawierają użytecznego klucza, a sekret jest tylko w środowisku (reguła z `CLAUDE.md`).
  - Poza Development brak certyfikatu zatrzymuje start (walidacja przy starcie hosta). Bundle migracji buduje host bez startu, więc certyfikatu nie potrzebuje.
  - W Development klucze są niezaszyfrowane (ostrzeżenie `XmlKeyManager[35]`).
- **`X-Forwarded-For` i `X-Forwarded-Proto`** są przyjmowane tylko od zaufanych proxy (`ClientAddress.cs`, wartości parsowane przy starcie - zła wartość zatrzymuje start):
  - loopback;
  - `ForwardedHeaders:KnownProxies` (adresy);
  - `ForwardedHeaders:KnownNetworks` (zakresy CIDR - Next w innym kontenerze nie ma stałego adresu).
- Za brzegiem HTTPS żądanie jest więc HTTPS i ciasteczka (`SecurePolicy` `SameAsRequest`) dostają `Secure`.
- Next przekazuje oba nagłówki tylko przy `TRUST_FORWARDED_FOR=true`. Bez tego `proxy.ts` je usuwa, bo klient mógłby je podać sam ([ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/#limity)).
- **Obraz API** (`apps/api/Dockerfile`, kontekst: korzeń repo):
  - `dotnet publish` + bundle migracji `efbundle`, uruchamiany przed nową wersją: `--entrypoint ./efbundle` i `ConnectionStrings__DefaultConnection`;
  - środowisko `Production`, port 8080, użytkownik bez roota;
  - `libgssapi-krb5-2` (Npgsql bez niej loguje błąd przy połączeniu);
  - `tini` jako PID 1. Bez niego nieudany start (wyjątek → `abort`) zostawiał kontener „Up” z martwą aplikacją, bo PID 1 bez handlera ignoruje ten sygnał (sprawdzone).
- **Obraz web** (`apps/web/Dockerfile`, kontekst: `apps/web`):
  - `output: "standalone"` (gdy `NEXT_OUTPUT=standalone`), `node server.js`, użytkownik `node`;
  - `API_INTERNAL_URL` to argument builda, ustawiany też w etapie uruchomieniowym. Rewrite `/api/*` jest ustalany przy budowaniu, a Server Components (`lib/server/api.ts`) czytają ten adres w czasie działania;
  - `.env*` są wykluczone z kontekstu builda: standalone kopiowałby je do obrazu.
- Logi zapytań EF (`Microsoft.EntityFrameworkCore.Database.Command`) na poziomie `Warning` poza Development (`Information` w `appsettings.Development.json`). Worker obserwowalności co minutę zapełniałby nimi log.

## Alternatywy

- **Wymuszenie `Secure` poza Development**: antiforgery odmawia wydania tokenu przy żądaniu, które API widzi jako HTTP (`500` na `/api/auth/csrf`, sprawdzone na obrazie). Odrzucone.
- **Klucze niezaszyfrowane w bazie**: kopia bazy pozwalałaby podrobić sesje. Odrzucone w review P9.
- **Klucze w wolumenie**: wymaga trwałego dysku przy każdej instancji, a backup bazy go nie obejmuje. Baza jest już kopiowana.
- **Migracje przy starcie API**: sprzeczne z [ADR 0003](/decyzje/0003-migracje/). Kilka instancji naraz migrowałoby jedną bazę.

## Konsekwencje

- Backup bazy obejmuje zaszyfrowane klucze. Po odtworzeniu z tym samym certyfikatem sesje i potwierdzenia dalej działają (sprawdzone). Utrata certyfikatu unieważnia wszystkie sesje i potwierdzenia: certyfikat trzymaj w menedżerze sekretów, osobno od kopii.
- `UseHttpsRedirection` loguje ostrzeżenie o braku portu HTTPS przy żądaniach bez `X-Forwarded-Proto` - oczekiwane, TLS kończy brzeg.
- Konta demo działają tylko w Development. Na hostingu personel tworzą `BootstrapAdmin` i `BootstrapCashier`, a klienci się rejestrują.
