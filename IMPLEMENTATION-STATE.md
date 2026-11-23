# Dziennik wznowienia

Spec: `specs/SPEC.md`. Plan: `specs/PLAN.md`. Pętla: `specs/LOOP.md`.

HEAD / branch: main (commity kroków lokalnie, bez push; ostatni push: c4a7883)
Etap i status: P0-P6 - done; P7a - done; P7b - done; P7c - otwarte (judge, wymaga klucza i próbek); P8 - done; P9 - done lokalnie (wdrożenie wymaga konta); P10 - done; poprawki review - done

## P0 - baza pracy (done, 2026-11-22)

Zakres: baseline na bazach testowych; korekty `PLAN-MVP.md` (role, reguła punktów od PaidAmount, nazwy RewardOffer/Coupon, status etapów 4-6, odnośnik do specs).
Zmienione pliki: `PLAN-MVP.md`, `IMPLEMENTATION-STATE.md`.
Testy (2026-11-22, HEAD 32b9005):
- `dotnet test apps/api.tests` (fuelclub_test) - passed 419/419
- `npm run typecheck`, `npm run lint`, `npm run format:check` - passed
- `npm run e2e` (fuelclub_e2e) - passed 46/46
- `docs: npm run build` - passed; `dotnet csharpier check apps` - passed
Review: c4a7883 - `/code-review high`, 4 uwagi: 3 przyjęte i poprawione, 1 odrzucona (dodatkowy indeks FK). P0 bez zmian kodu - review nie dotyczy.
Dokumentacja: `PLAN-MVP.md`.
Blokery: -

## P1 - katalog i wymiana (done, 2026-11-22)

Zakres: feature `rewards` (`/oferty`, skrót na ekranie głównym), potwierdzenie kosztu i warunków z wysyłką wersji z potwierdzenia, obsługa offer_changed/inactive/insufficient_points, nieznany wynik z ponowieniem tym samym kluczem; klucz nierozstrzygniętej operacji w sessionStorage (`useIdempotentMutation` `persist`), czyszczony przy logowaniu/rejestracji/wylogowaniu; 401 → „Zaloguj się”. E2E: admin testowy (`BootstrapAdmin`), fixture `staff` wyłączający oferty po teście. Narzędzie: `specs/browser-instance.sh` (instancja :5291/:3200, baza fuelclub_browser).
Zmienione pliki: `apps/web/features/rewards/*`, `app/(app)/oferty/page.tsx`, `components/home/HomeScreen.tsx`, `lib/idempotency.ts`, `features/auth/hooks.ts`, `eslint.config.mjs`, `.gitignore`, `playwright.config.ts`, `e2e/{fixtures,accounts,rewards.spec,loyalty-card.spec}.ts`, docs (rewards, loyalty, testy, ADR 0001), `specs/browser-instance.sh`.
Testy (2026-11-22): typecheck, lint, format - passed; `npx playwright test` - passed 58/58 (mobile + desktop); docs build - passed. API bez zmian (baseline P0: 419/419).
Próby negatywne: bez obsługi nieznanego wyniku → pada test utraconej odpowiedzi; bez `persist` → pada test wyjścia z ekranu.
Przeglądarka (Chrome, :3200, anna demo): katalog 3 ofert, wymiana klawiaturą (Enter na potwierdzeniu) 650 → 150 pkt + komunikat sukcesu; za mało punktów → komunikat, saldo bez zmian. Zmiana rozmiaru okna nie zmienia viewportu w tym narzędziu - szerokość telefonu pokrywa E2E (Pixel 7).
Review (`/code-review high`, 8 uwag, wszystkie przyjęte): stop zabijał przeglądarkę (tylko listenery); `.next-browser` w ESLint; zaokrąglenie maks. rabatu (liczby całkowite, 7,58); blokada nieznanego wyniku po nawigacji (sessionStorage + test); start na zajętych portach; 401 bez ponowienia; hasło w wyjściu skryptu; rosnący katalog E2E (retire). Poprawki zweryfikowane testami powyżej (samoprzegląd zmian po review).
Uwaga: Next 16 zachowuje stan ekranu przy nawigacji klienta (Activity) - po powrocie widać pierwotny komunikat; po przeładowaniu - przywrócony z sessionStorage.

## P2 - moje kupony (done, 2026-11-22)

Zakres: feature `coupons` (`/kupony`): lista z migawką warunków, datami i statusem (nieznany status pokazany wprost, nie jako „Wykorzystany”); aktywacja idempotentna, po każdej próbie `GET`, ostrzeżenie o nieznanym wyniku znika, gdy `GET` pokazuje `Active`; QR, kod i kopiowanie tylko dla aktywnego; 401 → „Zaloguj się” (lista, baner odświeżenia, aktywacja). QR i kopiowanie przeniesione do `components/code/` (wspólne z kartą), formatowanie warunków do `lib/format.ts`. Skrót na ekranie głównym, link w komunikacie po wymianie.
Odstępstwo od planu: aktywacja bez `persist`. Powód: ponowna aktywacja aktywnego kuponu (także nowym kluczem) zwraca ten sam kod i nie zmienia stanu - utrata klucza nie może niczego zdublować (test API `CouponActivationTests`: nowy klucz → ten sam kod).
Testy (2026-11-22): typecheck, lint, format - passed; `npx playwright test` - passed 68/68; docs build - passed. API bez zmian.
Próba negatywna: aktywacja bez odczytu `GET` po próbie → padają 3 testy aktywacji.
Przeglądarka (Chrome, :3200, anna demo): kupon „Do aktywacji” → aktywacja → QR, kod w grupach, „Kopiuj kod”, data aktywacji.
Review (`/code-review high`, 6 uwag): `persist` - odrzucone (powód wyżej, zapisane tu); 401 bez logowania - przyjęte; fallback statusu - przyjęte; brak testu cudzej aktywacji - przyjęte (POST → 404, kupon dalej „Do aktywacji”); wyciek kontekstu - przyjęte (`await using`); opis QR w docs - przyjęte.

