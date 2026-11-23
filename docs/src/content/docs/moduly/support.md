---
title: Support (konsultant)
description: Przekazanie rozmowy z czatu do konsultanta - zgłoszenia, atomowe przejęcie, wiadomości z kursorem i blokada czatu w trakcie.
sidebar:
  order: 6
---

:::caution[W trakcie - P11]
Zrealizowane (API): rola `Support`, model, wszystkie endpointy klienta i konsultanta, blokada czatu (także tura trwająca w chwili przekazania i wyścig z aktywacją), worker (wygaśnięcie, bezczynność, retencja), `BootstrapSupport` i konto demo. Zrealizowane także `propose_handoff` (zdarzenie `handoff`). Planowane: hub SignalR ([ADR 0014](/decyzje/0014-signalr-rozmowa-z-konsultantem/)), interfejs klienta i konsultanta, E2E. Decyzja: [ADR 0012](/decyzje/0012-przekazanie-do-konsultanta/).
:::

## Co robi

Klient z okna czatu prosi o rozmowę z człowiekiem. Powstaje zgłoszenie w kolejce; konsultant (rola `Support`) je przejmuje i odpowiada. Dopóki zgłoszenie jest otwarte, bot nie odpowiada i nie aktywuje kuponów.

## Gdzie jest kod

| Co | Plik |
| --- | --- |
| Rejestracja, trasy, limity | `apps/api/Modules/Support/SupportModule.cs` |
| Kody błędów, problemy, ustawienia, operacje wspólne (blokada czatu, numeracja i zapis wiadomości z warunkiem, kursor) | `SupportCore.cs` |
| Wygaśnięcie, powrót do kolejki, retencja; worker | `SupportWorker.cs` |
| Encje, stany, zdarzenia systemowe | `Domain/SupportCase.cs` |
| Tabele, `CHECK`, indeksy | `Infrastructure/SupportModelConfiguration.cs` |
| Klient: utworzenie, stan, wiadomości, zamknięcie | `Customer/CreateCaseEndpoint.cs`, `Customer/CustomerCaseEndpoints.cs`, `Customer/CloseCaseEndpoint.cs` |
| Konsultant: przejęcie; kolejka, wiadomości, oddanie, zamknięcie | `Agent/ClaimCaseEndpoint.cs`, `Agent/AgentCaseEndpoints.cs` |
| Blokada czatu i potwierdzeń; zatrzymanie trwającej tury | `apps/api/Modules/Assistant/Chat/ChatEndpoints.cs`, `Chat/ChatService.cs` |
| Konto konsultanta (`BootstrapSupport`), konto demo | `Modules/Accounts/Infrastructure/SupportAccountProvisioner.cs`, `Infrastructure/DemoData/DemoAccountsSeeder.cs` |

## Przepływ

1. Klient: `POST /api/support/cases` z pierwszą wiadomością i - jeśli zaznaczył - historią czatu. Transakcja idempotencji, potem **blokada klienta** (`CustomerLock`, ta sama co przy zakupie i wymianie), sprawdzenie otwartego zgłoszenia, zapis zgłoszenia `Waiting` i wiadomości.
2. Konsultant: `POST /api/support/agent/cases/{id}/claim` - warunkowy `UPDATE ... WHERE "Status" = 'Waiting'`. Przegrany czeka na blokadę wiersza zwycięzcy, potem warunek już nie pasuje → `409 support.case_taken`. Zwycięzca przy ponowieniu dostaje `200`. Przejęcie dopisuje wiadomość systemową `claimed`.
3. Czat przy otwartym zgłoszeniu (`Waiting` albo `Assigned`):
   - `POST /api/chat` → `409 assistant.handoff_active` przed strumieniem (zwykły odczyt: tura niczego nie zmienia);
   - `POST /api/chat/confirmations` → `409 assistant.handoff_active`. Sprawdzenie i aktywacja są **pod blokadą klienta** w jednej transakcji, więc aktywacja albo zatwierdza się przed zgłoszeniem, albo widzi zgłoszenie. Sam odczyt bez blokady mógłby trafić między początek tworzenia zgłoszenia a jego zapis.
   - Odmowa potwierdzenia jest ostateczną odpowiedzią i zostaje zapisana pod `Idempotency-Key`: po zamknięciu zgłoszenia ponowienie idzie **nowym kluczem** (ponowna aktywacja aktywnego kuponu jest bezpieczna).
   - **Tura trwająca w chwili przekazania**: odrzucanie nowych żądań nie zatrzymuje otwartego strumienia. Przed każdą odpowiedzią modelu, zdarzeniem narzędzia i propozycją aktywacji tura sprawdza zgłoszenie ponownie; przy otwartym kończy się zdarzeniem `error` `assistant.handoff_active`, bez odpowiedzi i bez `confirmation`. Okno między sprawdzeniem a wysłaniem zamyka klient (anuluje strumień przed utworzeniem zgłoszenia), a propozycję, która by się prześlizgnęła, odrzuca potwierdzenie pod blokadą.
   - Aktywacja w „Moich kuponach” (`/api/me/coupons/{id}/activate`) działa dalej - to decyzja klienta, nie bota.

