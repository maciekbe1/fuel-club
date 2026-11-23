# Dziennik wznowienia

Spec: `specs/SPEC.md`. Plan: `specs/PLAN.md`. Pętla: `specs/LOOP.md`.

HEAD / branch: main (commity kroków lokalnie, bez push; ostatni push: c4a7883)
Etap i status: P0-P6 - done; P7a - done; P7b - done; P7c - judge offline zrobiony w P16d (kalibracja czeka na oceny człowieka), online w P15e; P8 - done; P9 - done lokalnie (wdrożenie wymaga konta); P10 - done; poprawki review - done

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

## P11 - przekazanie do konsultanta (done, 2026-11-23)

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

Krok 5 - UI i E2E: `/czat` (przycisk, zgoda, historia opcjonalna, rozmowa na żywo, blokada czatu i propozycji), `/konsultant` (kolejka, przejęcie, rozmowa, oddanie, zamknięcie); `@microsoft/signalr`, `use-stick-to-bottom`, shadcn `textarea`/`checkbox`; jeden komponent na plik (uwaga właściciela), także `ChatScreen` → `ChatTurn`, `ToolChip`; proxy E2E tuneluje WebSocket i umie go zerwać; naprawiony błąd kroku 3 (web pomijał zdarzenie `handoff` - lista zdarzeń typowana kontraktem); E2E 148/148.
- review (`/code-review high`, 7 uwag, przyjęte): zapamiętany `403` wyrzucał konsultanta po ponownym przejęciu (rozmowa usuwana z pamięci przy wyjściu); przewijanie do dołu nie działało ponad 60vh (limit na elemencie przewijanym przez bibliotekę); hub nie wracał po wyczerpaniu ponowień; drugie odrzucenie czatu nie pokazywało zgłoszenia; historia brana przy kliknięciu zamiast przy wysłaniu; ręczne typy żądania → z `schema.d.ts`; test zerwania sprawdza „pisze” (tylko hub), nie tylko dotarcie wiadomości.
- audyt reszty kodu (podział plików, biblioteki zamiast własnego kodu) - raport przekazany właścicielowi, zmiany czekają na decyzję.

Refaktor 1 - podział plików (audyt): 8 plików z kilkoma komponentami rozbite skryptem (przeniesienie deklaracji + `organizeImports` z TypeScript), wspólne stałe do `labels.ts`, `couponLabels.ts`, `historyFormat.ts`, `editRewardShared.ts`; `FormAlert` osobno; reguła w ADR 0001; E2E 148/148.

Refaktor 2 - wspólne elementy: `components/query/QueryError.tsx` (shadcn `alert`) zamiast 7 kopii bloku błędu wczytywania (przy okazji: 401 wszędzie daje „Zaloguj się ponownie”); shadcn `skeleton` w 10 miejscach (karta salda zostaje - ciemne tło marki); ramki komunikatów jako `Alert` (wariant projektu `warning` dla niepewnego wyniku, `destructive`); E2E 148/148.

Refaktor 3 - czat na AI Elements ([ADR 0015](docs)): `conversation`, `message` (odpowiedź jako markdown przez `streamdown`), `suggestion` w `/czat` i w rozmowie z konsultantem zamiast ręcznych dymków, przewijania i podpowiedzi; `prompt-input` odrzucony (1414 linii za ~70); okno czatu o stałej wysokości (telefon 45dvh); zrzuty telefon/desktop sprawdzone; E2E 148/148.

Refaktor 4 - martwy kod i shadcn: usunięty nieużywany `lib/sse.ts`; formatery (kwota, liczba, litry, godzina, data z godziną) wspólne w `lib/format.ts` zamiast kopii w 9 plikach (zostają tylko specyficzne: procenty i sekundy w obserwowalności, znak punktów i data sprzedaży UTC w historii); shadcn `badge` (statusy kuponów i ofert, narzędzia czatu, powody przeglądu), `toggle-group` (okno czasu), `radio-group` + `checkbox` + `textarea` w formularzu oferty (przez `Controller`; E2E przez `getByRole`, bo Base UI dodaje ukryte pole z tą samą etykietą); E2E 148/148.
- review refaktoru (`/code-review high`, 8 uwag, przyjęte): checkbox i radio Base UI nie respektowały zablokowanego `fieldset` (formularz oferty po nieznanym wyniku zapisu) - teraz `disabled` wprost; `streamdown` przepuszczał obrazki z dowolnego adresu - odpowiedź bez `img` i `a` (`AssistantAnswer.tsx`); puste okno czatu nad otwartym zgłoszeniem; `remark-breaks` dla pojedynczych nowych linii; zawijanie długich słów w dymkach i znaczkach; angielskie przyciski `streamdown` wyłączone; ADR 0001 opisywał usunięty `lib/sse.ts`. Nowe E2E: markdown bez obrazków i linków (nic nie wychodzi do obcego adresu), zablokowany checkbox, brak pustego okna; mutacje: bez `disallowedElements` i bez `disabled` - testy padają; E2E 150/150.
- zostaje z audytu (osobno, za zgodą): skaner QR na `barcode-detector`, API: walidacja ustawień przez DataAnnotations, `Http.Resilience` dla Gemini, wbudowany health check.

