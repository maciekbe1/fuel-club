# Fuel Club - plan i kontekst MVP

## Cel

Działające demo aplikacji lojalnościowej stacji paliw. Pokazuje pełny przepływ punktów i kuponów, atomowe operacje, Idempotency-Key oraz bezpieczny czat AI z transmisją SSE.

Asystent implementuje uzgodnione kroki, użytkownik decyduje o zakresie i sprawdza wynik. Każdy krok kończy się testami i aktualizacją dokumentacji.

## Ustalenia

- Folder: `/Users/maciejbednarczyk/Projekty/fuel-club`.
- Jedno repozytorium (monorepo).
- Backend: modularny monolit .NET, bez mikroserwisów.
- Frontend Next.js i backend .NET działają jako dwa procesy.
- Lokalny PostgreSQL. Docker Compose jako proponowany sposób uruchomienia.
- Bez wymaganego dostępu do chmury ani kluczy API.
- Interfejs po polsku, responsywny. Proponowany wygląd: granat, biel, pomarańcz, motyw karty lojalnościowej i paragonu.

## Technologie

- Frontend: Next.js App Router, TypeScript, shadcn/ui, React Hook Form, TanStack Query.
- Backend: ASP.NET Core, EF Core, Npgsql, ASP.NET Core Identity.
- Baza: PostgreSQL.
- Testy: xUnit, integracyjne na prawdziwym PostgreSQL, Playwright dla przepływu UI (`apps/web/e2e`, baza `fuelclub_e2e`).
- AI: lokalny deterministyczny adapter demonstracyjny oraz opcjonalny adapter Gemini API.

## Zakres MVP

### Konto i role

- Logowanie i wylogowanie, sesja w ciasteczku HttpOnly.
- Role docelowe: User (klient), Cashier (kasjer), Admin (zarządzanie ofertami). Cashier jest planowany; obecnie zaimplementowano User i Admin.
- Konta demonstracyjne: dwóch użytkowników do testów izolacji, kasjer oraz administrator - wyłącznie w Development.
- Kasjer powstaje z konfiguracji startowej (analogicznie do administratora), z przypisaną stacją i kasą. Bez automatycznego awansu istniejącego zwykłego konta. Endpoint zarządzania personelem nie jest teraz potrzebny.
- Hasła haszowane przez Identity. Dane startowe tylko w środowisku Development.
- Rejestracja w MVP (nowe konto dostaje rolę użytkownika). Reset hasła i weryfikacja e-mail poza MVP.

### Punkty za tankowanie

