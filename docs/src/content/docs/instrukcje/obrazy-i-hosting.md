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

Wdrożone 2026-11-24 w osobnym projekcie `fuelclub-demo-a0999e`, region `europe-central2`. Adres: `https://fuelclub-web-407499358574.europe-central2.run.app`.

Cloud Run to tylko hosting aplikacji. Model nadal idzie przez Gemini API z kluczem (`Assistant:Gemini:ApiKey`); dostęp przez Vertex AI to osobna decyzja - [Stan wymagań](/referencja/stan-wymagan/#vertex-ai).

### Zasoby

| Zasób | Ustawienia |
| --- | --- |
| Cloud SQL `fuelclub-db` | PostgreSQL 17, `db-f1-micro`, 10 GB SSD, tylko prywatny IP (peering `google-managed-services-default`), kopie codziennie 02:00, 7 kopii. Połączenie z `SSL Mode=Require`. |
| Artifact Registry `fuelclub` | obrazy `api:<sha>` i `web:<sha>`, budowane w GitHub Actions (`deploy.yml`); ręcznie Cloud Build (`deploy/cloudbuild-*.yaml`) |
| Secret Manager | `db-connection`, `pseudonym-key`, `gemini-api-key`, `dp-certificate`, `dp-certificate-password`, `admin-password`, `cashier-password`, `support-password`; dostęp tylko konto usługi `fuelclub-api` |
| Job `fuelclub-migrate` | `./efbundle` z obrazu api, Direct VPC egress; uruchamiany przed nową rewizją api |
| Usługa `fuelclub-api` | ingress `internal`, Direct VPC egress (podsieć `default`, tylko zakresy prywatne), `min=max=1` instancja (SignalR, workery), CPU naliczane w trakcie żądań, timeout 300 s |
| Usługa `fuelclub-web` | publiczna, Direct VPC egress `all-traffic` (żądania do `*.run.app` api liczą się jako wewnętrzne), `min=0`, `max=2`, `TRUST_FORWARDED_FOR=true` |

Konta startowe (hasła tylko w Secret Managerze, odczyt wymaga dostępu do projektu):

| Konto | Rola | Hasło |
| --- | --- | --- |
| `admin@demo.fuelclub.test` | `Admin` | `gcloud secrets versions access latest --secret admin-password --project fuelclub-demo-a0999e` |
| `kasjer@demo.fuelclub.test` | `Cashier`, stanowisko `DEMO-01`, kasa `K1` | `gcloud secrets versions access latest --secret cashier-password --project fuelclub-demo-a0999e` |
| `konsultant@demo.fuelclub.test` | `Support` | `gcloud secrets versions access latest --secret support-password --project fuelclub-demo-a0999e` |

Klient: zwykła rejestracja w aplikacji.

### Wdrożenie nowej wersji

Automatycznie po zielonym CI na `main` ([ADR 0022](/decyzje/0022-cd-na-cloud-run/), `.github/workflows/deploy.yml`): obrazy, migracja, api, web, smoke test. Ręcznie z GitHuba: Actions → Deploy → Run workflow.

Nieudany smoke test sam przełącza ruch na rewizje sprzed wdrożenia. Rollback ręczny: `gcloud run services update-traffic fuelclub-api --project fuelclub-demo-a0999e --region europe-central2 --to-revisions <rewizja>=100` (lista: `gcloud run revisions list --service fuelclub-api`), tak samo `fuelclub-web`. Rollback nie cofa migracji, a job `fuelclub-migrate` zostaje na nowym obrazie - przed ręcznym uruchomieniem joba ustaw obraz rewizji, która serwuje ruch. Kolejne wdrożenie przywraca ruch na najnowszą rewizję (`--to-latest`).

Ręcznie z komputera (awaria Actions):

```sh
P=fuelclub-demo-a0999e R=europe-central2 TAG=$(git describe --always --dirty) REPO=$R-docker.pkg.dev/$P/fuelclub
gcloud builds submit --project $P --region $R --config deploy/cloudbuild-api.yaml --substitutions _TAG=$TAG .
gcloud run jobs update fuelclub-migrate --project $P --region $R --image $REPO/api:$TAG
gcloud run jobs execute fuelclub-migrate --project $P --region $R --wait
gcloud run deploy fuelclub-api --project $P --region $R --image $REPO/api:$TAG
gcloud builds submit --project $P --region $R --config deploy/cloudbuild-web.yaml --substitutions _TAG=$TAG apps/web
gcloud run deploy fuelclub-web --project $P --region $R --image $REPO/web:$TAG
```

Wdrażaj tylko zacommitowany kod: tag to commit, a Cloud Build wysyła katalog roboczy. `git status` musi być czysty (albo tag z `git describe --always --dirty`). Zmienne i sekrety zostają z poprzedniej rewizji. Pełna lista: `gcloud run services describe fuelclub-api`.

### Adres klienta na Cloud Run

Łańcuch do API: brzeg Google przed web (dopisuje adres klienta) → Next (przekazuje `X-Forwarded-For` bez zmian) → wewnętrzny hop Cloud Run (dopisuje `0.0.0.0`) → proxy instancji (gniazdo `169.254.169.126`) → API. Sprawdzone logiem `ForwardedHeadersMiddleware` (Debug): przed zaufaniem zatrzymywał się na `169.254.169.126`, potem na `0.0.0.0`. Z domyślnym `ForwardLimit=1` API brało `0.0.0.0` - wszyscy klienci mieli jeden limit logowań. Sprawdzone: 11 prób z jednego adresu, a próba z drugiego (Cloud Build) w tym samym oknie dostała `429`.

Ustawienia api:

- `ForwardedHeaders__KnownNetworks__0=169.254.0.0/16`;
- `ForwardedHeaders__KnownProxies__0=0.0.0.0`;
- `ForwardedHeaders__ForwardLimit=3`.

Po zmianie, w jednym oknie: pierwszy adres `429`, drugi `401`. Własny `X-Forwarded-For` klienta z internetu nie zdejmuje limitu - brzeg dopisuje prawdziwy adres na prawo (sprawdzone dwoma podrobionymi nagłówkami: `429`).

Gwarancja dotyczy ruchu przez publiczny brzeg web. Inna usługa w tym projekcie, która woła api bezpośrednio (ingress `internal`), też przychodzi z `169.254.x` i jej `X-Forwarded-For` byłby zaufany. Dziś takich usług nie ma; nowa wymaga zawężenia zaufania.

### Rozmowa w trakcie wdrożenia

Pytanie: klient rozmawia z konsultantem albo z asystentem, a w tym czasie wchodzi nowa wersja api. Czy rozmowa się przerwie?

Jak to działa przy wdrożeniu:

- Cloud Run uruchamia nową rewizję i przełącza na nią **nowe** żądania. Stara instancja dokańcza trwające żądania i trzyma otwarte WebSockety.
- Zapis wiadomości i operacje na punktach mają `Idempotency-Key`: ponowienie po urwanym żądaniu nie zapisze niczego drugi raz.
- Treść rozmowy z konsultantem jest zawsze czytana z bazy kursorem (`after=seq`); hub tylko mówi „jest coś nowego”.

Pomiar na Cloud Run (2026-11-24, nowa rewizja api w trakcie rozmowy; skrypty: konsultant wysyła wiadomość co 2 s, klient słucha huba):

| Przypadek | Wynik |
| --- | --- |
| Zapis wiadomości konsultanta (45 w ~100 s, wdrożenie w środku) | 45/45 zapisanych, 0 błędów, 0 duplikatów, 0 braków |
| Powiadomienia huba u klienta przed poprawką | 12/45 - po przełączeniu żadnego; hub **nie** rozłączył się (stara instancja trzyma WebSocket), więc UI nie przechodziło na odczyt co 3 s. Rozmowa stała dla klienta do zamknięcia połączenia (do 300 s) |
| Po poprawce (odczyt kursorem co 10 s także przy połączonym hubie) | 45/45 widocznych u klienta; mediana 3,4 s, maks. 9,8 s od wysłania |
| Czat z asystentem (SSE, 40 tur co 2 s, wdrożenie w środku) | 40/40 tur zakończonych `done`, żadna nie przerwana |

Wnioski:

- Rozmowa nie jest przerywana i nic nie ginie. Najgorszy przypadek przy wdrożeniu: wiadomości konsultanta dochodzą z opóźnieniem do 10 s zamiast natychmiast.
- Wskaźnik „… pisze” i toast o punktach (hub lojalności) w tym oknie mogą nie przyjść - to sygnały pomocnicze, saldo i historia są czytane z API.
- Tura czatu dłuższa niż okno wygaszania starej instancji (ok. 10 s po `SIGTERM` według dokumentacji Cloud Run) mogłaby się urwać; klient widzi wtedy „Połączenie zostało przerwane - odpowiedź jest niepełna” i może ponowić. Typowa tura trwa 1,5-3 s.

Lepsze rozwiązania (nie wdrożone): backplane SignalR (Redis albo PostgreSQL `LISTEN/NOTIFY`), żeby nowa instancja mogła wysyłać do połączeń na starej; zamykanie połączeń starej rewizji zaraz po przełączeniu ruchu.

### Sprawdzone po wdrożeniu (2026-11-24)

- rejestracja `201`, logowanie `204`, ciasteczka sesji i CSRF z `Secure` i `HttpOnly`;
- api z internetu: `404` (ingress `internal`);
- czat przez web: `text/event-stream`, `Content-Encoding: identity`, `Cache-Control: no-store, no-transform`, `meta`, `tool`, `delta`, `done`; pierwszy bajt po 0,27 s, całość 1,7 s (bez buforowania); provider Gemini;
- SignalR: negocjacja `200` (WebSockets, SSE, long polling), handshake WebSocket przez rewrite Next;
- sesja ważna po pięciu nowych rewizjach api;
- wylogowanie `204`: ciasteczka wygaszone z `Secure`, potem `/api/auth/me` → `401`;
- konta startowe logują się z rolami `Admin`, `Cashier`, `Support`;
- migracje z joba, logi startu bez błędów (`Production`).

:::caution[Do sprawdzenia na telefonie]
Karta z QR, skan kamerą przy kasie, czat i rozmowa z konsultantem w przeglądarce - jeszcze nie sprawdzone ręcznie.
:::

Ograniczenia:

- Rewrite Next ma limit 30 s na żądanie (`proxyTimeout` w Next). `Assistant:TurnTimeout` też wynosi 30 s - dłuższego limitu tury nie ustawiaj bez zmiany proxy.
- Workery (retencja, zgłoszenia, reguły anomalii) dostają CPU tylko w trakcie żądań. Na demo to akceptowalne opóźnienie; stały CPU (`--no-cpu-throttling`) to ok. 45 USD/mies. więcej.

### Koszt (szacunek, cennik z 2026-11-24 - sprawdź w rozliczeniach)

Cloud SQL `db-f1-micro` z dyskiem ok. 10-12 USD, api z jedną bezczynną instancją ok. 5-10 USD, web od zera ok. 0-2 USD, rejestr i sekrety poniżej 1 USD. Razem ok. 20 USD/mies. bez ruchu, plus Gemini według użycia.