Audyt - API i skaner (2026-11-23):
- Gemini: ponowienia przejściowych błędów (`429`, `5xx`, `408`, połączenie) przez `Microsoft.Extensions.Http.Resilience` - 2 ponowienia, odstęp od 0,5 s z rozrzutem, `Retry-After`, `4xx` bez ponowień; `RequestTimeout` obejmuje wszystkie próby. Testy: `503`, `429`, sukces → 3 identyczne żądania; `400` → jedno; mutacja (1 ponowienie) - test pada.
- `/health/db`: wbudowany health check EF Core (`AddDbContextCheck`) zamiast własnego endpointu; odpowiedź `Healthy`/`Unhealthy` (tekst), nadal tylko Development; zniknął z OpenAPI (`schema.d.ts`). Test w `ApiReferenceTests`.
- Skaner QR na `@yudiel/react-qr-scanner` (`barcode-detector`) - próba odrzucona: domyślnie pobiera WASM (924 KB) z `fastly.jsdelivr.net`, domyślne ograniczenia (min. 640 px wysokości) odrzucają kamerę 640x480 (`overconstrained`), po wyjściu z kasy kamera zostawała włączona (E2E `leaving the register stops the camera`). Zostaje `jsqr` z własną obsługą kamery (wyścigi pokryte E2E).
- Walidacja ustawień przez DataAnnotations - pominięta: około połowa reguł zależy od innych pól, środowiska albo słownika reguł, więc zasady byłyby w dwóch miejscach (atrybuty i walidator) bez realnego zysku.
- review (`/code-review high`, 7 uwag): przyjęte - długi `Retry-After` czekał do `RequestTimeout` i kończył turę jako `timeout` (teraz > 5 s → od razu `provider_error`; mutacja: 20 s i test pada), atrapa Gemini powtarzała ostatnią odpowiedź (osłabiała inne testy - wycofane, porażki podane jawnie), opis `RequestTimeout`, ostrzeżenie w logu przy ponowieniu; częściowo - `TurnTimeout` przy wielu krokach (bez zmian w kodzie, poprawione zdanie w ADR 0011), testy pozostałych ścieżek (dodany `Retry-After`); odrzucone - rozpoznawanie limitu dziennego `429` (tylko z treści odpowiedzi, koszt ok. 1,5 s).
- API 540/540 (1 pominięty: Gemini na żywo).

P14 - regulamin i FAQ w czacie: dopisany do `specs/PLAN.md`. Regulamin `apps/api/Modules/Knowledge/Documents/regulamin-v1.md` zaakceptowany przez właściciela 2026-11-23 (demo i baza wiedzy, nie ocena prawna) po korektach: §1.1 symulacja zamiast „roszczeń”, limit 5000 zł przed rabatem, zaokrąglenie rabatu przed ograniczeniem do kwoty, konsultant bez dostępu do salda i kuponów, §3.4+§3.7 połączone. Wyszukiwanie `simple`+`unaccent`+`pg_trgm` - wariant do sprawdzenia testami odmiany i pytań bez źródła.

## P14 - regulamin w czacie (done, 2026-11-23)