- Użytkownik pokazuje kod kreskowy karty lojalnościowej. Kasjer skanuje go, a kasa przesyła dane zakończonego zakupu; backend rozpoznaje konto i nalicza punkty.
- Panel kasjera symuluje kasę: skan lub wklejenie kodu karty, kwota, litry, data sprzedaży i numer paragonu. Wpisanie kodu jest alternatywą dla skanera, nie innym procesem.
- Stacja i kasa są przypisane do konta kasjera i odczytuje je backend. Formularz nie może ich nadpisać. Unikalność paragonu chroni przed duplikatem, nie przed celowym fałszowaniem pozostałych danych paragonu.
- Kod karty: losowy identyfikator o 128 bitach losowości, zapisany jawnie z UNIQUE, bez danych osobowych (email, userId). Nie zastępuje logowania i nie daje dostępu do salda ani kuponów.
- Kod karty: Crockford Base32, 26 znaków. Prezentacja do skanowania lub kopiowania; ręczne wpisanie jako alternatywa (wejście normalizowane: wielkie litery, bez spacji i myślników).
- Zakup i wymiana karty biorą tę samą blokadę klienta (advisory lock, z limitem i anulowaniem). Zakup po uzyskaniu blokady sprawdza kartę ponownie: wymiana zatwierdzona wcześniej → stary kod odrzucony; zakup wcześniej → naliczenie poprawne.
- Po wydaniu lub wymianie frontend pobiera kartę przez GET; odpowiedź odtworzona przez idempotencję opisuje historyczną operację.
- Wydanie karty to jawna operacja POST; odczyt GET niczego nie tworzy. Wydanie i wymiana wymagają Idempotency-Key, żeby ponowienie po utracie odpowiedzi nie unieważniło właśnie wydanej karty.
- Wymiana kodu karty wymaga zalogowanego właściciela i natychmiast unieważnia poprzedni kod. Kodów kart nie logujemy.
- Po zapisie zakupu kasjer widzi ID zakupu i przyznane punkty, bez salda klienta.
- Operacje kasowe wymagają Cashier; sama rola Admin ich nie uprawnia. Prawdziwa integracja POS pozostaje poza MVP.
- Reguła: 1 punkt za każdy pełny 1 zł tankowania, floor(kwota). Kwota 0 < x <= 5000 zł, maks. 2 miejsca po przecinku; litry > 0, maks. 3 miejsca. Wartości jako decimal, bez float.
- Unikalna kombinacja stacja + kasa + data sprzedaży + numer paragonu zapobiega ponownemu naliczeniu również z innym kluczem idempotencji: 409 loyalty.receipt_already_registered, bez informacji o właścicielu. Ten sam klucz z tym samym żądaniem odtwarza pierwotny sukces.
- Data sprzedaży nie może być późniejsza niż dziś w strefie stacji (demo Europe/Warsaw): 400 loyalty.sale_date_in_future. Starsze daty są dozwolone.
- Zakup poniżej 1 zł (0 pkt) jest zapisywany bez wpisu w historii i bez zmiany salda. Idempotencja obowiązuje także dla niego.
- Użytkownik widzi saldo i historię operacji, nie może sam naliczać punktów.

### Kupony

- Administrator tworzy, edytuje i wyłącza oferty: nazwa, opis, koszt punktowy.
- Użytkownik wymienia punkty na własną instancję kuponu.
- Korzyść: rabat na litr (stawka zł/l i limit litrów z oferty), na dowolne paliwo, realizowany razem z zakupem paliwa w jednej transakcji. Punkty od kwoty zapłaconej po rabacie. Szczegóły: ADR 0006.
- Koszt, nazwa, stawka i limit są zapisywane jako snapshot przy wymianie; późniejsza edycja oferty nie zmienia kupionego kuponu. Wyłączenie oferty blokuje nowe wymiany, nie unieważnia wydanych kuponów.
- Cykl życia: Purchased -> Active -> Redeemed.
- Aktywacja nie oznacza odbioru i nie pobiera punktów ponownie.
- Aktywacja tworzy losowy, nieprzewidywalny kod odbioru, widoczny właścicielowi: `K` + 128 bitów w Crockford Base32 (27 znaków).
- Kasjer skanuje kod aktywnego kuponu. Symulator kasy pokazuje korzyść i potwierdza jej zastosowanie do zakupu; backend oznacza kupon jako Redeemed w tej samej transakcji co zapis realizacji.
- Sam skan/podgląd nie zużywa kuponu. Ponowne potwierdzenie nie może naliczyć rabatu drugi raz.
- Demo zapisuje symulację rabatu; nie integruje się z płatnością ani drukarką fiskalną. Nie deklarujemy atomowości z zewnętrzną kasą.
- Odbiór możliwy tylko raz, również przy dwóch równoległych żądaniach i różnych kluczach.
- Karta klienta i aktywny kupon mają osobne kody do skanowania (kod kreskowy lub QR) oraz możliwość wpisania/wklejenia. Kod karty nie pozwala odebrać kuponu.
- Brak zwrotów, wygasania i limitów magazynowych w pierwszej wersji.

### Czat