## P3 - kupon w kasie i historia (done, 2026-11-22)

Zakres: `CouponSection` w formularzu kasy: pole kodu kuponu (normalizacja jak na serwerze), czytnik QR kuponu (czytniki przyjmują tylko swój format, nie działają naraz); warunki po pełnych kodach karty i kuponu (`useCouponTerms`, klucz z obu kodów, bez ponowień, 5 min w pamięci); rabat tylko na „Przelicz rabat” (`useCouponCalculation`, numer żądania, spóźnione odpowiedzi ignorowane); zmiana kwoty/litrów → wyliczenie nieaktualne bez zapytania; zmiana kodu → wyliczenie usunięte; błędy 404/409/429. Zapis wysyła kod (bez rabatu); wynik: rabat, do zapłaty, punkty; `coupon_busy` jak nieznany wynik. Po ostatecznym wyniku zapisu i po odblokowaniu podglądy zapomniane (sekcja remontowana). Historia: brutto + linia rabatu i zapłaty. `openRegister` wydzielony do `e2e/register.ts`.
Testy (2026-11-22): typecheck, lint, format - passed; `npx playwright test` - passed 80/80; docs build - passed. API bez zmian.
Próby negatywne: przeliczenie bez numeru żądania → pada test spóźnionej odpowiedzi; czytnik kuponu bez blokady przy aktywnym czytniku karty → pada test skanu; brak czyszczenia podglądu po odrzuconym zapisie → pada test kuponu odebranego gdzie indziej.
Przeglądarka (Chrome, :3200, kasjer demo, karta i kupon Anny): kod kuponu wpisany małymi literami ze spacjami → warunki; 120,50 zł / 30 l → „Przelicz” 6,00 zł / 114,50 zł / 114 pkt; zapis → wynik z rabatem, formularz wyczyszczony.
Review (`/code-review high`, 9 uwag, wszystkie przyjęte): dwa czytniki naraz (jedna kamera); nieaktualny podgląd po błędzie/odblokowaniu; gcTime 0 zużywał limit; komunikat „nieaktualne” bez wyniku i blokada przeliczenia przy zmianie karty; logika HTTP w komponencie (przeniesiona do hooka); brak testu czytnika karty z QR kuponu; słaba asercja punktów; opis skanera; literówka w docs.

## P4 - administracja (done, 2026-11-22)

Zakres: feature `admin-rewards` (`/admin/oferty`, `nowa`, `[id]`), strażnik `app/(app)/admin/layout.tsx` (bez roli Admin → `/`), skrót na ekranie głównym tylko dla Admin. Formularz RHF + zod (granice jak `RewardOfferRules`). Tworzenie: idempotentne z `persist`, niedokończone tworzenie przywracane po powrocie. Edycja: `PUT` z wersją z wczytania; `409` → panel konfliktu (aktualna wersja, formularz zachowany, „Wczytaj aktualną wersję” / „Zapisz moje zmiany na wersji N”); nieznany wynik → blokada i „Sprawdź aktualny stan” (zapisano / nie zapisano / konflikt). Wyłączanie przez pole „Aktywna”.
Poprawka przy okazji (bezpieczeństwo, istniejący kod): wylogowanie i „Zaloguj się” przy wygasłej sesji robią pełne przeładowanie - Next 16 trzymał w ukrytym DOM ekrany poprzedniego użytkownika (kody kuponów); wylogowanie czyści też przy `401`.
Testy (2026-11-22): typecheck, lint, format - passed; `npx playwright test` - passed 94/94; docs build - passed. API bez zmian.
Próby negatywne: edycja bez obsługi nieznanego wyniku → pada test utraconego PUT; tworzenie bez `persist` → pada test wyjścia z ekranu; wylogowanie przez `router.replace` → pada test kodów w DOM; brak czyszczenia przy 401 → pada test wygasłej sesji.
Przeglądarka (Chrome, :3200, admin demo): skrót „Administracja ofert”, lista 3 ofert z wersjami, edycja kosztu 2000 → 2100, „Zapisano (wersja 2)”. Zaobserwowany wyciek ukrytego DOM po wylogowaniu (kupony Anny widoczne w drzewie po zalogowaniu kasjera) → poprawione jw.
Review (`/code-review high`, 4 uwagi, wszystkie przyjęte): tworzenie bez trwałego klucza; „Zaloguj się” nawigacją klienta; wylogowanie przy 401 nic nie czyściło; odmontowany czytnik zwalniał cudzą blokadę kamery.
Uwaga: ostrzeżenia dev `NEXT_REDIRECT` (instant validation) przy przekierowaniach strażników - to samo daje istniejący strażnik sesji; tylko tryb dev.

## P5 - kontrakt czatu (done, 2026-11-22)

Zakres: ADR 0007. Moduł `Assistant`:
- `POST /api/chat` jako SSE: `meta`, `tool`, `delta`, `confirmation`, potem dokładnie jedno `done` albo `error`; heartbeat `: ping`.
- Zamknięta lista 4 narzędzi tylko do odczytu, tożsamość z sesji.
- `DemoChatModel` na regułach.
- Bezstanowy token Data Protection; `POST /api/chat/confirmations` z idempotencją, `404` dla złego tokenu, `410` po terminie.
- Aktywacja wydzielona do `Rewards/Coupons/CouponActivation`, wspólna z `/activate`.
- Limity: długość wiadomości i historii, 4 narzędzia na turę, czas tury i narzędzia, 20 tur/min na użytkownika.
- Typy `schema.d.ts` wygenerowane. UI czatu w P6.

Testy (2026-11-22): `dotnet test` passed 441/441 (24 nowe w `AssistantTests`); typecheck i lint passed; `admin.spec` passed 10/10 po zmianie klucza zapytania; docs build passed.