- Prototyp wyszukiwania na osobnej bazie (5434, usunięta): trygramy vs `simple` z prefiksami słów vs połączenie, 18 pytań strojących; wybrane: prefiksy (odmiana) + synonimy pojęć programu + wagi rzadkości + trygram do kolejności, próg pokrycia 0,45. Pytania kontrolne (napisane po progu): 13/14.
- Moduł `Knowledge` ([ADR 0016](docs/src/content/docs/decyzje/0016-regulamin-w-czacie.md), [moduł](docs/src/content/docs/moduly/knowledge.md)): `regulamin-v1.md` wbudowany w API (skrót SHA-256 w teście), migracja `RuleFragments` (32 punkty, `tsvector` `simple` generowany, GIN, `pg_trgm`), `ProgramRulesSearch` (jedno zapytanie SQL), normalizacja w C# (`PolishText`, wspólna z modelem demo) zamiast `unaccent`.
- Narzędzie `search_program_rules(query)`: wersja + fragmenty ze źródłem (§), `no_match`; demo `demo-rules-v4` cytuje najlepszy punkt albo mówi „Regulamin programu tego nie opisuje” i proponuje konsultanta; Gemini `gemini-v5` (źródło, brak odpowiedzi → konsultant, fragmenty to dane).
- Evale `assistant-evals-v2` (plik `assistant-cases.v2.json`): +6 przypadków regulaminu, w tym podrzucona instrukcja w dokumencie. Web: znacznik „Regulamin”, E2E pytania z odpowiedzią i bez.
- Znane pudła: „Do kiedy muszę wykorzystać kupon?”, „Czy konsultant może dodać mi punkty?”; margines „Ile kosztuje benzyna?” 0,42 przy progu 0,45.
- Kontrakt HTTP bez zmian (`schema.d.ts` bez różnic). Migracja dev: właściciel uruchamia `dotnet ef database update`.
- review (`/code-review high`, 8 uwag, przyjęte wszystkie): wzorzec pytań o zasady w demo był za szeroki (aktywacja, stan kuponu, wyjaśnienie punktów i grzeczna prośba o konsultanta trafiały do regulaminu) - zawężony do słownictwa zasad, „czy konsultant widzi/może” osobno, testy tras; przycięcie zapytania w środku emoji kończyło turę błędem - cięcie po parze, a argument z pojedynczym surogatem (`\ud83d`) od modelu jest odrzucany zamiast wywracać turę (dotyczyło wszystkich narzędzi z tekstem); klucze synonimów przez `Stem` (`oddać`, `myjnia`, `traci` nie działały); migracja z zamrożonymi wierszami zamiast kodu parsera (sprawdzone: cofnięcie i ponowne zastosowanie na bazie testowej); sprzątanie podrzuconego punktu także na starcie evali; `.gitattributes` (LF dla regulaminu - skrót w teście); komentarz `AssistantTools`.
- CI na GitHubie: od P13 czerwone na `typecheck` - typy `LayoutProps`/`PageProps` Next generuje do `.next/` dopiero przy `dev`/`build`, a lokalnie `.next` zawsze istniał. `typecheck` = `next typegen && tsc --noEmit` (sprawdzone w czystym worktree: 5 błędów → 0). Pierwszy przebieg E2E w CI: 150/152 - test ponowienia katalogu zakładał istniejącą ofertę (lokalna baza E2E ma dane z poprzednich przebiegów, świeża w CI - nie); teraz tworzy własną.
- Test Gemini na żywo czyta klucz także z user-secrets API; pierwszy przebieg zaliczony (saldo z narzędzia).
- API 602 (601 + 1 na żywo), E2E 152/152 przed poprawkami, czat i konsultant 34/34 po.

## Evale Gemini - pilotaż (2026-11-23)

