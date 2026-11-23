# Fuel Club

Demo programu lojalnościowego stacji paliw:

- karta klienta z QR i punkty za tankowanie przy kasie;
- wymiana punktów na kupony, aktywacja i jednorazowy odbiór przy zakupie;
- czat z asystentem (SSE) z potwierdzaniem aktywacji przyciskiem;
- ślady tur, evale, reguły anomalii i panel admina.

Stos:

- API: ASP.NET Core 10, modularny monolit, PostgreSQL 17.
- Web: Next.js 16.
- Dokumentacja: Astro Starlight (`docs/`).

Czat działa lokalnie na deterministycznym adapterze demo - bez kluczy i bez chmury. Opcjonalnie Gemini:

```sh
dotnet user-secrets set "Assistant:Provider" "gemini" --project apps/api
dotnet user-secrets set "Assistant:Gemini:ApiKey" "<klucz>" --project apps/api
# powrót do demo: dotnet user-secrets remove "Assistant:Provider" --project apps/api
```

## Wymagania

.NET SDK 10, Node.js 22, Docker.

## Świeży start

```sh
git clone <repo> fuel-club && cd fuel-club
docker compose up -d postgres                 # PostgreSQL :5433, baza fuelclub
dotnet tool restore
dotnet restore                                # dotnet ef wymaga przywróconych pakietów

# Konfiguracja API poza repozytorium (user-secrets); lista zmiennych: .env.example
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5433;Database=fuelclub;Username=fuelclub;Password=fuelclub_local" --project apps/api
dotnet user-secrets set "DemoAccounts:Enabled" "true" --project apps/api
dotnet user-secrets set "DemoAccounts:Password" "<haslo-demo>" --project apps/api   # min. 10 znaków, cyfra, wielka i mała litera, znak specjalny

dotnet ef database update --project apps/api  # API nie migruje bazy samo
dotnet run --project apps/api --launch-profile http          # http://localhost:5271, UI endpointów: /scalar
```

W drugim terminalu:

```sh
cd apps/web && npm ci && npm run dev          # http://localhost:3000, /api/* -> API
```

### Konta demo

Hasło: to z `DemoAccounts:Password`.

| E-mail | Rola |
| --- | --- |
| `anna@demo.fuelclub.test` | klient |
| `bartek@demo.fuelclub.test` | drugi klient (izolacja) |
| `kasjer@demo.fuelclub.test` | kasjer, stanowisko `DEMO-01` |
| `admin@demo.fuelclub.test` | admin ofert i obserwowalności |

Telefon w sieci lokalnej (kamera do skanowania wymaga HTTPS): `npx next dev --experimental-https -H 0.0.0.0` - szczegóły w docs, „Kasjer i konta demo”.

## Scenariusz demonstracji

1. **Anna** (telefon): „Karta” → „Wydaj kartę”.
2. **Kasjer** (drugie okno lub urządzenie): „Kasa” → zeskanuj lub wpisz kod karty, kwota 600 zł, litry, paragon → „Zapisz zakup”. Przyznano 600 pkt.
3. **Admin**: „Administracja ofert” → oferta za 500 pkt, 0,20 zł/l do 50 l.
4. **Anna**: „Oferty” → „Wymień punkty” → potwierdzenie kosztu i warunków.
5. **Anna**: „Asystent” → „Aktywuj mój kupon” → karta potwierdzenia → „Aktywuj kupon”. Kod jest w „Moje kupony” (QR), nigdy w czacie.
6. **Kasjer**: karta, kwota 100 zł, 40 l, kod kuponu. Warunki pojawiają się od razu, „Przelicz rabat” pokazuje 8,00 zł. Potem „Zapisz zakup”. Ponowny zapis tym samym kuponem jest odrzucony.
7. **Anna**: kupon „Wykorzystany”; w historii rabat i kwota zapłacona.
8. **Admin**: „Obserwowalność” → metryki czatu, reguły anomalii, tury do przejrzenia.

Ten sam przebieg sprawdza automatycznie `apps/web/e2e/demo.spec.ts` (telefon i desktop).

## Kontrole

Bazy testowe (kontener `postgres-test`, :5434) są oddzielne od bazy dev.

```sh
docker compose up -d postgres-test
FUELCLUB_TEST_DATABASE='Host=localhost;Port=5434;Database=fuelclub_test;Username=fuelclub_test;Password=fuelclub_test' \
  dotnet test apps/api.tests                   # API + evale asystenta (assistant-evals-v2)
dotnet csharpier check apps
cd apps/web
npm run typecheck && npm run lint && npm run format:check && npm run build
npm run e2e                                    # Playwright: własne API :5281 i Next :3100, baza fuelclub_e2e
npm run api:types                              # przy działającym API - po każdej zmianie kontraktu
cd ../../docs && npm ci && npm run build       # dokumentacja
```

CI (`.github/workflows/ci.yml`) uruchamia to samo na własnym PostgreSQL. Dodatkowo sprawdza, czy `schema.d.ts` zgadza się z OpenAPI.

## Dokumentacja

```sh
cd docs && npm ci && npm run dev
```

Zawiera moduły, referencję (endpointy, kody błędów, konfiguracja, baza), instrukcje i decyzje (ADR). Odbiór MVP - kryteria i dowody: `docs/src/content/docs/referencja/odbior-mvp.md`.

## Stan

**Gotowe lokalnie:**

- konta i role;
- karta, kasa, punkty i historia;
- oferty, wymiana, kupony i odbiór;
- panel admina ofert;
- czat z narzędziami i potwierdzeniem;
- ślady i evale;
- zdarzenia bezpieczeństwa, anomalie, kolejka przeglądu i retencja;
- limity.

**Obrazy:** `apps/api/Dockerfile`, `apps/web/Dockerfile`. Lokalna próba produkcyjna, kopia i odtworzenie bazy oraz instrukcja Railway są w docs, „Obrazy i hosting”.

**Planowane / niewykonane:**

- wdrożenie na hosting (wymaga konta);
- ocena tur przez judge (P7c - wymaga klucza dostawcy);
- alerty Slack.