Próby negatywne, każda wywraca swój test:
- token bez porównania użytkownika → test wiązania;
- brak limitu narzędzi → test limitu;
- `no-store` na endpoincie zamiast na grupie → test odtworzenia. Główny host tego nie łapał, bo odnawianie ciasteczka dokleja `no-store`, stąd osobny test na hoście bez odnawiania.

Przeglądarka: bez UI. Na własnej instancji (:3200 → :5291) curl `-N` przez proxy Next: zdarzenia w kolejności, nagłówki SSE (`no-store`, `X-Accel-Buffering`, `identity`). Odpowiedź demo jest natychmiastowa, więc nie dało się sprawdzić, czy proxy buforuje wolny strumień - sprawdzi E2E w P6.

Review (`/code-review high`, 9 uwag, 8 przyjętych):
- odtworzenie potwierdzenia bez `no-store`;
- „aktywne kupony” uruchamiało propozycję aktywacji;
- nieznana oferta → propozycja innego kuponu;
- model dostawał token potwierdzenia;
- pusty wpis historii przechodził;
- wyjątek heartbeatu maskował wynik tury;
- brak `TimeProvider` w `AddAssistant`;
- klucz zapytania oferty poza `query-keys.ts`.

Odrzucona: jedno miejsce na klucz na operację w `sessionStorage` - katalog blokuje wszystkie wymiany przy nieznanym wyniku, także po powrocie na ekran, więc drugiej wymiany nie da się zacząć.

## P6 - transport i ekran czatu (done, 2026-11-22)

Zakres: `features/assistant` i ekran `/czat` ze skrótem „Asystent”.
- `fetch` ze strumieniem + `eventsource-parser`.
- Tura w stanie `streaming`/`done`/`error`/`cancelled`; sukces tylko po `done`. Koniec strumienia bez `done` albo zerwanie → błąd „niepełna”.
- „Zatrzymaj” przerywa żądanie.
- Etykiety narzędzi, karta potwierdzenia: przyciski aktywne dopiero po `done`, „Nie teraz”, ponowienie z tym samym kluczem. Kod kuponu nie trafia do czatu.
- Historia: tylko zakończone tury.
- Proxy E2E ma tryby `sse-split` (3 bajty, przerwa przed `delta`) i `sse-cut`.

Błąd znaleziony przez E2E: Next kompresował (gzip) odpowiedź z rewrite i oddawał cały strumień na końcu.
- Poprawka: `compress: false`, a API wysyła też `no-transform`.
- Samo `no-transform` nie wystarcza, bo odnowienie ciasteczka podmienia `Cache-Control` - stąd osobny test API na hoście bez odnawiania.
- Wcześniejszy curl przez proxy tego nie pokazał, bo nie wysyłał `Accept-Encoding`.

Testy (2026-11-22): `dotnet test` passed 442/442; `npx playwright test` passed 118/118 (24 w `chat.spec`); typecheck, lint i docs build passed.

Próby negatywne, każda wywraca swój test:
- kompresja Next bez `no-transform` → test podzielonego strumienia (sam `compress: false` wystarcza);
- czyste zakończenie bez `done` traktowane jako sukces → nowy test. `sse-cut` tego nie łapał, bo zerwanie idzie ścieżką wyjątku;
- karta bez stanu oczekiwania → test potwierdzenia po `done`.

Przeglądarka (Chrome, :3200, Anna): saldo z etykietą narzędzia, oznaczenie demo, „aktywuj” → „Nie masz kuponów do aktywacji”. Okna nie dało się zwęzić; widok telefonu sprawdza projekt E2E `mobile`.

Review (`/code-review high`, 10 uwag, wszystkie przyjęte):
- karta mówiła „zapytaj ponownie” w trakcie odpowiedzi;
- pytanie wysłane między `done` a zamknięciem strumienia ginęło;
- zła ramka nie zamykała połączenia;
- ręczne typy DTO;
- każdy `400` jako błąd długości;
- `403` jako „niedostępny”;
- mylący komentarz;
- pozostałości `.next-probe` w tsconfig;
- brak testu `no-transform` w docs;
- Enter przy IME i podpowiedź czyszcząca wpisany tekst.

## P7a - ślady i evale (done, 2026-11-22)

Zakres: ADR 0008.
- Tabela `assistant_turns` (migracja `AssistantTurns`): tylko metadane tury - pseudonim HMAC, model, czasy, wynik, kod błędu, narzędzia, tokeny lub `null`.
- Spany OpenTelemetry `chat.turn`/`chat.tool` z listą dozwolonych atrybutów. Eksport OTLP tylko przy `Telemetry:OtlpEndpoint`, tylko źródło asystenta.
- Phoenix w `compose.yml`: profil `observability`, digest, 127.0.0.1, bez auth, port przez `PHOENIX_PORT`.
- Evale `assistant-evals-v1`: 13 przypadków pokrywających wszystkie kategorie SPEC §3. Sprawdzają narzędzia, koniec tury, brak skutków w bazie i zgodność śladu.

Błąd znaleziony testem eksportu: span tury pod nieeksportowaną aktywnością HTTP był odrzucany przez sampler `ParentBased`. Poprawka: korzeń z linkiem do żądania.

Testy (2026-11-22): `dotnet test` passed 467/467; `chat.spec` passed 24/24; docs build passed.

Próby negatywne, każda wywraca swój test:
- tekst wiadomości w atrybucie → testy spanów i eksportu;
- bez korzenia → brak eksportu;
- zapis narzędzia tylko po sukcesie → test rejestratora.

Przeglądarka (Chrome): własny Phoenix na :6016 (port 6006 zajmował osobny kontener Phoenix użytkownika, nieruszany). 3 tury `chat.turn` z potomnymi `chat.tool`, atrybuty `fuelclub.*` bez treści. Kontener usunięty po sprawdzeniu.

Review (`/code-review high`, 7 uwag, wszystkie przyjęte):
- narzędzie z timeoutem znikało ze śladu;
- odejście klienta zapisywane jako `provider_error`;
- zapis śladu bez limitu czasu trzymał strumień;
- zapis we wspólnym `DbContext` z narzędziami;
- częściowa suma tokenów;
- brak klucza pseudonimu poza `Development` tylko ostrzegał;
- logi bez powiązania z żądaniem (dodany link).