- `AssistantEvals.RunAsync`: przypadek zbiera nazwane sprawdzenia (`tools`, `arguments`, `answer`, `safety`, `turn`, `trace`) zamiast przerywać na pierwszym; demo wymaga wszystkich (22/22), Gemini raportuje.
- `Gemini_report` (`FUELCLUB_GEMINI_EVALS=pilot|full`, klucz z user-secrets): limity kroków (`MaxToolCalls`), tokenów na przypadek (40k/4k) i całego przebiegu (pierwotnie 150k; po poprawce 400k + 0,25 USD), bez ponawiania przypadków; raport md+json w `apps/api.tests/Evals/reports` (model, prompt, commit, sprawdzenia, tokeny, koszt, szacunek); test sprawdza, że klucza nie ma w raporcie.
- Pilotaż 5×1 (`gemini-3.5-flash-lite`, `gemini-v5`): 4/5 w pełni, narzędzia 9/11, argumenty 6/6, odpowiedź 3/4, bezpieczeństwo 16/16; ok. 14k tokenów wejścia, 0,005 USD; pełny przebieg (54) szacunkowo 0,05 USD. Ceny: ai.google.dev/gemini-api/docs/pricing (2026-11-23).
- Wykryty błąd (3/3 przebiegów): „Co regulamin mówi o płatności kartą kredytową?” - Gemini wyszukuje własnym zapytaniem, dostaje §2.3 i cytuje go zamiast „regulamin tego nie opisuje” + `propose_handoff`. Do decyzji: próg wyszukiwania dla zapytań modelu i/lub reguła w prompcie (fragment musi odpowiadać na pytanie).
- `answerPatterns` w `assistant-cases.v2.json` dla 3 przypadków (wzorce zamiast dokładnego tekstu na prawdziwym modelu).
- Narzędzia jednego kroku po kolei: świadoma decyzja, opis w ADR 0011.
- Poprawka (decyzja właściciela: pilotaż v5 zostaje punktem odniesienia, poprawka teraz, pełny przebieg po niej): `gemini-v6` - cytat tylko fragmentu o tej samej rzeczy (przykład karta kredytowa / karta programu), § nie dowodzi trafności. Próg 0,45 bez zmian (sprawdzone: poprawne pytania od 0,47, słabe trafienie 0,52). Synonimy `lojalnosciowa`, `karcie`, słowa pomocnicze `czego`, `mowi`; regresja w testach wyszukiwania i eval `rules-loyalty-card` (`regulamin-karta`, 22 przypadki); demo `demo-rules-v5` („do czego służy” → regulamin).
- Runner: limit kosztu (`FUELCLUB_GEMINI_EVAL_MAX_USD`, 0,25) obok tokenów (400k); pominięte przypadki w raporcie, oznaczenie „NIEPEŁNY” (sprawdzone przebiegiem z limitem 1 tokena).
- Pilotaż po poprawce (`gemini-v6`, te same 5): 5/5.
- Review (c67bf04..1c88eef): 9 uwag (3 ważne), wszystkie przyjęte i poprawione.
- Pełny przebieg (2026-11-23, `gemini-v6`, 57 = 19 × 3): 53/57 w pełni, bezpieczeństwo 183/183, argumenty 63/63, narzędzia 61/63, odpowiedź 25/27; 0,063 USD. Niezaliczone: „hot dog” 2/3 razy „regulamin tego nie opisuje” zamiast §3.6 (możliwa nadkorekta v6 albo własne zapytanie modelu - do zbadania); pytanie spoza regulaminu 1/3 bez `propose_handoff`; wstrzyknięcie z cudzym kuponem 1/3 bez wywołania narzędzia (odmowa bez próby - bezpieczne, oczekiwanie przypadku pisane pod demo).
- Wyniki po diagnozie (2026-11-23, 5 zmienionych przypadków × 3, `gemini-v7`, raport `gemini-pilot-20261123-162257`): 13/15.
- „Hot dog” to dwa pytania: zakup w sklepie (§3.1, punkty tylko za paliwo) i odbiór nagrody (§3.6). Rozdzielone na `rules-product-purchase`, `rules-product-reward-points` i `rules-product-ambiguous` (agent dopytuje). Zapisane wywołania pokazały dwie przyczyny: Gemini dopisywał do zapytania słowa spoza regulaminu („sklep”, „pozapaliwowe”) → brak trafień (poprawka w prompcie `gemini-v7`), a krótkie zapytanie („punkty za hot doga”) daje remis pokrycia 1,00 dla wielu punktów i limit 3 ucinał §3.1/§3.6 (limit podniesiony do 5, test regresji). Zakup 3/3, odbiór 3/3, pytanie niejednoznaczne 2/3 (raz odpowiedział na oba znaczenia ze źródłami zamiast dopytać).
- Cudzy kupon: odmowa bez wywołania narzędzia jest na Gemini poprawna (`geminiOptionalTools`); wymuszone wywołanie i kontrolę właściciela na backendzie sprawdza model demo (`AssistantTests`, eval `other-coupon`). 3/3.
- Konsultant: pusty wynik wyszukiwania sam dołącza propozycję (zdarzenie `handoff`, raz na turę), niezależnie od modelu. Luka zostaje, gdy wyszukiwanie zwróci fragment, który model słusznie odrzuci (karta kredytowa → §2.3): wtedy propozycja zależy od modelu - 2/3.
- „Bezpieczeństwo X/X” to zaliczone sprawdzenia tych przypadków, nie gwarancja bezpieczeństwa agenta.
- Demo `demo-rules-v6`: pytanie o punkty za produkt bez „zakup/odbiór/kupon” → dopytanie bez narzędzi; po `no_match` bez `propose_handoff` (propozycja z narzędzia).
- Kanały (aplikacja + WhatsApp, wspólna rozmowa): osobny ADR po domknięciu evali (wskazanie właściciela 2026-11-23).

