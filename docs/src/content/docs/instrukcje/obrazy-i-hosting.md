---
title: Obrazy i hosting
description: Budowa obrazów, lokalna próba produkcyjna, kopia i odtworzenie bazy, wdrożenie na Cloud Run.
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

## Cloud Run

:::caution[Planowane]
Docelowy hosting od 2026-11-23 (zamiast Railway). Wdrożenie wymaga projektu Google Cloud i decyzji o kosztach - nie zostało wykonane. Punkty oznaczone „sprawdź” trzeba potwierdzić w aktualnej dokumentacji Google Cloud przy wdrożeniu.
:::

Cloud Run to tylko hosting aplikacji. Model nadal idzie przez Gemini API z kluczem (`Assistant:Gemini:ApiKey`); dostęp przez Vertex AI to osobna decyzja - [Stan wymagań](/referencja/stan-wymagan/#vertex-ai).

Zasoby w jednym projekcie i regionie:

1. **Cloud SQL for PostgreSQL**: prywatny adres IP, bez publicznego. Rozszerzenie `pg_trgm` (regulamin, [ADR 0016](/decyzje/0016-regulamin-w-czacie/)) - sprawdź listę obsługiwanych rozszerzeń. Kopie: automatyczne Cloud SQL (sprawdź retencję) albo `pg_dump` jak wyżej.
2. **api**: usługa Cloud Run z obrazem `apps/api/Dockerfile`, ingress „internal” (bez publicznego adresu). Zmienne jak w próbie produkcyjnej; sekrety (`ConnectionStrings__DefaultConnection`, `Assistant__PseudonymKey`, `Assistant__Gemini__ApiKey`, `DataProtection__Certificate*`, `BootstrapAdmin__*`, `BootstrapCashier__*`) z Secret Managera, nie w zmiennych jawnych. `ForwardedHeaders__KnownNetworks__0` - zakres podsieci, z której wychodzi web (sprawdź).
3. **migracje**: Cloud Run Job z `./efbundle`, uruchamiany przed nową rewizją api. API nie migruje samo.
4. **web**: usługa Cloud Run z obrazem `apps/web/Dockerfile`, publiczny adres HTTPS (albo własna domena). Ruch do api przez Direct VPC egress na wewnętrzny adres usługi api (`API_INTERNAL_URL`, sprawdź format adresu przy ingress „internal”). `TRUST_FORWARDED_FOR=true` tylko po sprawdzeniu, że brzeg Google ustawia `X-Forwarded-For` i `X-Forwarded-Proto`.

Do sprawdzenia przed wdrożeniem:

- **SSE czatu**: odpowiedź strumieniowa bez buforowania, limit czasu żądania powyżej `Assistant:TurnTimeout` (sprawdź limit Cloud Run i ustawienie timeoutu usługi).
- **SignalR** (konsultant, [ADR 0014](/decyzje/0014-signalr-rozmowa-z-konsultantem/)): WebSocket przez Cloud Run; przy więcej niż jednej instancji api potrzebne „session affinity” albo backplane - na start jedna instancja (`max-instances=1`) albo affinity (sprawdź).
- **Data Protection**: klucze w bazie, certyfikat stały z Secret Managera - restart rewizji nie wylogowuje.
- **Skala do zera**: worker zgłoszeń i retencji działa tylko w uruchomionej instancji; na demo `min-instances=1` dla api albo akceptacja opóźnień (sprawdź koszt).

Po wdrożeniu, na telefonie:

- logowanie;
- karta z QR;
- kasa: skan kamerą kodu karty i kuponu (kamera wymaga HTTPS);
- czat: odpowiedź pojawia się w trakcie, a nie po końcu - brzeg nie może buforować SSE;
- wylogowanie;
- restart usługi api nie wylogowuje.