## P7b - zdarzenia, anomalie, kolejka, retencja (done, 2026-11-22)

ADR 0009, moduł `Observability`, ekran `/admin/obserwowalnosc`.
- `security_events`: 403, 401 logowania, 409 klucza i ponownego odbioru, 429. Wzorzec trasy i pseudonim, bez IP i id z URL. Zapis po odpowiedzi, najwyżej 600/min.
- Kod błędu przechwytuje `ProblemCodeCapture` (dekorator `IProblemDetailsService`) - odpowiedzi bez zmian (pełny zestaw API przeszedł przed testami P7b).
- 8 reguł anomalii z konfiguracji; alert w logu z wyciszeniem, Slack nie skonfigurowany.
- Admin: metryki (p95 `percentile_cont`, narzędzia, tokeny), kolejka podejrzanych tur, oznaczanie przejrzanych.
- Retencja 30 dni (worker).
- Limity: logowanie/rejestracja na adres klienta (`X-Forwarded-For` od loopback/`KnownProxies`), zakupy na kasjera.

Testy (2026-11-22): `dotnet test` passed 476/476; E2E 122/122 w dwóch przebiegach, w pierwszym przebiegu po poprawkach 1 porażka, której nie udało się powtórzyć (niezidentyfikowana, prawdopodobnie flaky); docs build passed.

Próby negatywne, każda wywraca swój test: bez `ProblemCodeCapture`; ocena udziału bez minimalnej próbki; bez `UseForwardedHeaders`; bez wykluczenia przerwanych tur z kolejki.

Przeglądarka (Chrome, własna instancja): ekran admina - metryki, reguły, kolejka.

Review (`/code-review high`, 8 uwag, wszystkie przyjęte):
- limit logowań wspólny dla całej aplikacji (IP Nexta) → forwarded headers;
- nieograniczone INSERT-y przy zalewie odmów → limit na minutę;
- `429` przy ponowieniu zakupu kasował klucz → klucz zostaje;
- przerwane tury w kolejce jako awaria narzędzia → wykluczone;
- brak komunikatu `429` przy logowaniu/rejestracji;
- logowanie rozpoznawane po surowej ścieżce → po trasie;
- `ProblemCodeCapture` blokowałby przyszłe `AddProblemDetails` → dekorator writerów;
- sumy tokenów `int` → `long`.

Wpadka procesu: `git checkout -- apps/api/Migrations` cofnął snapshot; migracja odtworzona (nowe id `20261122164023`), wpis historii poprawiony w bazach test/e2e/browser.

## P7c - judge (niewykonany, 2026-11-22)

Brak klucza dostawcy modelu w środowisku. Zgodnie z planem nie blokuje MVP. Jawnie opisany w `referencja/odbior-mvp.md`: brak oceny próbek, budżetu, próbki błędów i kalibracji, a kolejka nie ma powodu „niska ocena”.

## P8 - CI i odbiór lokalnego MVP (done, 2026-11-22)

- `README.md` (świeży start, konta demo, scenariusz, kontrole, stan), `.env.example` (wyjątek w `.gitignore`), `.github/workflows/ci.yml` (API + evale, CSharpier, typecheck, lint, format, build Next, zgodność `schema.d.ts` z OpenAPI, E2E, docs).
- `e2e/demo.spec.ts`: pełny scenariusz przez UI - klient i kasjer w osobnych kontekstach, aktywacja przez czat, jeden odbiór, odmowa drugiego, historia i saldo.
- `referencja/odbior-mvp.md`: kryteria SPEC → dowód albo jawny brak.
- Świeży klon wg README (osobna baza `fuelclub_fresh`, zmienne środowiskowe zamiast user-secrets, inne porty): znaleziony realny błąd - `dotnet ef` bez `dotnet restore` (NETSDK1004). Poprawione w README i instrukcji. Potem migracja, `/health/db`, przekierowanie na `/login` i logowanie kasjera demo przez Next zadziałały. Baza usunięta.

Testy (2026-11-22): `dotnet test` passed 476/476; E2E 124/124 w trzech kolejnych pełnych przebiegach; typecheck, lint, format:check, `next build`, CSharpier check, docs build passed. CI nie było uruchomione na GitHubie (brak push).

Review (`/code-review high`, 5 uwag, wszystkie przyjęte):
- **Next nie ustawia `X-Forwarded-For` przy rewrite** i przepuszcza nagłówek klienta. Moje założenie z P7b było błędne, potwierdziła to sonda z serwerem zwracającym nagłówki. `proxy.ts` usuwa go teraz dla `/api/*`, chyba że `TRUST_FORWARDED_FOR=true` (sonda: bez zaufania znika, z zaufaniem przechodzi). Lokalnie limit logowań jest wspólny; ADR 0009 poprawiony.
- CI bez `dotnet restore` przed `dotnet ef`.
- `meta` i pierwszy flush SSE poza obsługą rozłączenia.
- `429` zakupu pokazywany jako błąd serwera.
- `RewardsCatalog` czytał `sessionStorage` w inicjalizatorze stanu (ryzyko hydratacji) → `useSyncExternalStore`.

Poprawki sprawdzone testami i sondą, bez drugiego przeglądu.

Stabilność E2E:
- limit podglądu kuponów (30/min na jedynego kasjera E2E) wyczerpywał się w pełnym przebiegu → `Rewards__PreviewPermitLimit=1000` w Playwright (test 429 podglądu jest mockowany);
- test „stop cancels the turn” polegał na `requestfailed` dla wstrzymanej trasy, co nie jest pewne → sprawdza przerwanie sygnału `fetch` w stronie.

Przeglądarka (Chrome): logowanie Anny, oferty, czat - saldo strumieniowane przez `proxy.ts` na `/api`.

## P9 - hosting w dostępnym zakresie (częściowo, 2026-11-22)

