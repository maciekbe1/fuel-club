---
title: Obrazy i hosting
description: Budowa obrazów, lokalna próba produkcyjna, kopia i odtworzenie bazy, wdrożenie na Railway.
sidebar:
  order: 7
---

Decyzje: [ADR 0010](/decyzje/0010-obrazy-i-hosting/), limity i adres klienta: [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/#limity).

## Obrazy

```sh
docker build -f apps/api/Dockerfile -t fuelclub-api .
docker build --build-arg API_INTERNAL_URL=http://<prywatny-adres-api>:8080 -t fuelclub-web apps/web
```

`API_INTERNAL_URL` jest wbudowywany przy budowaniu (rewrite `/api/*`). Zmiana adresu API wymaga nowego obrazu web.

## Certyfikat kluczy Data Protection

Poza Development wymagany. Wygeneruj raz i trzymaj w menedżerze sekretów:

```sh
openssl req -x509 -newkey rsa:2048 -nodes -keyout dp.key -out dp.crt -days 1825 -subj "/CN=FuelClub DataProtection"
openssl pkcs12 -export -inkey dp.key -in dp.crt -out dp.pfx -passout pass:<haslo-pfx>
base64 -i dp.pfx | tr -d '\n'     # -> DataProtection__Certificate; hasło -> DataProtection__CertificatePassword
```

Wymiana certyfikatu unieważnia sesje i potwierdzenia czatu. Wygaśnięcie certyfikatu nie przeszkadza w odszyfrowaniu.

## Lokalna próba produkcyjna

Sprawdzone 2026-11-22. Prywatna sieć, baza bez publicznego portu, API w `Production`, web na `127.0.0.1:3400`:

```sh
docker network create fc
docker run -d --name fc-db --network fc -e POSTGRES_DB=fuelclub -e POSTGRES_USER=fuelclub -e POSTGRES_PASSWORD=<haslo> postgres:17
CS='Host=fc-db;Database=fuelclub;Username=fuelclub;Password=<haslo>'
docker run --rm --network fc -e ConnectionStrings__DefaultConnection="$CS" --entrypoint ./efbundle fuelclub-api
docker run -d --name fc-api --network fc -e ConnectionStrings__DefaultConnection="$CS" \
  -e Assistant__PseudonymKey="$(openssl rand -base64 32)" \
  -e DataProtection__Certificate="<base64-pfx>" -e DataProtection__CertificatePassword="<haslo-pfx>" \
  -e ForwardedHeaders__KnownNetworks__0=172.16.0.0/12 fuelclub-api
docker run -d --name fc-web --network fc -e TRUST_FORWARDED_FOR=true -p 127.0.0.1:3400:3000 fuelclub-web
```

`TRUST_FORWARDED_FOR=true` jest tu tylko dlatego, że próba sama podaje `X-Forwarded-Proto: https`, tak jak zrobiłby brzeg.

Wynik próby:

- rejestracja `201`, logowanie `204`;
- ciasteczka sesji i CSRF z `Secure` i `HttpOnly`;
- czat `text/event-stream` przez Next standalone: `Content-Encoding: identity`, `Cache-Control: no-store, no-transform`, kolejno `meta`, `tool`, `delta`, `done`;
- strony `/` i `/karta` renderują dane z API (Server Components);
- klucze w bazie zaszyfrowane (`EncryptedXml`, bez jawnego `masterKey`);
- sesja ważna po restarcie kontenera API;
- bez certyfikatu kontener kończy się błędem (`DataProtection:Certificate is required…`, kod 134);
- w logach tylko oczekiwane ostrzeżenie `HttpsRedirectionMiddleware` (żądania bez `X-Forwarded-Proto`).

Przeglądarka po zwykłym `http://` nie odeśle ciasteczek `Secure`. Lokalnie logowanie przez obraz w przeglądarce wymaga HTTPS przed Nextem.

## Kopia i odtworzenie

```sh
docker exec fc-db pg_dump -U fuelclub -d fuelclub -Fc -f /tmp/fuelclub.dump
docker exec fc-db createdb -U fuelclub fuelclub_restore
docker exec fc-db pg_restore -U fuelclub -d fuelclub_restore --no-owner /tmp/fuelclub.dump
```

Sprawdzone:

- te same liczby wierszy (użytkownicy, klucze, historia migracji, ślady tur);
- API na odtworzonej bazie z tym samym certyfikatem przyjęło sesję sprzed kopii.

Kopia zawiera dane osobowe i zaszyfrowane klucze - przechowuj ją jak bazę, a certyfikat osobno.

## Railway

:::caution[Planowane]
Wdrożenie wymaga konta i decyzji o kosztach. Nie zostało wykonane. Punkty oznaczone „sprawdź” trzeba potwierdzić w aktualnej dokumentacji i panelu Railway przy wdrożeniu.
:::

Usługi w jednym projekcie:

1. **PostgreSQL**: baza Railway, bez publicznego dostępu. Kopie: wbudowane (sprawdź plan) albo `pg_dump` jak wyżej.
2. **api**: obraz z `apps/api/Dockerfile`, bez publicznej domeny, tylko sieć prywatna. Zmienne:
   - `ConnectionStrings__DefaultConnection` - z referencji do bazy, w formacie Npgsql (`Host=…;Database=…;Username=…;Password=…`), nie URL;
   - `Assistant__PseudonymKey` - `openssl rand -base64 32`, stały;
   - `DataProtection__Certificate`, `DataProtection__CertificatePassword` - jak wyżej, stałe;
   - `BootstrapAdmin__*`, `BootstrapCashier__*` - konta personelu;
   - `ForwardedHeaders__KnownNetworks__0` - zakres sieci prywatnej, w której działa web (sprawdź adresację, prawdopodobnie IPv6);
   - migracje: polecenie przed wdrożeniem `./efbundle` (sprawdź „pre-deploy command”).
3. **web**: obraz z `apps/web/Dockerfile` z `API_INTERNAL_URL=http://<api>.railway.internal:8080` (sprawdź nazwę hosta prywatnego), publiczna domena z HTTPS. Zmienne:
   - `TRUST_FORWARDED_FOR=true` - tylko gdy brzeg Railway ustawia `X-Forwarded-For` z adresem klienta i `X-Forwarded-Proto` (sprawdź). Bez tego limit logowań jest wspólny, a ciasteczka nie mają `Secure`;
   - `API_INTERNAL_URL` także w czasie działania, jeśli inny niż przy buildzie.

Po wdrożeniu, na telefonie:

- logowanie;
- karta z QR;
- kasa: skan kamerą kodu karty i kuponu (kamera wymaga HTTPS);
- czat: odpowiedź pojawia się w trakcie, a nie po końcu - brzeg nie może buforować SSE;
- wylogowanie;
- restart usługi api nie wylogowuje.
