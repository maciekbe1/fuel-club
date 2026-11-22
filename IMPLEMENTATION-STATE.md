# Dziennik wznowienia

Spec: `specs/SPEC.md`. Plan: `specs/PLAN.md`. Pętla: `specs/LOOP.md`.

HEAD / branch: main (commity kroków lokalnie, bez push; ostatni push: c4a7883)
Etap i status: P0 - done; P1 - done; P2 - done; P3 - done; P4 - done; P5 - done; P6 - done; P7 - pending

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

## Następny krok

P7a - lokalne evals i metadane śladów, wg specs/PLAN.md (rozbicie P7) i SPEC.md §3.