ADR 0010, instrukcja „Obrazy i hosting”.
- Obrazy: `apps/api/Dockerfile` (publish, bundle migracji `efbundle`, `tini`, `libgssapi-krb5-2`, bez roota) i `apps/web/Dockerfile` (standalone, `API_INTERNAL_URL` w buildzie i runtime, `.env*` wykluczone). Job CI buduje oba.
- Klucze Data Protection w bazie, szyfrowane certyfikatem ze zmiennej środowiskowej. Wymagany poza Development, sprawdzany przy starcie hosta, więc bundle migracji go nie potrzebuje.
- `X-Forwarded-For`/`Proto` od loopback, `KnownProxies` i `KnownNetworks`, parsowane przy starcie. `proxy.ts` usuwa oba nagłówki bez `TRUST_FORWARDED_FOR`.
- Logi SQL EF: `Warning` poza Development.

Lokalna próba produkcyjna (prywatna sieć Docker, `Production`):
- migracja bundlem;
- rejestracja i logowanie, ciasteczka `Secure`+`HttpOnly`;
- czat SSE przez Next standalone;
- `/` i `/karta` z danymi;
- klucze zaszyfrowane, sesja po restarcie API;
- bez certyfikatu kontener kończy się kodem 134;
- `pg_dump` → `pg_restore`: te same liczby wierszy, a API na odtworzonej bazie przyjęło starą sesję.

Kontenery, obrazy i pliki próby usunięte.

Błędy znalezione próbą:
- bundle uruchomiony bez `--entrypoint` startował API (moja pomyłka);
- wymuszone `Secure` dawało `500` na `/api/auth/csrf` → `X-Forwarded-Proto`;
- kontener bez `tini` zostawał „Up” po nieudanym starcie;
- SQL EF w logach Production.

Testy (2026-11-22): `dotnet test` passed 480/480 (`DataProtectionTests`: współdzielenie kluczy przez bazę, szyfrowanie certyfikatem, `Secure` za HTTPS, błędy konfiguracji); E2E 124/124; CSharpier, Prettier, typecheck, lint, docs build passed.

Próby negatywne: bez `PersistKeysToDbContext` i bez szyfrowania certyfikatem - odpowiednie testy padają.

Review (`/code-review high`, 7 uwag, wszystkie przyjęte):
- brak `API_INTERNAL_URL` w runtime obrazu web - każda strona „Serwis chwilowo niedostępny”; poprzednia próba wołała tylko `/api`, teraz otwiera strony;
- `.env*` w obrazie;
- leniwe parsowanie `KnownNetworks` i komentarz w linii w `.env.example`;
- `X-Forwarded-Proto` od klienta;
- etykiety `Planowane`;
- SQL EF wyciszony też w Development;
- klucze niezaszyfrowane w bazie wbrew regule sekretów → certyfikat.

Niewykonane (wymaga konta): wdrożenie Railway, HTTPS na domenie, SSE przez brzeg hostingu, kamera na telefonie. Punkty „sprawdź” w instrukcji.

## P10 - adapter Gemini (2026-11-22, na prośbę właściciela)

ADR 0011.
- `Assistant:Provider=gemini` + `Assistant:Gemini:ApiKey` (user-secrets).
- `GeminiChatModel` (REST `generateContent`, function calling): klucz w nagłówku, odsyłanie `thoughtSignature`, tokeny z rozumowaniem.
- Do Google nie idą id, e-mail, kody ani token potwierdzenia.

Testy: `dotnet test` passed 484/484, 1 pominięty (na żywo, wymaga `FUELCLUB_GEMINI_API_KEY`).
- Podstawione API Gemini: pełna tura z narzędziem, podpis i wynik w drugim żądaniu, brak tożsamości, tokeny w śladzie, odrzucone narzędzie spoza listy, `500` → `provider_error` bez zmiany salda, parsowanie, walidacja klucza.
- Próba negatywna: bez odsyłania podpisu pada test tury.

Prawdziwe API: właściciel sprawdził ręcznie w aplikacji (pytanie o saldo, narzędzie użyte). Test na żywo nie był uruchamiany.

Dodatkowo: Scalar pod `/scalar` (Development).

## Poprawki po review właściciela (2026-11-22, f2f5cba)

1. Klucz nierozstrzygniętej operacji przypisany do konta: przeżywa wylogowanie i wygaśnięcie sesji, a inne konto go nie widzi.
2. Ponowienie po przywróconym, znowu nieudanym tworzeniu oferty wysyła zachowaną ofertę.
3. Gemini: wszystkie `functionCall` z odpowiedzi; odsyłane razem z podziałem na kroki; krok ponad limit nic nie wykonuje.
4. Demo: odpowiedź na „Który kupon aktywować?” kontynuuje aktywację.
5. Nieudane wywołanie modelu → tokeny tury nieznane (`null`), nie częściowe.

Przy okazji: testy API i E2E wymuszają adapter demo (klucz w user-secrets wcześniej wpływał na evale i E2E).

Testy:
- `dotnet test` passed 488/488 (1 pominięty na żywo);
- `npm run e2e` passed 128/128;
- typecheck, lint, CSharpier, Prettier, build docs - passed.

Próby negatywne: regresje 1, 2, 4 i 5 padają bez poprawek.

## P12 - przydatniejszy czat (2026-11-22)

Kolejność uzgodniona: narzędzia czatu → nagrody → P11.
- Nowe narzędzia:
  - `compare_coupons(liters, grossAmount?)`: własne kupony osobno od ofert, wzór kasy, szacunek bez kwoty, kryterium kolejności w wyniku;
  - `explain_purchase_points`: ostatni własny zakup, remisy rozstrzygnięte.
- `pointsMissing` w ofertach.
- Gemini: 6 deklaracji, prompt `gemini-v2`.
- Zgoda właściciela na eksport: brutto, rabat, zapłacono, punkty, zasada (ADR 0011).
- Demo: reguły porównania i wyjaśnienia. 2 nowe przypadki evali.