4. Rozmowa: obie strony czytają `GET .../messages?after={seq}` i dopisują `POST .../messages`. Odpowiedź niesie stan, więc przejęcie, oddanie i zamknięcie widać przy następnym odczycie. (Interfejs - planowany: powiadomienia SignalR i status „pisze”, polling co 3 s awaryjnie - [ADR 0014](/decyzje/0014-signalr-rozmowa-z-konsultantem/).) Kursor: `after` = ostatni otrzymany `Seq`; ten sam kursor daje te same wiadomości, do 100 naraz.
5. **Zapis konsultanta sprawdza opiekuna**: `UPDATE ... SET "LastSeq" = "LastSeq" + 1 WHERE "Status" = 'Assigned' AND "AssignedToUserId" = ja RETURNING` - sprawdzenie i numer w jednym poleceniu pod blokadą wiersza. Po oddaniu, przejęciu przez innego, zamknięciu albo powrocie po bezczynności były opiekun nic nie zapisze (`403 support.not_assignee`, zamknięte `409 support.case_closed`); wiadomość czekająca na trwające oddanie dostaje `403`. Klient pisze tylko do własnego otwartego zgłoszenia tak samo.
6. Konsultant: `release` (warunkowo na `Waiting`, wiadomość `released`) albo `close` (`Closed`, `closed_by_support`). Odczyt zostaje dla opiekuna także po zamknięciu. Powtórzone zamknięcie zwraca stan; **powtórzone oddanie** (np. po zgubionej odpowiedzi) dostaje `403 support.not_assignee` - zgłoszenie już nie jest jego, więc klient traktuje to jak wykonane i odświeża kolejkę (serwer nie zapisuje, kto oddał).
   Kolejka: zgłoszenia konsultanta i najstarsze czekające, razem najwyżej 100.
7. Klient: `POST /api/support/cases/{id}/close` - warunkowy `UPDATE` otwartego zgłoszenia na `Closed` z wiadomością `closed_by_customer`; czat działa znowu. Powtórzenie zwraca stan bez zapisu, cudze → `404`.

8. Worker (`Support:WorkerInterval`, domyślnie co minutę), każda zmiana warunkowym `UPDATE` po warunku przeterminowania (wiadomość zapisana w międzyczasie przesuwa `LastActivityAt` pod tą samą blokadą i ratuje zgłoszenie):
   - `Waiting` bez aktywności przez `WaitingExpiry` (7 dni) → `Expired`, wiadomość `expired`; klient może utworzyć nowe (interfejs - planowany - pokaże „Nikt nie zdążył odpowiedzieć”);
   - `Assigned`, gdy **konsultant** nic nie zrobił przez `AssignedIdleTimeout` (24 h; liczy się `AgentActivityAt` - przejęcie i wiadomości konsultanta, nie klienta) → `Waiting` bez opiekuna, `idle_returned`; potem obowiązuje wygaśnięcie. Wiadomości klienta nie zatrzymają zgłoszenia przy nieobecnym konsultancie;
   - `Closed`/`Expired` starsze niż `Retention` (30 dni od `ClosedAt`) → usunięte z wiadomościami.
   W logu tylko liczby.

## Konto konsultanta

- Demo: `konsultant@demo.fuelclub.test` (hasło kont demo).
- Inne środowiska: `BootstrapSupport:Email`/`Password` w zmiennych środowiskowych / user-secrets. Tworzy tylko nowe konto z rolą `Support`. Istniejące konto z tym e-mailem **nie** dostaje roli: ostrzeżenie w logu z id konta (bez e-maila), konto bez zmian.