- Odpowiedzi przesyłane przez SSE; frontend obsługuje strumień z POST przez fetch.
- Narzędzia: get_my_balance, list_available_rewards, list_my_coupons, propose_coupon_activation.
- Tożsamość zawsze z sesji serwera. Narzędzia nie przyjmują userId wybranego przez model.
- Aktywacja przez czat: agent proponuje konkretny własny kupon, użytkownik zatwierdza przyciskiem, serwer wykonuje tę samą usługę domenową co zwykły interfejs.
- Potwierdzenie związane z użytkownikiem i kuponem; samo wygenerowanie tekstu przez model nie jest zgodą.
- Agent nie nalicza punktów, nie zarządza ofertami i nie odbiera kuponów.
- Lokalny adapter obsługuje ograniczony zestaw intencji i jest jawnie oznaczony jako tryb demo, nie prawdziwy LLM.
- Gemini opcjonalne, klucz wyłącznie po stronie backendu. Bez klucza działa cały scenariusz demo.
- Walidacja argumentów, lista dozwolonych narzędzi, limit wywołań i czasu; odpowiedź modelu jest niezaufanym wejściem.

## Struktura repozytorium

```text
apps/
  web/                  Next.js (architektura: docs/src/content/docs/decyzje/0001-architektura-frontendu.md)
  api/                  ASP.NET Core, moduły w Modules/<Nazwa>
  api.tests/            testy integracyjne na PostgreSQL
tests/                  scenariusze E2E i evals
docs/                   Astro Starlight: nauka, instrukcje, referencja, wyjaśnienia, moduły, decyzje (ADR)
compose.yml
.env.example
README.md
PLAN-MVP.md
CLAUDE.md
```

Podział na moduły logiczne: Accounts, Loyalty, Rewards, Assistant, Observability. Projekty .NET można wydzielać stopniowo, bez rozbudowy architektury przed pierwszym działającym przepływem.

## Dane i niezmienniki

- Identity users/roles: konta i uprawnienia.
- LoyaltyCard (planowana): właściciel, kod (128 bitów losowości, jawnie, UNIQUE), daty utworzenia i unieważnienia. Jeden aktywny kod na użytkownika; wymiana unieważnia poprzedni.
- CashierStation (planowana): kasjer, stacja, kasa - przypisanie z konfiguracji, źródło StationCode i RegisterCode zakupu.
- LoyaltyAccount: userId, balance, wersja współbieżności (xmin). Saldo nigdy ujemne.
- PointsLedger: userId, delta, typ, identyfikator źródła, data. Dziennik niezmienny.
- FuelPurchase: klient (z karty), wykonawca (kasjer z sesji), kwota, litry, przyznane punkty; paragon identyfikuje stacja + kasa + data sprzedaży + numer (unikalne razem).
- RewardDefinition: oferta, koszt, aktywność.
- UserCoupon: właściciel, oferta, snapshot, status, kod, daty aktywacji i odbioru.
- IdempotencyRecord: użytkownik wykonujący operację, klucz, fingerprint żądania, status HTTP i odpowiedź.
- ChatTrace/ToolTrace: correlationId, pseudonimowy identyfikator użytkownika, narzędzie, wynik, czas i sygnały anomalii.

Saldo i wpis ledger zmieniają się w jednej transakcji. Zmiana stanu kuponu wymaga poprawnego stanu poprzedniego. Ograniczenia UNIQUE i CHECK w bazie stanowią ostatnią linię ochrony.

## Atomowość i Idempotency-Key

Wymagane dla tankowania, wydania i wymiany karty, wymiany punktów, aktywacji i odbioru:

1. Klient generuje UUID dla jednej logicznej operacji i zachowuje go przy ponowieniu po błędzie sieci.
2. Serwer uwierzytelnia, autoryzuje i sprawdza CSRF - także przed odtworzeniem zapisanej odpowiedzi.
3. Klucz jest unikalny w obrębie zalogowanego wykonawcy; fingerprint obejmuje metodę, szablon i wartości trasy, query i kanoniczną treść JSON. JSON z powtórzonymi nazwami pól (także różniącymi się wielkością liter) jest odrzucany.
4. Rezerwacja klucza, savepoint, zmiana domenowa i zapis odpowiedzi następują w jednej transakcji PostgreSQL. Błąd domenowy (4xx poza 400/401/403/408/429) wycofuje zmiany do savepointu, rezerwacja zostaje, odpowiedź jest zapisywana. 400 (walidacja) i 5xx nie są zapisywane.
5. Ten sam klucz i fingerprint zwracają zapisany status, Content-Type, Location i treść (nigdy cookies ani nagłówki uwierzytelnienia). Inny fingerprint: 409 idempotency.key_reused.
6. Równoległe żądania synchronizuje unikalność klucza; drugie czeka na pierwsze z limitem (`lock_timeout`) i anulowaniem, potem odczytuje wynik w nowej transakcji. Przekroczenie limitu: 409 idempotency.in_progress (niezapisywane), klient ponawia z tym samym kluczem.
7. Naliczenie punktów: jedno zapytanie `INSERT INTO LoyaltyAccounts (UserId, Balance) VALUES (@user, @points) ON CONFLICT (UserId) DO UPDATE SET Balance = LoyaltyAccounts.Balance + EXCLUDED.Balance RETURNING Balance`. Dodaje punkty do istniejącego salda albo tworzy konto, blokuje wiersz do końca transakcji (równoległe naliczenia czekają, nie gubią się) i zwraca saldo po zmianie, które trafia do BalanceAfter. Wydawanie: warunkowy UPDATE balance >= cost ... RETURNING; brak wiersza oznacza brak punktów, nie ujemne saldo.
8. Warunkowy UPDATE kuponu WHERE status = Active zabezpiecza jednorazowy odbiór z różnymi kluczami.

Ponowienie odebranego kuponu tym samym kluczem zwraca pierwotny sukces. Nowy klucz nie może spowodować drugiego odbioru. Błędy infrastrukturalne wycofują transakcję; klient może bezpiecznie powtórzyć operację. Retencję kluczy trzeba jawnie ustalić przed produkcją; MVP ich nie usuwa.

## API - proponowane granice

- GET /api/auth/csrf, POST /api/auth/register, POST /api/auth/login, POST /api/auth/logout, GET /api/auth/me
- GET /api/me/wallet, GET /api/me/transactions
- GET /api/rewards, GET /api/me/coupons
- POST /api/rewards/{id}/exchange
- POST /api/me/coupons/{id}/activate
- GET /api/me/loyalty-card - tylko odczyt; brak karty: 404
- POST /api/me/loyalty-card - wydanie karty (Idempotency-Key)
- POST /api/me/loyalty-card/rotate - wymiana kodu (Idempotency-Key)
- GET /api/cashier/station - stanowisko zalogowanego kasjera
- POST /api/cashier/fuel-purchases - kod karty i dane zakupu; konto klienta ustala backend z karty, stację i kasę z konta kasjera
- GET /api/admin/rewards, GET /api/admin/rewards/{id}
- POST /api/admin/rewards (Idempotency-Key), PUT /api/admin/rewards/{id} - cały formularz z `version`, także włączanie i wyłączanie; nieaktualna wersja → 409
- POST /api/cashier/coupons/preview - podgląd korzyści po skanie, bez zużycia kuponu
- POST /api/cashier/redemptions - potwierdzenie realizacji w symulatorze kasy
- POST /api/chat - SSE: meta, tool, delta, confirmation, done, error
- GET /api/admin/observability - ograniczony podgląd metryk i anomalii

Identyfikator z URL nie zastępuje kontroli właściciela. Dostęp do cudzego kuponu kończy się bez ujawniania jego danych.

## Bezpieczeństwo