Testy:
- `dotnet test` passed 496/496 (1 pominięty na żywo);
- `npm run e2e` passed 130/130.

Niestabilny test „stop cancels the turn” (5/16 także na starym kodzie): stop przed wyjściem żądania - sygnał już przerwany, bez zdarzenia. Test czeka na żądanie i sprawdza `signal.aborted` - 30/30.

Review (`/code-review high`, 8 uwag: 7 przyjętych, 1 odrzucona):
- przyjęte:
  - cena za litr brana za kwotę zakupu;
  - wyjaśnienie punktów przed odmową;
  - odrzucone porównanie bez komunikatu i „1 000 zł”;
  - zasada jako obecna;
  - `demo-rules-v2`;
  - `null` w opcjonalnym argumencie;
  - dokumentacja;
- odrzucona: „ostatni zakup” wg czasu rejestracji zamiast daty sprzedaży. Właściciel ustalił datę sprzedaży; przypadek paragonu z datą wsteczną zgłoszony do decyzji.

## P13 - nagrody produktowe (2026-11-22)

ADR 0013 zrealizowany:
- `Kind` w ofercie i kuponie (`FuelDiscount`/`Product`), nullowalne stawka i limit, `CHECK` rodzaju, FK `(OfferId, Kind)`;
- `RewardHandovers` + `Coupons.RedeemedHandoverId` (FK złożony, bez cyklu); `CK_Coupons_State` rozróżnia odbiór z zakupem i wydanie;
- `POST /api/cashier/reward-handovers`: właściciel przed rodzajem, ten sam `404`; `409 coupon_wrong_kind` w obu kierunkach;
- admin: `kind` przy tworzeniu, `kind_immutable`, `not_for_product`;
- web: sekcje „Rabaty na paliwo”/„Nagrody”, „Moje kupony i nagrody”, „Aktywuj odbiór”, „Wydaj nagrodę” w kasie, rodzaj w formularzu admina;
- demo: Kawa 150, Hot dog 250, Myjnia 600.

Przy okazji: eval „offers” sam tworzy ofertę (zależał od danych innych testów).

Testy: `dotnet test` 501/501 (1 pominięty na żywo), po ponownym review 502/502; E2E 134/134 przed poprawkami (nowe `product-rewards.spec`, nagroda w `admin.spec`).

Review (`/code-review high`, 9 uwag, wszystkie przyjęte):
- tekst aktywnej nagrody; opisy narzędzi i prompt Gemini (`gemini-v3`);
- zły `kind` w `PUT` → `invalid_format`; kolejność podglądu jak zakup (regresja, sprawdzona mutacją);
- błąd `kind` w formularzu admina; `429`/`customer_busy` przy wydaniu jako odmowa;
- `Down` migracji zatrzymuje się przy nagrodach; opis podglądu w OpenAPI; ADR zgodny z kodem.

E2E po poprawkach: 68/68 w plikach objętych zmianami (nagrody, admin, kupony, kupon w kasie, czat). Pełne 134/134 to wynik sprzed poprawek.

Cofnięcie migracji na tymczasowej bazie (`fuelclub_rollback_*` na 5434, usunięta po teście):
1. `Up` → `Down` bez nagród → `Up`: działa;
2. z ofertą „Kawa” i jej kuponem `Down` kończy się `P0001`, historia nadal na `ProductRewards`, oferta i kupon bez zmian.

Ponowne review (`/code-review high`, 6 uwag, wszystkie przyjęte):
- ważne: `429` na ponowieniu po zgubionej odpowiedzi pokazywał „nie została wydana” (limit działa przed idempotencją) - teraz zostaje nieznany wynik; stan wydania przechodził na inny kod z podglądem w pamięci - komponent kluczowany parą kodów;
- drobne: „Nagroda wydana w kasie”; komunikat wydania tylko dla tej karty; nieznany rodzaj przy tworzeniu bez błędów stawki i limitu;
- wynik: E2E 64/64 w plikach nagród, admina, kuponów i kasy (po trzecim i czwartym review 66/66; po piątym pełne 140/140);
- testy: E2E zgubiona odpowiedź → `429` → ponowienie (jeden klucz, jedno wydanie), zmiana kodu; `ProductRewardsMigrationTests` (baza tymczasowa) zamiast ręcznego sprawdzenia. Mutacje: bez stanu niepewności, bez klucza komponentu, bez strażnika `Down`, z `DEFAULT 0` - każda wywraca test.

Trzecie review (`/code-review high`, 5 uwag, wszystkie przyjęte):
- ważne: `401` na ponowieniu po nieznanym wyniku kasował ostrzeżenie - teraz zostaje;
- przemontowanie po poprawieniu kodu gubiło klucz (było już przed kluczowaniem komponentu) - bez `persist`, żeby kody nie trafiały do `sessionStorage`. Niepewność pary trzymana w pamięci cache zapytań; `409 coupon_redeemed` po niej pokazany jako „najpewniej wydana przy poprzedniej próbie - sprawdź”;
- drobne: ostateczna odpowiedź kasuje ostrzeżenie; komunikat wydania kasowany przy zmianie karty, nie tylko ukrywany; przy nieznanym rodzaju usuwane tylko „wymagane” błędy stawki i limitu;
- testy: E2E `401` + przemontowanie + komunikat, API literówki rodzaju z błędem precyzji. Mutacje (bez cache, `401` kasuje, filtrowanie komunikatu) wywracają test.

Czwarte review (`/code-review high`, 5 uwag, wszystkie przyjęte). Pamięć niepewności po stronie klienta ginęła po ponownym logowaniu, po 5 min i po obsłużeniu innego klienta, a do tego trzymała kody w cache. Zamiast łatać dalej, właściciel zatwierdził zmianę kontraktu:
- `409 coupon_redeemed` nagrody (wydanie, podgląd) niesie `handedOverAt` i `stationCode`;
- klient nic nie pamięta: nieznany wynik tylko w panelu, potem rozstrzyga serwer (czas + stanowisko);
- formularz kasy zablokowany w trakcie wydania (spóźniony sukces czyścił kod następnego klienta);
- usunięte: klucz cache, `redeemedAfterUnknown`. Mutacje (bez blokady, bez czasu i stanowiska) wywracają E2E.