## Dane

### `SupportCases`

| Kolumna | Opis |
| --- | --- |
| `Id` | UUID v7 |
| `UserId` | klient, FK `RESTRICT` |
| `Status` | `Waiting`, `Assigned`, `Closed`, `Expired` |
| `AssignedToUserId` | konsultant; ustawiony dokładnie w `Assigned`, pusty w `Waiting` |
| `CreatedAt`, `LastActivityAt` | utworzenie; ostatnia wiadomość lub zmiana stanu (wygaśnięcie) |
| `AgentActivityAt` | ostatnie przejęcie lub wiadomość opiekuna; ustawione zawsze w `Assigned` (`CHECK`); bezczynność |
| `ClosedAt` | ustawiony dokładnie w `Closed`/`Expired`; od niego liczy się retencja |
| `LastSeq` | ostatni numer wiadomości |

- `UX_SupportCases_OpenPerUser`: częściowy `UNIQUE (UserId) WHERE Status IN ('Waiting', 'Assigned')` - najwyżej jedno otwarte zgłoszenie. Tworzenie sprawdza to pod blokadą klienta; indeks to ostatnia linia.
- `CHECK`: stan, opiekun zgodny ze stanem, `ClosedAt` zgodny ze stanem, `LastSeq >= 0`.

### `SupportMessages`

| Kolumna | Opis |
| --- | --- |
| `CaseId`, `Seq` | `UNIQUE`; `Seq` od 1, bez luk |
| `Author` | `Customer`, `Support`, `System` |
| `AuthorUserId` | konsultant - dokładnie dla `Support` |
| `ClientProvidedHistory`, `HistoryRole` | dołączona historia czatu: zawsze `Customer`, rola `user`/`assistant` tak, jak podał klient |
| `Text` | 1-2000 znaków; dla `System` kod zdarzenia (`claimed`, `released`, `idle_returned`, `closed_by_customer`, `closed_by_support`, `expired`), nie proza |
| `CreatedAt` | `clock_timestamp()` |

- Numeracja: `UPDATE "SupportCases" SET "LastSeq" = "LastSeq" + n ... RETURNING` blokuje wiersz zgłoszenia do końca transakcji, więc piszący do jednego zgłoszenia dostają kolejne numery w kolejności zatwierdzeń. Czytający po `Seq` nie pominie wiadomości.
- Dołączona historia to **niezaufany tekst klienta**: serwer nie przechowuje rozmów z botem i jej nie weryfikuje. `CHECK` nie pozwala zapisać jej jako `System` ani `Support`.
- Treść wiadomości tylko w bazie - nie w logach, śladach ani zdarzeniach bezpieczeństwa.
- Wiadomości usuwa kaskada ze zgłoszeniem. Migracja `Down` odmawia (`P0001`), gdy istnieją zgłoszenia.

## Zabezpieczenia

- Klient: rola `User`, CSRF, `Idempotency-Key` (utworzenie, wiadomość), limit tworzenia `Support:CasePermitLimit` na `Support:CaseWindow` (domyślnie 5/h). Cudze zgłoszenie wygląda jak brak (`404`).
- Wiadomości: `Support:MessagePermitLimit` na `MessageWindow` (30/min) na użytkownika, `Idempotency-Key`.
- Konsultant: rola `Support`, CSRF; odczyt i zapis tylko jako opiekun. `Support` nie ma uprawnień `User`, `Cashier` ani `Admin` (portfel, kupony, karta, kasa, admin → `403`); widzi nazwę wyświetlaną klienta, nie e-mail.
- Walidacja: wiadomość 1-1000 znaków po przycięciu; historia jak w czacie (`Assistant:MaxHistoryItems`, `Assistant:MaxHistoryTextLength`), tylko role `user`/`assistant`.

## Testy

