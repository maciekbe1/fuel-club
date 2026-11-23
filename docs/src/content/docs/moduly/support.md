---
title: Support (konsultant)
description: Przekazanie rozmowy z czatu do konsultanta - zgłoszenia, atomowe przejęcie, wiadomości z kursorem i blokada czatu w trakcie.
sidebar:
  order: 6
---

:::caution[W trakcie - P11]
Zrealizowane: rola `Support`, model zgłoszeń i wiadomości, utworzenie i zamknięcie zgłoszenia przez klienta, atomowe przejęcie, blokada czatu i potwierdzeń przy otwartym zgłoszeniu (także wyścig z aktywacją). Planowane: pozostałe endpointy (stan, wiadomości z kursorem, oddanie, zamknięcie przez konsultanta, kolejka), worker (wygaśnięcie, bezczynność, retencja 30 dni), bootstrap konta, `propose_handoff`, interfejs klienta i konsultanta. Decyzja: [ADR 0012](/decyzje/0012-przekazanie-do-konsultanta/).
:::

## Co robi

Klient z okna czatu prosi o rozmowę z człowiekiem. Powstaje zgłoszenie w kolejce; konsultant (rola `Support`) je przejmuje i odpowiada. Dopóki zgłoszenie jest otwarte, bot nie odpowiada i nie aktywuje kuponów.

## Gdzie jest kod

| Co | Plik |
| --- | --- |
| Rejestracja, trasy, limit tworzenia | `apps/api/Modules/Support/SupportModule.cs` |
| Kody błędów, problemy, ustawienia, operacje wspólne (blokada czatu, numeracja wiadomości) | `SupportCore.cs` |
| Encje, stany, zdarzenia systemowe | `Domain/SupportCase.cs` |
| Tabele, `CHECK`, indeksy | `Infrastructure/SupportModelConfiguration.cs` |
| Utworzenie i zamknięcie zgłoszenia (klient) | `Customer/CreateCaseEndpoint.cs`, `Customer/CloseCaseEndpoint.cs` |
| Przejęcie (konsultant) | `Agent/ClaimCaseEndpoint.cs` |
| Blokada czatu i potwierdzeń | `apps/api/Modules/Assistant/Chat/ChatEndpoints.cs` |

## Przepływ

1. Klient: `POST /api/support/cases` z pierwszą wiadomością i - jeśli zaznaczył - historią czatu. Transakcja idempotencji, potem **blokada klienta** (`CustomerLock`, ta sama co przy zakupie i wymianie), sprawdzenie otwartego zgłoszenia, zapis zgłoszenia `Waiting` i wiadomości.
2. Konsultant: `POST /api/support/agent/cases/{id}/claim` - warunkowy `UPDATE ... WHERE "Status" = 'Waiting'`. Przegrany czeka na blokadę wiersza zwycięzcy, potem warunek już nie pasuje → `409 support.case_taken`. Zwycięzca przy ponowieniu dostaje `200`. Przejęcie dopisuje wiadomość systemową `claimed`.
3. Czat przy otwartym zgłoszeniu (`Waiting` albo `Assigned`):
   - `POST /api/chat` → `409 assistant.handoff_active` przed strumieniem (zwykły odczyt: tura niczego nie zmienia);
   - `POST /api/chat/confirmations` → `409 assistant.handoff_active`. Sprawdzenie i aktywacja są **pod blokadą klienta** w jednej transakcji, więc aktywacja albo zatwierdza się przed zgłoszeniem, albo widzi zgłoszenie. Sam odczyt bez blokady mógłby trafić między początek tworzenia zgłoszenia a jego zapis.
   - Odmowa potwierdzenia jest ostateczną odpowiedzią i zostaje zapisana pod `Idempotency-Key`: po zamknięciu zgłoszenia ponowienie idzie **nowym kluczem** (ponowna aktywacja aktywnego kuponu jest bezpieczna).
   - Aktywacja w „Moich kuponach” (`/api/me/coupons/{id}/activate`) działa dalej - to decyzja klienta, nie bota.

4. Klient: `POST /api/support/cases/{id}/close` - warunkowy `UPDATE` otwartego zgłoszenia na `Closed` z wiadomością `closed_by_customer`; czat działa znowu. Powtórzenie zwraca stan bez zapisu, cudze → `404`.

## Dane

### `SupportCases`

| Kolumna | Opis |
| --- | --- |
| `Id` | UUID v7 |
| `UserId` | klient, FK `RESTRICT` |
| `Status` | `Waiting`, `Assigned`, `Closed`, `Expired` |
| `AssignedToUserId` | konsultant; ustawiony dokładnie w `Assigned`, pusty w `Waiting` |
| `CreatedAt`, `LastActivityAt` | utworzenie; ostatnia wiadomość lub zmiana stanu (wygaśnięcie, bezczynność) |
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

- Klient: rola `User`, CSRF, `Idempotency-Key`, limit tworzenia `Support:CasePermitLimit` na `Support:CaseWindow` (domyślnie 5/h).
- Konsultant: rola `Support`, CSRF. `Support` nie ma uprawnień `User`, `Cashier` ani `Admin` (portfel, kupony, karta, kasa, admin → `403`); widzi nazwę wyświetlaną klienta, nie e-mail.
- Walidacja: wiadomość 1-1000 znaków po przycięciu; historia jak w czacie (`Assistant:MaxHistoryItems`, `Assistant:MaxHistoryTextLength`), tylko role `user`/`assistant`.

## Testy

| Plik | Co sprawdza |
| --- | --- |
| `apps/api.tests/SupportTests.cs` | zgłoszenie z historią: wiadomości `Customer` z `ClientProvidedHistory` i rolą, kolejne `Seq`; ten sam klucz → to samo zgłoszenie, inny → `409 support.case_open` z `caseId`; walidacja (pusta, za długa, rola `system`, 21 wpisów) bez zapisu; **przejęcie z bramką: pierwszy trzyma wiersz, drugi czeka na blokadę wiersza → `200` i `409 support.case_taken`**, ponowienie zwycięzcy `200`, jedna wiadomość `claimed`; 4 równoległe przejęcia → jedno; nieznane `404`, zamknięte `409 support.case_closed`, `User`/`Admin`/`Cashier` → `403`; `Support` bez dostępu do portfela, kuponów, karty, admina, kasy i tworzenia zgłoszeń; **wyścig: aktywacja z blokadą klienta pierwsza → `200`, zgłoszenie czeka i powstaje; zgłoszenie z blokadą pierwsze (przed zapisem) → aktywacja czeka i dostaje `409 assistant.handoff_active`, kupon nieaktywny**; czat przy otwartym zgłoszeniu → `409` bez strumienia; zamknięcie: cudze `404`, właściciel dwa razy `200` z jedną wiadomością `closed_by_customer`, potem potwierdzenie (nowy klucz), czat i nowe zgłoszenie działają; baza: drugie otwarte zgłoszenie, powtórzony `Seq`, historia jako `System`, wiadomość `Support` bez autora, `Assigned` bez opiekuna |

Mutacje sprawdzone: bez blokady klienta w potwierdzeniu oba testy wyścigu padają; przejęcie bez warunku `Waiting` - oba testy przejęcia padają.