- Frontend i API dostępne dla przeglądarki pod wspólnym origin przez proxy; prywatny adres API skonfigurowany po stronie Next.js.
- HttpOnly, SameSite i Secure przy HTTPS; ochrona CSRF dla operacji modyfikujących, również logowania.
- Oddzielna autoryzacja Admin i Cashier na backendzie, nie tylko ukrywanie przycisków. Kasjer nie ma uprawnień do administracji ofertami ani przeglądania wszystkich kont.
- Kod karty identyfikuje odbiorcę punktów; wykonawcę operacji, stację, kasę i audyt ustala sesja kasjera, nigdy dane z formularza.
- Limity prób logowania, odbioru kodu i czatu.
- Brak haseł, cookies, kluczy API, kodów kart i kodów odbioru w logach.
- Tekst rozmów: domyślnie metadane i kategorie intencji. Opcjonalny zapis zredagowanej treści tylko w trybie deweloperskim, z retencją.
- Sekrety w zmiennych środowiskowych, .env poza repozytorium.

## Evals, logi i anomalie

Wersjonowane scenariusze agenta: saldo, dostępne nagrody, brak punktów, własny kupon, cudzy kupon, próba nadania punktów, prompt injection, niepoprawne argumenty, brak potwierdzenia i awaria dostawcy.

Sprawdzamy wybrane narzędzie, zakres danych, wynik domenowy oraz brak niedozwolonych skutków. Lokalne evals deterministyczne w CI; testy jakości Gemini oddzielnie i tylko po podaniu klucza.

Logi strukturalne z correlationId, czasem narzędzia, wynikiem i trybem dostawcy. Proste reguły wykrywają: serię odmów autoryzacji, częste konflikty kluczy, powtarzane próby odbioru, przekroczenia limitów narzędzi i błędy AI. Progi konfigurowalne; bez deklarowania pełnej detekcji oszustw.

## Etapy implementacji

1. Szkielet repo, .NET API, Next.js, PostgreSQL, healthcheck, .env.example i README.
2. Migracje Identity, konta demo, logowanie, sesja i role. Test izolacji dwóch użytkowników.
3. Rola Cashier, karta lojalnościowa, saldo, ledger i tankowanie przez symulator kasy. Kolejność: 3.1 rola Cashier i stanowisko (zrobione) → 3.3 infrastruktura Idempotency-Key (zrobione) → 3.2 karta (zrobione) → 3.4 zakup przez kasę (zrobione) → 3.5 odczyt salda i historii (zrobione). Testy uprawnień, rozpoznania karty, ponowień i paragonu.
4. Oferty i wymiana punktów z transakcją oraz idempotencją. Test równoległego wydawania.
5. Aktywacja, skan kodu i realizacja przez kasjera. Test jednorazowego rabatu przy różnych kluczach.
6. Interfejs: konto i karta, saldo/historia, katalog, moje kupony, panel kasjera i osobny panel administratora.
7. Lokalny agent, SSE, narzędzia i potwierdzenie aktywacji.
8. Evals, logi, anomalie, scenariusz demonstracji i test E2E.
9. Opcjonalny adapter Gemini, bez blokowania lokalnego uruchomienia.

Po każdym etapie: uruchomienie, krótki test zachowania, dopiero potem kolejny etap. Typy API (`npm run api:types`) regenerujemy przy każdej zmianie kontraktu, nie na końcu etapu.

## Kryteria ukończenia MVP

- Nowa osoba uruchamia aplikację według README, bez kluczy chmurowych.
- Kasjer skanuje kartę klienta i zatwierdza zakup w symulatorze; punkty trafiają do właściwego konta. Użytkownik widzi saldo i historię.
- User oraz konto mające wyłącznie Admin nie mogą wykonać operacji Cashier.
- Wymiana pobiera punkty raz i wydaje jeden kupon na operację.
- Równoległe wydawanie nie daje ujemnego salda.
- Aktywacja nie jest odbiorem; kasjer skanuje aktywny kupon i potwierdza zastosowanie korzyści dokładnie raz.
- Powtórzony klucz odtwarza wynik; zmieniony payload powoduje 409.
- Drugie konto nie odczytuje ani nie zmienia pierwszego, także przez narzędzia czatu.
- SSE pokazuje postęp i wynik, a błędy nie udają sukcesu.
- Agent aktywuje kupon dopiero po zatwierdzeniu.
- Testy integracyjne używają PostgreSQL, nie zastępują jego semantyki bazą in-memory.
- README zawiera konfigurację, migracje, konta demo, komendy testów i ograniczenia.