| Plik | Co sprawdza |
| --- | --- |
| `apps/api.tests/SupportTests.cs` | zgłoszenie z historią: wiadomości `Customer` z `ClientProvidedHistory` i rolą, kolejne `Seq`; ten sam klucz → to samo zgłoszenie, inny → `409 support.case_open` z `caseId`; walidacja (pusta, za długa, rola `system`, 21 wpisów) bez zapisu; **przejęcie z bramką: pierwszy trzyma wiersz, drugi czeka na blokadę wiersza → `200` i `409 support.case_taken`**, ponowienie zwycięzcy `200`, jedna wiadomość `claimed`; 4 równoległe przejęcia → jedno; nieznane `404`, zamknięte `409 support.case_closed`, `User`/`Admin`/`Cashier` → `403`; `Support` bez dostępu do portfela, kuponów, karty, admina, kasy i tworzenia zgłoszeń; **wyścig: aktywacja z blokadą klienta pierwsza → `200`, zgłoszenie czeka i powstaje; zgłoszenie z blokadą pierwsze (przed zapisem) → aktywacja czeka i dostaje `409 assistant.handoff_active`, kupon nieaktywny**; czat przy otwartym zgłoszeniu → `409` bez strumienia; zamknięcie: cudze `404`, właściciel dwa razy `200` z jedną wiadomością `closed_by_customer`, potem potwierdzenie (nowy klucz), czat i nowe zgłoszenie działają; baza: drugie otwarte zgłoszenie, powtórzony `Seq`, historia jako `System`, wiadomość `Support` bez autora, `Assigned` bez opiekuna; `BootstrapSupport` tworzy nowe konto tylko z rolą `Support`, powtórzenie bez zmian, istniejące konto klienta nie dostaje roli ani nowego hasła - ostrzeżenie z id, bez e-maila |
| `apps/api.tests/SupportConversationTests.cs` | rozmowa: `current`, kolejka (nazwa klienta, pierwsza wiadomość klienta - nie historia), odczyt przed przejęciem `403`, przejęcie przenosi do „moich”, historia + wiadomość + `claimed`, odpowiedź konsultanta z `Seq`, ten sam kursor - te same wiadomości; obcy klient `404`, obcy konsultant `403 support.not_assignee`, każda strona tylko na swoich ścieżkach, zły kursor i tekst `400`; **po oddaniu były opiekun: wiadomość, odczyt, oddanie, zamknięcie → `403`, inny przejmuje i pisze, były dalej `403`**; **wiadomość w wyścigu z oddaniem (bramka trzyma niezatwierdzone oddanie) czeka na blokadę wiersza i dostaje `403`, nic nie zapisane**; zamknięte: wiadomości obu stron `409`, oddanie `409`, `current` → `204`, odczyt ze stanem `Closed`; 12 równoległych wiadomości → `Seq` bez luk, odczyt małymi krokami zbiera każdą raz; ten sam klucz → jedna wiadomość |
| `apps/api.tests/SupportHandoffTurnTests.cs` | **tura wstrzymana w modelu, w tym czasie zgłoszenie: spóźniona odpowiedź nie wychodzi (`meta`, `error handoff_active`); spóźniona propozycja aktywacji - bez `tool` i `confirmation`, kupon nieaktywny**; ślad tury zatrzymanej przekazaniem ma wynik `Cancelled`, nie `Error` (kolejka przeglądu i reguła powtarzających się błędów go nie liczą); bez zgłoszenia tura odpowiada normalnie; **„chcę porozmawiać z konsultantem” (model demo) → `tool propose_handoff ok`, `handoff`, odpowiedź z nazwą przycisku, żadnego zgłoszenia**; `propose_handoff` z argumentami → odrzucone, bez `handoff` |
| `apps/api.tests/SupportHousekeepingTests.cs` | przejęte: 23 h bez zmian, 25 h → kolejka, `idle_returned`, były opiekun `403`; czekające: 6 dni bez zmian, 8 dni → `Expired`, `expired`, czat wolny, wiadomość `409`, nowe zgłoszenie; retencja: po 29 dniach zamknięte i wygasłe zostają, po 31 usunięte z wiadomościami; **wiadomość klienta nie zatrzymuje zgłoszenia przy konsultancie nieaktywnym 25 h, wiadomość konsultanta tak** |

Mutacje sprawdzone: bez blokady klienta w potwierdzeniu - 2 testy wyścigu padają; przejęcie bez warunku `Waiting` - 2 testy przejęcia; tura bez ponownego sprawdzenia - 2 testy tury; bezczynność liczona od dowolnej aktywności - test wiadomości klienta; zapis konsultanta bez warunku opiekuna - 3 testy rozmowy.