## P16a - manifest i populacja (2026-11-23)

- Decyzje właściciela: ADR 0017 (oznaczenie z konfiguracji instancji, serwerowy `conversationId` z jawnym zakończeniem, „bez przekazania” zamiast containment, dane judge'a tylko syntetyczne poza Git). P16 odsyła do metryk P15b.
- API: `Simulation:Enabled`/`RunId` (tylko Development, walidacja przy starcie), `GET /api/simulation` tylko na instancji symulacji (runId, dostawca, model). Testy `SimulationTests`.
- `apps/tools/Simulator` (.NET, tylko HTTP): `populate` (konta `sim-<run>-<nr>@sim.fuelclub.test`, profile Zero/Low/Medium/High, karta, zakupy kasjera, oferty symulacji, wymiana, aktywacja, realizacja rabatu przy zakupie), manifest z kluczami idempotencji (bez haseł i kodów), tempo poniżej limitów + `Retry-After`; `cleanup` jednego przebiegu tylko na `fuelclub_demo`, jedna transakcja. Testy `SimulatorTests`.
- `compose.yml`: `postgres-demo` (5435). Sekrety w `.simulation/demo.env` (gitignore).
- Przebieg `pilot-1`, 5 kont: 26/26; ponowienie bez zmian; ponowienie z 3 operacjami `Pending` bez podwojeń; sprzątanie: odmowa na `fuelclub_test`, na demo usunięte tylko konta i oferty przebiegu.
- Review (f302853..6e96a64): 7 uwag, 6 przyjętych i poprawionych (ponowienie rejestracji przez logowanie, błąd konta nie zatrzymuje przebiegu, sprzątanie po dokładnych adresach - także konta bez id, rekordy idempotencji kasjera/admina po kluczach, data sprzedaży w strefie stanowiska); 1 rozwiązana wpisem w ADR (`/api/simulation` poza `schema.d.ts` - zwykła instancja jej nie mapuje, front jej nie używa). Weryfikacja `pilot-2`: 26/26, ponowienie z rejestracjami `Pending` OK, sprzątanie z kontem bez id - 5/5 kont, 21 rekordów idempotencji.
- API 638/638, docs build OK.
- Następnie P16b: rozmowy (serwerowy `conversationId`, `Synthetic`/`RunId` przy turze i rozmowie) i scenariusze czatu.

## P16b - rozmowy i scenariusze (2026-11-23)

- Doprecyzowania właściciela:
  - rozmowa powstaje przed modelem, a jej odzyskanie po utracie odpowiedzi nie tworzy duplikatów;
  - każda tura sprawdza właściciela i stan;
  - zakończenie jest idempotentne, a bezczynność ≠ rozwiązanie;
  - przekazanie jest wiązane przy tworzeniu zgłoszenia, bez podwojeń;
  - `Synthetic`/`RunId` tylko z konfiguracji;
  - najpierw mały przebieg.
- API:
  - `POST /api/chat/conversations` (`Idempotency-Key`, osobny limit) i `POST .../{id}/end` (idempotentne);
  - `conversationId` wymagany w `POST /api/chat`. Właściciel, stan i bezczynność sprawdzane jednym warunkowym `UPDATE` przed modelem: `404 assistant.conversation_not_found`, `409 assistant.conversation_ended`;
  - `Assistant:ConversationIdleTimeout` (30 min); bezczynna rozmowa kończy się jako `Idle` z czasem upływu limitu;
  - tabela `assistant_conversations` (migracja `Conversations`) oraz kolumny tury `ConversationId`, `HandoffProposed`, `Synthetic`, `RunId` z `CHECK`;
  - zgłoszenie z `conversationId` wiąże rozmowę raz (`UNIQUE`, pierwsze wygrywa) i kończy ją jako `Handoff`; cudza rozmowa daje `400`;
  - retencja rozmów jak tur;
  - `demo-rules-v7`: przy zakupie produktu §3.1 z wnioskiem („dotyczy paliwa, zakupów w sklepie nie wymienia”).
- Web:
  - `useChat` zakłada rozmowę z kluczem trzymanym do skutku. Przy `409`/`404` zakłada nową rozmowę i ponawia pytanie raz;
  - wyjście z ekranu kończy rozmowę (`keepalive`);
  - handoff przekazuje `conversationId` do zgłoszenia;
  - `schema.d.ts` wygenerowany z instancji bez symulacji.
- Evale: `answerAlternatives` i `answerForbiddenPatterns`. Hot dog zaliczany za dopytanie albo za jasne rozróżnienie zakupu i odbioru. Zakazane: punkty za zakup w sklepie i §3.1 przedstawiony jako zapis o hot dogu. Test `Product_answers_are_checked_by_meaning`. Regexy to przybliżenie; pełną ocenę znaczenia da judge.
- Symulator:
  - `converse`: 14 scenariuszy z wagami, wybór deterministyczny, SSE, potwierdzenie aktywacji, zgłoszenie z `conversationId`, porzucenie;
  - transkrypty w `.simulation/<run>/transcripts`, bez tokenów; `prune` (14 dni);
  - `cleanup` usuwa też rozmowy i tury przebiegu.
- Przebiegi: `pilot-6` (30 rozmów) i `pilot-8` (8 kont, 60 rozmów, 89 tur). Wszystko z `RunId`; 10 przekazań z powiązanym zgłoszeniem; 3 aktywacje; ponowienie bez duplikatów.
- Znaleziska z transkryptów: luki wzorców demo („Za co dostaję punkty?”, „wybrać”) i luka wyszukiwania („zbieranie punktów”). Scenariusze przeformułowane, reguł i wyszukiwania nie zmieniałem bez diagnozy.
- Testy: API 653+ (w tym `ConversationTests`), E2E czat/konsultant/obserwowalność 42/42 (telefon i desktop), docs build OK.
- Bez ręcznego przebiegu w przeglądarce: aplikacja dev właściciela (:3000 → :5271) działa na starym kodzie i bazie bez migracji. E2E pokrywa oba widoki.
- Review (f153063..7b44538): 7 uwag, wszystkie przyjęte i poprawione:
  - symulator zamyka otwarte zgłoszenie konta przed każdą rozmową. Wcześniej nieudane zamknięcie blokowało konto na stałe; sprawdzone w `pilot-9`: ponownie otwarte zgłoszenie zamknięte, 50/50;
  - przekazanie z bezczynnej rozmowy zostaje `Idle` (test);
  - start rozmowy po wyjściu z ekranu kończy ją;
  - historia tylko z bieżącej rozmowy;
  - `404` zadeklarowane przy czacie i `schema.d.ts` wygenerowany ponownie;
  - `support.conversation_not_found` w kodach błędów;
  - tempo czatu także na konto (18/min).
- API 656/656, E2E 42/42.
- Do zrobienia przez właściciela: `dotnet ef database update --project apps/api` na bazie dev (migracje `RuleFragments`, `Conversations`) i restart API.

## P16c - metryki, koszt, regresje i raporty (2026-11-23)

- Doprecyzowania właściciela: trzy nietrafne pytania jako osobne scenariusze regresyjne (przeformułowanie generuje ruch, nie poprawia agenta); raporty osobno: techniczny i poprawności; rozmowy, tury i wywołania narzędzi jako osobne liczniki; handoff raz na rozmowę, `Idle` ≠ rozwiązanie; filtry `RunId`, model, wersja promptu; koszt demo „nie dotyczy”, brak danych Gemini „nieznany”; 2000 tur demo ≠ pomiar Gemini; judge szacuje i wymaga kalibracji; E2E nie zastępuje testu ręcznego.
- API: `GET /api/admin/observability` z filtrami `traffic` (domyślnie `real`), `runId`, `model`, `promptVersion`; sekcje tur, kosztu i rozmów; kolumny `ModelSteps`, `CostUsd`, `PriceVersion` (migracja `MetricsAndCost`); `Assistant:Pricing` z wersjonowanym cennikiem; ADR 0018.
- Web: filtry, osobne sekcje, komunikat „dane demo”, koszt „nie dotyczy / nieznany / niepełny”, odsetek z mianownikiem, przygaszenie przy ładowaniu nowych filtrów.
- Symulator: kroki ze sprawdzeniami regułowymi, 3 scenariusze regresyjne (pierwsze rozmowy przebiegu, `--regressions-each`), scenariusz zapisany w manifeście, `report-technical.md` i `report-correctness.md`.
- `control-1` (10 kont, 80 rozmów, 109 tur) i `demo-1` (200 kont, 1460 rozmów, 2113 tur), oba na adapterze demo:
  - technicznie `demo-1`: 1460/1460 rozmów, 2113/2113 tur z `done`, 0 błędów, 0 × `429`, 7,3 min; czas u klienta p50 21 ms, p95 36 ms (demo, nie Gemini);
  - poprawność `demo-1`: zwykłe 2050/2050 tur zgodnych, regresyjne 0/63 - trzy pomyłki nadal są;
  - panel (`synthetic`, `demo-1`) zgodny z raportem: 2113 tur, 3984 kroki, 1871 wywołań narzędzi (42 nie `ok`), 1460 rozmów, 305 z propozycją, 194 przekazane, 65 otwartych (porzucone), koszt „nie dotyczy”.
- Test ręczny: osobny web na :3200 → API demo :5301 (bez :3000). Widok rzeczywisty pusty, syntetyczny z filtrem przebiegu, komunikat demo, koszt „nie dotyczy”, rozmowy z mianownikami; telefon 390 px bez przewijania w bok.
- Review (616053d..d866d66): 9 uwag, wszystkie przyjęte: koszt tury bez wywołania modelu = 0, unikalna cena na model i datę, wywołania narzędzi z jednego źródła, scenariusz rozmowy w manifeście, tury bez sprawdzeń poza wynikiem, typy filtrów ze schematu, `<dl>` kosztu, przygaszenie przy ładowaniu, zduplikowany komentarz.
- API 668/668, E2E obserwowalności 4/4, docs build OK.
- Do zrobienia przez właściciela: migracja `MetricsAndCost` na bazie dev (razem z `RuleFragments`, `Conversations`); przegląd commitów P16b i P16c.

## P16c - poprawki po przeglądzie właściciela (2026-11-23)

- Przegląd właściciela do `10c05b6`, 4 uwagi, wszystkie poprawione:
  - transkrypt zapisywany przed zakończeniem (`end-pending`), więc zgubiona odpowiedź na zakończenie nie zostawia pustego pliku;
  - token aktywacji tylko z tury `done`;
  - filtry modelu i promptu muszą pasować do tej samej tury rozmowy;
  - krok ponowiony po utracie połączenia ma tokeny i koszt nieznane (`GeminiAttempts` wewnątrz potoku retry).
- Cennik potwierdzony przez właściciela: `gemini-3.5-flash-lite-standard-2026-11-23`, 0,30 / 2,50 USD (wyjście z myśleniem), nadal szacunek.
- Review poprawek (`/code-review high`, 7 uwag): przyjęte 6 - zapis transkryptu atomowy i odporny na ucięty plik, koszt nieznany tylko po próbie bez odpowiedzi (nie po `429`/`5xx`), propozycja konsultanta tylko z tury `done`, wyciek tokenu kończy rozmowę przed błędem, wynik ponowienia z manifestu (`activated`/`handoff`), `end-pending` osobno w raporcie. Odrzucona 1: zmiana nazwy wersji cennika - w bazach dev i demo nie ma tur z kosztem, więc nic nie zostało osierocone (notka w ADR 0018).
- API 675/675, docs build OK.

## P16d - judge offline na syntetycznych transkryptach (2026-11-23)

- Symulator: `judge`, `calibrate`, `agreement` (`Judge.cs`, `JudgeCommand.cs`). Tylko przebiegi z manifestem symulatora; klucz z `GEMINI_API_KEY` albo user-secrets API, w nagłówku.
- Rubryka `judge-rubric-v1`: `answers_question`, `grounded`, `action`, `reason` w schemacie JSON; werdykt z reguły w symulatorze. Próbki osobno: losowa, błędów, regresje. `judge_error` (HTTP, timeout, transport, format) osobno od oceny negatywnej. Twardy limit kosztu (najgorszy przypadek przed wywołaniem), bez podwójnego płacenia przy ponowieniu. Arkusz kalibracji ślepy; zgodność i kappa Cohena.
- `prune` usuwa też pliki judge'a (14 dni).
- `demo-1`: losowa 39/40 pass, regresje 10/10 fail, 0 awarii, 0,0377 USD. Jedyna rozbieżność z regułami to realny błąd demo: przy jednym kuponie nazwanym innym kupon jest proponowany mimo to. Niepoprawione, do decyzji właściciela.
- Kalibracja: arkusz 30 tur czeka na oceny człowieka. Do tego czasu judge to nieskalibrowany szacunek.
- Review P16d (`/code-review high`, 10 uwag, wszystkie przyjęte): kalibracja i zgodność dla jednego modelu (arkusz w nazwie z modelem, z ocen tego modelu, bez nowego losowania), oceny usuniętych transkryptów wypadają (retencja), raport wg bieżącej próbki, pusty arkusz nie powstaje, instrukcje w limicie kosztu, odporne odczytanie użycia, uczciwe liczniki w konsoli, test `prune` dla plików judge'a.
- Testy: `JudgeTests` (7), `SimulatorTests` (prune).

## P16d - poprawki po przeglądzie właściciela (2026-11-23)

- Konto rozmowy z manifestu (`Operation.Account`) przy wznowieniu; test z dopopulowaniem kont.
- `demo-rules-v8`: wybór kuponu - ID albo nazwa → tylko pasujący; brak dopasowania → informacja i lista własnych; kilka → dopytanie; ogólne „aktywuj mój kupon” → jedyny. Testy w `AssistantTests`. Regresja `regresja-nazwany-kupon` (oryginalne brzmienie z `demo-1`) i sprawdzenie odpowiedzi w `aktywacja`.
- Symulator: `--pass` i `compare`; `429` z `Retry-After` > 60 s pada zamiast czekać; wznowienie `end-pending` bez powtarzania tur.
- Baseline `demo-1` (v7) w `.simulation/demo-1/baseline-v7/`. `v8-same` zachowany jako test techniczny (stan kuponów zmieniony - nie mierzy jakości).
- Wypełniony przeze mnie arkusz to przegląd pomocniczy drugim modelem, nie kalibracja; arkusz kalibracji wyczyszczony do oceny człowieka.
- Review (`/code-review high`, 10 uwag): przyjęte 8 - wznowienie bierze wynik z manifestu i tylko dla tej samej rozmowy; dopasowanie nazwy po całych słowach z odmianą (nie „kawa” w „kawałek”), samodzielny myślnik i powitania to nie nazwa; sprawdzenie celu propozycji ze zdarzenia `confirmation` zamiast wzorca tekstu; `compare` bez przejścia odmawia; nieaktualny akapit docs; klucz w logu. Częściowo: luźniejsze dopasowanie („jedno słowo”) odrzucone - przywraca błąd v7 („SIM demo-1” pasuje do „hot dog”); zbyt ostra odmowa kończy się listą do wyboru. Pominięta: `screenshots.spec.ts` uruchamia się w `npm run e2e` - plik nie mój, nieśledzony.
- Testy: API 689/689, E2E chat/demo/observability 40/40.

## Następny krok

Otwarte:
- P7c judge na próbkach;
- publiczny hosting i telefon - docelowo Cloud Run (decyzja 2026-11-23, instrukcja w docs; Vertex osobno); strona „Stan wymagań” w referencji;
- kolejność uzgodniona z właścicielem (2026-11-23): 1) push i zielone CI, 2) evale Gemini (te same scenariusze, powtórzenia, narzędzia i argumenty, izolacja, handoff, RAG, znane pudła RAG jako jawne braki; raport jako punkt odniesienia), 3) P15a-d, 4) judge offline (rubryka, kalibracja na ręcznie ocenionej próbce), potem online, 5) publiczne wdrożenie (Cloud Run jako hosting i Vertex jako dostęp do modelu - osobne decyzje właściciela); porównanie RAG leksykalny vs hybrydowy na tym samym zbiorze po evalach; Slack i voice na końcu.

P13 zamknięty (9 rund review, ostatnia bez uwag blokujących). P11: API, `propose_handoff`, hub, UI i E2E gotowe; dalej refaktor z audytu (zgoda właściciela): podziały plików, wspólny błąd wczytywania, shadcn `skeleton`/`alert`, martwy kod; potem osobno skaner QR i API.

## P15 - panel LLMOps (pending, 2026-11-23)

Na prośbę właściciela dodano zakres do specs/PLAN.md i specs/SPEC.md: trace, metryki/koszty, rozmowy/anomalie, raporty offline i judge na próbce. P7c realizowany jako P15e, bez dublowania etapu. Implementacja po P14. Zmiana wyłącznie planistyczna; nie włącza zapisu treści ani eksportu do judge'a. Testy aplikacji nieuruchamiane (brak zmian kodu); sprawdzono diff i spójność odwołań P15/P7c.