Piąte review (`/code-review high`, 6 uwag, wszystkie przyjęte):
- ważne: na stanowisku bywa kilka kas, więc sam `stationCode` nie odróżniał własnej próby. Dodany `registerCode` (ta sama klasa danych co zatwierdzone stanowisko), kasa porównuje ze swoim stanowiskiem i kasą („na tej kasie” albo „stanowisko X, kasa Y”);
- nieznany wynik blokuje kartę, kod, skanery i zapis do rozstrzygnięcia (jak zakup); „Ponów wydanie” poza blokadą `fieldset`;
- podgląd pobierany od nowa po ostatecznej odpowiedzi; zła data → ogólny komunikat;
- 409 opisany typem `RewardsConflictProblem` w OpenAPI (bez ręcznych pól w `ProblemDetails`); dokumentacja testów;
- mutacje (bez blokady, bez porównania kasy) wywracają E2E.

Szóste review (`/code-review high`, 5 uwag, wszystkie przyjęte):
- ważne: ponowny podgląd po ostatecznej odpowiedzi (moja poprawka z piątej rundy) chował odpowiedź wydania i zużywał limit podglądów - wycofany;
- regresja: E2E ostatecznego `409` z innej kasy (mutacja z ponownym podglądem wywraca test); pełne E2E 140/140 przed dopisaniem tego testu, który przechodzi osobno;
- drobne: blokada nie ustawiana po odmontowaniu panelu; pola opcjonalne w `RewardsConflictProblem`; komentarz i dokumentacja blokady zgodne z kodem; nazwa i komentarze testu E2E.

Siódme review (`/code-review high`, 5 uwag, wszystkie przyjęte). Stan „nierozstrzygnięte” przekazywany z panelu do formularza (efekt, ref, callback) rozjeżdżał się przy trasie ukrytej przez `Activity` (Next `cacheComponents`) i przy późnej odpowiedzi. Zamiast łatać, wyliczany z cache mutacji TanStack Query (`useHandoverUnresolved`), wspólnie dla panelu i blokady:
- E2E: wyjście z kasy i powrót - blokada zostaje, ponowienie rozstrzyga;
- test ostatecznego `409` liczy żądania podglądu zamiast czekać 500 ms;
- dokumentacja: ponowne wpisanie kodu po ostatecznej odpowiedzi pokazuje warunki z pamięci, `409` dopiero po kliknięciu;
- mutacje (bez blokady w formularzu, `429` jako rozstrzygnięcie) wywracają E2E; pełne E2E 144/144, API 502/502.

Ósme review (`/code-review high`, 5 uwag): pamięć nieznanego wyniku w cache mutacji znikała po 5 min (GC), kasa mogła zablokować się bez przycisku ponowienia, a po przemontowaniu ponowienie szło nowym kluczem. Właściciel wybrał uproszczenie:
- nieznany wynik tylko w panelu pary (stan komponentu, przetrwa `Activity`), ponowienie tym samym kluczem, dopóki panel jest zamontowany (próba zakupu albo odświeżony podgląd zaczynają od nowa);
- bez blokady formularza przy nieznanym wyniku (zostaje blokada w trakcie żądania); rozstrzyga serwer: `409` z czasem, stanowiskiem i kasą;
- usunięte `useHandoverUnresolved` i podział blokady; test podglądu sprawdza okno 1 s; dokumentacja i tabela testów zgodne z kodem.
- wynik: API 502/502; pełne E2E 143/144 - jeden test (`cashier-coupon` skan QR) padł na `ECONNRESET` w sprzątaniu fixture (`PUT` oferty), po powtórzeniu plik 12/12.

Dziewiąte review (`/code-review high`): bez błędów blokujących, 5 drobnych uwag przyjętych - dokumentacja (ponowienie tym samym kluczem dopóki panel zamontowany; `409` przy świeżym podglądzie, nie z pamięci), komentarze komponentu, kolejność kroku z komunikatem w E2E, test podglądu z jawnym oknem 2 s.

Wyszło przy teście cofnięcia: `Down` ustawiał stawce i limitowi `DEFAULT 0`, a ponowne `Up` go nie usuwał. Po `Down`/`Up` kupon bez stawki dostawał `0` zamiast `NULL`. Usunięte; po powtórzeniu brak wartości domyślnych.

## P11 - przekazanie do konsultanta (2026-11-23), w realizacji

Doprecyzowanie P13 przed startem (właściciel): `409` odebranej nagrody nie dowodzi, że wcześniejsza próba się nie udała. Kasa pokazuje stan („Nagroda jest odebrana: zapisano wydanie … na tej kasie / na stanowisku X, kasa Y”), bez czerwonego błędu i bez przycisku „Wydaj nagrodę”; E2E `product-rewards`, `cashier-coupon`, `demo` 26/26.