## Po MVP - projekt integracji

- Gemini API: adapter dostawcy za wspólnym interfejsem; narzędzia wykonuje backend z tożsamością sesji, nigdy model bezpośrednio w bazie.
- Vertex AI Agent Builder / Dialogflow CX: alternatywna warstwa orkiestracji. Ocenić po demonstracji; nie dokładać obu do MVP.
- Cloud Run: osobne wdrożenia web/API; PostgreSQL docelowo Cloud SQL, sekrety w Secret Manager. Migracje jako kontrolowany etap wdrożenia. SSE z ograniczonym czasem i obsługą anulowania.
- Slack: opcjonalne alerty anomalii przez outbox i webhook; bez danych osobowych i treści rozmów. Żadne wiadomości nie są wysyłane bez osobnej konfiguracji.
- RAG: regulamin i FAQ jako źródła wiedzy z cytowaniem; dokumenty nie nadają uprawnień i nie są instrukcjami dla narzędzi.
- Voice: wejście/wyjście głosowe jako adapter czatu; modyfikacje nadal wymagają widocznego potwierdzenia.

## Historyczny stan przed rozpoczęciem implementacji

- Nie powstała implementacja ani repozytorium Git.
- Dostępne lokalnie: .NET SDK 10.0.300, Node.js 22.23.1, npm 10.9.8, klient psql.
- Docker jest zainstalowany, ale próba dostępu wymagała zgody, której nie udzielono. PostgreSQL nie został uruchomiony ani zweryfikowany.
- Folder projektu utworzył użytkownik.
- Pierwszy krok w terminalu: przejść do folderu projektu, utworzyć szkielet i wybrać dostępny sposób uruchomienia PostgreSQL.

```sh
cd /Users/maciejbednarczyk/Projekty/fuel-club
git init
dotnet new sln -n FuelClub
dotnet new webapi -n FuelClub.Api -o backend/FuelClub.Api --framework net10.0
dotnet sln add backend/FuelClub.Api/FuelClub.Api.csproj
npx create-next-app@latest frontend --ts --eslint --tailwind --app --src-dir --import-alias '@/*' --use-npm
```

Powyższy zapis jest historyczny, nie jest instrukcją uruchomienia obecnego projektu. Aktualny stan i komendy opisuje dokumentacja w docs/src/content/docs/.

## Źródła techniczne

- Next.js App Router: https://nextjs.org/docs/app
- Instalacja Next.js: https://nextjs.org/docs/app/getting-started/installation
- Gemini API: https://ai.google.dev/gemini-api/docs
- SDK Gemini: https://googleapis.github.io/js-genai/index.html
## Hosting MVP - Railway

- Jeden projekt Railway: Next.js, .NET API i PostgreSQL z trwałym dyskiem.
- Publiczny adres HTTPS frontendu pozwala testować na telefonie. Frontend przekazuje /api/* do prywatnego API; baza pozostaje prywatna.
- Lokalnie Docker Compose; na hostingu te same obrazy Docker.
- Pierwsze wdrożenie po etapie logowania. Testy na telefonie: sesja, cookies, CSRF, HTTPS, później SSE.
- SSE bez buforowania proxy, z heartbeat, limitem czasu i anulowaniem po rozłączeniu.
- Sekrety w zmiennych Railway, migracje jako kontrolowany etap wdrożenia, kopie bazy i test odtworzenia.
- Hobby: minimum 5 USD/mies., obejmuje 5 USD zużycia. Szacowany budżet demo: 10-20 USD/mies., do weryfikacji po wdrożeniu. Konfigurujemy alert kosztowy. Gemini opcjonalnie, rozliczane osobno.
- Cloud Run pozostaje opcją przyszłej migracji.

Źródła:
- https://docs.railway.com/pricing
- https://railway.com/guides/sse-vs-websockets