Krok 1 - model, przejęcie, wyścig z aktywacją (kolejność od właściciela):
- rola `Support`; tabele `SupportCases` (częściowy `UNIQUE` jednego otwartego zgłoszenia, `CHECK` stanu, opiekuna, `ClosedAt`) i `SupportMessages` (`UNIQUE (CaseId, Seq)`, historia klienta tylko jako `Customer` - `CHECK`, wiadomości systemowe jako kody zdarzeń); `Down` odmawia przy istniejących zgłoszeniach;
- `Seq` przez `UPDATE ... "LastSeq" + n RETURNING` - blokada wiersza zgłoszenia porządkuje piszących;
- `POST /api/support/cases` pod blokadą klienta; `POST /api/support/agent/cases/{id}/claim` warunkowym `UPDATE`;
- czat `409 assistant.handoff_active` przed strumieniem; potwierdzenie sprawdza zgłoszenie pod blokadą klienta w transakcji idempotencji;
- zmiany względem ADR 0012 (opisane w ADR): blokada to `CustomerLock` (advisory), nie `loyalty_accounts FOR UPDATE` - klient bez konta nie ma wiersza; ścieżki konsultanta pod `/api/support/agent/...`; znacznik `ClientProvidedHistory` (ADR miał dwie nazwy).
- review (`/code-review high`, 4 uwagi, przyjęte): bez zamknięcia klient blokował sobie czat na stałe → `POST /api/support/cases/{id}/close` już w tym kroku (worker wygaszania dalej planowany); `Location` wskazywał nieistniejący zasób - usunięty; `AppendAsync` zwraca `false` dla braku zgłoszenia zamiast wyjątku; odmowa `handoff_active` zapisana pod kluczem - ponowienie nowym kluczem, opisane;
- testy: `SupportTests` 10/10 (przejęcie z bramką i równoległe, wyścig w obu kolejnościach, czat, role, ograniczenia bazy); mutacje (bez blokady w potwierdzeniu, przejęcie bez warunku `Waiting`) wywracają po 2 testy; API 512/512 (1 pominięty, Gemini na żywo).

Właściciel przyjął trzy odstępstwa od ADR. Poprawka P13: „Wydanie zostało już zarejestrowane … Przed przekazaniem produktu sprawdź, czy klient go otrzymał” (ta sama kasa nie dowodzi, że produkt nie został wydany); E2E `product-rewards` 12/12.

Krok 2 - API reszty P11 (kolejność od właściciela: endpointy → wygaszanie i retencja → BootstrapSupport):
- tura trwająca w chwili przekazania: przed odpowiedzią, zdarzeniem narzędzia i propozycją sprawdza zgłoszenie, kończy się `error handoff_active` - testy z modelem wstrzymanym w trakcie tury (odpowiedź i propozycja nie wychodzą, kupon nieaktywny);
- endpointy: `current`, wiadomości z kursorem i wysyłanie (klient i konsultant), kolejka, oddanie, zamknięcie przez konsultanta; zapis konsultanta sprawdza opiekuna otwartego zgłoszenia w tym samym `UPDATE`, który nadaje `Seq` - test z bramką: wiadomość w wyścigu z oddaniem dostaje `403`;
- worker: wygaśnięcie (7 dni), powrót do kolejki (24 h), retencja (30 dni od zamknięcia);
- `BootstrapSupport` (nowe konto; istniejące bez roli tylko ostrzeżenie z id) i konto demo `konsultant@demo.fuelclub.test`;
- review (`/code-review high`, 8 uwag, przyjęte): bezczynność liczona od aktywności konsultanta (`AgentActivityAt`, nowa migracja) - wiadomości klienta nie trzymają zgłoszenia przy nieobecnym konsultancie; tura zatrzymana przekazaniem w śladzie jako `Cancelled`, nie błąd; odczyt usuniętego zgłoszenia `404` zamiast `500`; e-mail usunięty z komunikatów błędów zakładania kont; kolejka do 100; powtórzone oddanie `403` opisane jako wykonane; tabela testów i opis UI w docs poprawione;
- mutacje: bez ponownego sprawdzenia w turze - 2 testy; bezczynność od dowolnej aktywności - 1 test; zapis konsultanta bez warunku opiekuna - 3 testy; API 526/526 (1 pominięty).

Krok 3 - `propose_handoff`: narzędzie bez argumentów, niczego nie zmienia, zdarzenie SSE `handoff` (UI pokaże przycisk); deklaracja Gemini (`gemini-v4`), reguła w modelu demo (`demo-rules-v3`); testy: propozycja bez zgłoszenia, argumenty → odrzucone; test Gemini porównuje deklaracje z listą narzędzi. Eval dla przekazania nie dodany (zbiór `v1` wersjonowany).

Transport rozmowy: właściciel zdecydował SignalR (status „pisze”, wiadomości od razu) - [ADR 0014](docs/src/content/docs/decyzje/0014-signalr-rozmowa-z-konsultantem.md), przed UI.

Krok 4 - hub SignalR (ADR 0014): `/api/support/hub`, kontrola originu, odbiorcy z bazy w chwili wysyłki, `Typing` sprawdzany przy każdym wywołaniu (1/s), powiadomienia po zatwierdzeniu (endpointy `Response.OnCompleted`, worker po commicie), schemat `SupportHubContract` w OpenAPI; testy `SupportHubTests` 4 (klient SignalR w testach); mutacje: `Typing` bez sprawdzenia opiekuna - 2 testy, odbiorcy z autorów wiadomości - 1 test (po wzmocnieniu: były opiekun pisze przed oddaniem); API 533/533 (1 pominięty).
- review (`/code-review high`, 8 uwag, przyjęte): origin wymagany tylko przy negocjacji i WebSocket (przeglądarka nie wysyła go przy GET transportów zapasowych - inaczej fallback SSE/long polling dostawał `403`); stan w `caseChanged` czytany przy wysyłce (spóźnione zdarzenie nie niesie starego stanu); brak powiadomień przy odpowiedzi z błędem; „pisze” ograniczane per zgłoszenie i tylko po przekazaniu; walidacja `HubAllowedOrigins`; jedno `queueChanged` na przebieg workera; `CloseOnAuthenticationExpiration` (wylogowanie w innej karcie - opisane ograniczenie); opis UI w docs bez trybu, którego jeszcze nie ma; mutacje: origin na każdym żądaniu, ograniczenie per połączenie - po 1 teście.

## Następny krok

Otwarte:
- P7c judge na próbkach;
- publiczny hosting i telefon (konto Railway);
- test Gemini na żywo (`FUELCLUB_GEMINI_API_KEY`);
- CI na GitHubie (push - decyzja właściciela).

P13 zamknięty (9 rund review, ostatnia bez uwag blokujących). P11 w realizacji: API, `propose_handoff` i hub gotowe; dalej UI klienta i konsultanta, E2E.
