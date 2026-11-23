---
title: "0012 - Przekazanie rozmowy do konsultanta"
description: Rola Support, zgłoszenie w oknie czatu, atomowe przejęcie, wiadomości z kursorem, zgoda na zapis i retencja 30 dni.
sidebar:
  order: 12
---

## Status

Decyzja przyjęta (2026-11-22), P11. W realizacji: API i `propose_handoff` zrobione ([Support](/moduly/support/)); planowane SignalR ([ADR 0014](/decyzje/0014-signalr-rozmowa-z-konsultantem/)), UI i E2E.

## Problem

Klient czatu nie ma drogi do człowieka. Przekazanie wymaga:

- miejsca, gdzie ktoś odpowiada;
- zapisu treści rozmowy, czego czat dziś nie robi ([ADR 0008](/decyzje/0008-slady-i-evale-czatu/) - tylko metadane);
- zatrzymania bota, żeby dwie strony nie odpowiadały naraz.

Model nie może sam przekazać rozmowy ani dawać konsultantowi uprawnień, których klient by nie chciał.

## Rozwiązanie

### Rola i uprawnienia

- Nowa rola `Support`. Konto demo `konsultant@demo.fuelclub.test`. Na innych środowiskach, także na hostingu: `BootstrapSupport:Email`/`Password`.
  - Sekrety tylko w zmiennych środowiskowych / user-secrets.
  - Bootstrap tworzy tylko nowe konto. Istniejące konto z tym e-mailem **nie** dostaje roli: ostrzeżenie w logu, konto bez zmian.
- `Support` nie ma uprawnień `User`, `Cashier` ani `Admin`: nie nalicza punktów, nie realizuje kuponów, nie zmienia ofert, nie widzi salda, kart ani kuponów klienta.
- Konsultant widzi tylko zgłoszenia: nazwę wyświetlaną klienta (bez e-maila) i wiadomości.

### Stany zgłoszenia

`Waiting` → `Assigned` → `Closed`, oraz `Expired`. W oknie czatu: bot → oczekiwanie → konsultant → zakończona albo wygasła.

- Klient ma najwyżej jedno otwarte zgłoszenie (`Waiting` lub `Assigned`) - częściowy indeks UNIQUE w bazie.
- `Assigned` → `Waiting`: konsultant oddaje zgłoszenie do kolejki (tylko aktualny opiekun) albo przekracza limit bezczynności.
- **Wygaśnięcie:** `Waiting` bez aktywności przez `WaitingExpiry` (domyślnie 7 dni) → `Expired`, nie „rozwiązane”. Klient widzi informację „Nikt nie zdążył odpowiedzieć - zgłoszenie wygasło” i może utworzyć nowe.
- **Bezczynność przejętego:** `Assigned` bez wiadomości przez `AssignedIdleTimeout` (domyślnie 24 h) wraca do kolejki z wiadomością systemową; potem obowiązuje wygaśnięcie jak wyżej. Żadne zgłoszenie nie zostaje otwarte bezterminowo.
- Zgłoszenie żyje na serwerze: po przeładowaniu i ponownym zalogowaniu okno czatu wraca do niego.
- Zamyka konsultant przejmujący zgłoszenie albo klient. Po zamknięciu klient może zacząć nową rozmowę z botem.

### Rozpoczęcie przekazania

- Tylko przyciskiem klienta „Porozmawiaj z konsultantem”. Przycisk jest stale dostępny w oknie czatu.
- Model może tylko **zaproponować** przekazanie: nowe narzędzie `propose_handoff` (bez argumentów, niczego nie zmienia) daje zdarzenie SSE `handoff` i UI pokazuje przycisk przy odpowiedzi. Tekst modelu nie przekazuje rozmowy.
- Zdarzenie `error` czatu też pokazuje propozycję (po stronie UI). Bez automatycznego przekazania.
- Przed wysłaniem okno pokazuje zgodę:
  - „Rozmowa z konsultantem jest zapisywana i przechowywana 30 dni od zakończenia.”
  - pole „Dołącz dotychczasową rozmowę z asystentem” - domyślnie **wyłączone**.
- Konsultant widzi wiadomości od przekazania.
- **Dołączona historia to niezaufany tekst klienta**, nie potwierdzony zapis działań systemu. Serwer nie przechowuje rozmów z botem, więc jej nie weryfikuje; klient może wpisać tam cokolwiek, także „asystent aktywował kupon”.
  - Zapis: wiadomości autora `Customer` ze znacznikiem `ClientProvidedHistory` i rolą (`user`/`assistant`) podaną przez klienta, nigdy jako `System` ani `Support` (`CHECK` w bazie).
  - Panel konsultanta pokazuje je osobno, pod nagłówkiem „Tekst przekazany przez klienta - niepotwierdzony”, jako zwykły tekst.
  - Limity jak historia czatu (20 wpisów, 2000 znaków).

### Bot w trakcie zgłoszenia

- Rozpoczęcie przekazania przerywa trwającą odpowiedź bota (UI anuluje strumień).
- Trwająca tura sama też się zatrzymuje: przed każdą odpowiedzią, zdarzeniem narzędzia i propozycją sprawdza otwarte zgłoszenie i kończy się `error assistant.handoff_active` (dopisane przy realizacji, na prośbę właściciela).
- Gdy klient ma otwarte zgłoszenie:
  - `POST /api/chat` → `409 assistant.handoff_active` przed strumieniem;
  - `POST /api/chat/confirmations` → `409 assistant.handoff_active` - stare przyciski aktywacji są wyłączone w UI, a backend odrzuca ich użycie.
- **Wyścig przekazanie-aktywacja.** Sam odczyt stanu przed zapisem nie wystarcza: przekazanie mogłoby się zatwierdzić między sprawdzeniem a aktywacją.
  - Utworzenie zgłoszenia i aktywacja z czatu biorą tę samą blokadę klienta w swojej transakcji: transakcyjny advisory lock `CustomerLock` (zakup, wymiana, karta). Pierwotny zapis „`loyalty_accounts ... FOR UPDATE`” zmieniony przy realizacji - klient bez konta lojalnościowego nie ma takiego wiersza, a advisory lock daje tę samą gwarancję.
  - Aktywacja sprawdza brak otwartego zgłoszenia **pod tą blokadą** i w tej samej transakcji zmienia kupon.
  - Skutek: albo aktywacja zatwierdza się przed zgłoszeniem, albo widzi zgłoszenie i kończy się `409`. Test API uruchamia oba żądania równolegle (bramka blokad jak w testach wyścigów).

### Endpointy

Klient (rola `User`, CSRF dla zmian):

| Metoda | Ścieżka | Opis |
| --- | --- | --- |
| `POST` | `/api/support/cases` | Utworzenie. `Idempotency-Key`. Treść `{ message, history? }` (`history` jak w czacie, tylko gdy klient zaznaczył dołączenie). `201`, a gdy otwarte zgłoszenie już jest - `409 support.case_open` z jego `id`. |
| `GET` | `/api/support/cases/current` | Otwarte zgłoszenie (`200`) albo `204`. Okno czatu woła to przy wejściu. |
| `GET` | `/api/support/cases/{id}/messages?after={seq}` | Wiadomości po kursorze + stan zgłoszenia. Cudze → `404`. |
| `POST` | `/api/support/cases/{id}/messages` | `Idempotency-Key`, `{ text }` 1-1000 znaków. Zamknięte → `409 support.case_closed`. |
| `POST` | `/api/support/cases/{id}/close` | Idempotentne co do skutku. |

Konsultant (rola `Support`, CSRF dla zmian). Ścieżki pod `/api/support/agent/...` (zmiana przy realizacji: jedna ścieżka dla dwóch ról mieszałaby autoryzację i typy odpowiedzi w jednym handlerze):

| Metoda | Ścieżka | Opis |
| --- | --- | --- |
| `GET` | `/api/support/agent/queue` | Oczekujące (najstarsze pierwsze) i moje przejęte: nazwa klienta, czas, pierwsza wiadomość. |
| `POST` | `/api/support/agent/cases/{id}/claim` | Atomowe przejęcie: warunkowy `UPDATE ... WHERE Status = 'Waiting'`. Przegrany → `409 support.case_taken`; ponowienie przez tego samego konsultanta → `200`. |
| `GET` | `/api/support/agent/cases/{id}/messages?after={seq}` | Tylko przejmujący. Inny konsultant → `403 support.not_assignee`. |
| `POST` | `/api/support/agent/cases/{id}/messages` | Tylko przejmujący, `Idempotency-Key`. |
| `POST` | `/api/support/agent/cases/{id}/release` | Oddanie do kolejki: warunkowy `UPDATE ... WHERE AssignedTo = ja AND Status = 'Assigned'` → `Waiting`. Od tej chwili poprzedni opiekun dostaje `403 support.not_assignee`. Wiadomości zostają, bot dalej wyłączony. |
| `POST` | `/api/support/agent/cases/{id}/close` | Tylko przejmujący. |

### Wiadomości i kursor

- `SupportMessages(CaseId, Seq)` z UNIQUE. `Seq` nadawany pod blokadą wiersza zgłoszenia (`UPDATE ... "LastSeq" = "LastSeq" + n RETURNING`), więc kolejne wiadomości jednego zgłoszenia są widoczne w kolejności zapisu, bez luk.
- Odczyt: `after` = ostatni otrzymany `Seq`. Ten sam kursor daje te same wiadomości; nic nie ginie ani się nie dubluje.
- Autor: `Customer`, `Support`, `System` (kod zdarzenia, np. `claimed` → „Konsultant dołączył”), plus znacznik `ClientProvidedHistory` dla dołączonej rozmowy.

### Polling

:::note[Zmienione]
Transport rozmowy z konsultantem zmienia [ADR 0014](/decyzje/0014-signalr-rozmowa-z-konsultantem/): SignalR z powiadomieniami i statusem „pisze”; polling zostaje jako tryb awaryjny.
:::

- Klient i konsultant odpytują co 3 s, tylko przy otwartej karcie i otwartym zgłoszeniu. Po `Closed` odpytywanie się kończy.
- Odpowiedź niesie stan zgłoszenia, więc przejęcie i zamknięcie widać przy następnym odczycie.

### Dane, logi, retencja

- Treść wiadomości tylko w bazie. Nie trafia do logów, śladów ani zdarzeń bezpieczeństwa; logi i spany mają id zgłoszenia i stan.
- Zgłoszenie i jego wiadomości są usuwane 30 dni po zamknięciu albo wygaśnięciu. Wygaszanie, powrót do kolejki i usuwanie robi worker jak w [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/).
- Limity: tworzenie zgłoszeń i wiadomości na użytkownika (domyślnie 5 zgłoszeń/h, 30 wiadomości/min).

## Alternatywy

- **WebSocket/SSE dla konsultanta**: krótsze opóźnienie, ale nowe połączenia, skalowanie i testy. Polling wystarcza na demo.
- **Admin jako konsultant**: miesza zarządzanie ofertami z dostępem do rozmów. Odrzucone.
- **Automatyczne przekazanie po błędach**: zapis treści bez decyzji klienta. Odrzucone.
- **Kursor po globalnym `bigserial`**: transakcje mogą zatwierdzić się w innej kolejności niż numery, więc czytający pominąłby wiadomość. Odrzucone na rzecz `Seq` per zgłoszenie.

## Konsekwencje

- Pierwsza tabela z treścią rozmów: zakres danych i retencja jak wyżej, opis w dokumentacji modułu.
- Czat ma nowy stan i nowy kod błędu; Gemini dostaje piątą deklarację narzędzia (bez nowych danych dla Google).
- Testy:
  - API: izolacja klientów, `Support` bez dostępu do operacji kasy i admina, wyścig dwóch przejęć (jeden wygrywa), wyścig przekazania z aktywacją, oddanie do kolejki, wygaśnięcie i bezczynność, bootstrap nie awansuje istniejącego konta, historia oznaczona jako niezaufana, kursor przy równoległych wiadomościach, idempotencja tworzenia i wiadomości, `409 handoff_active` dla czatu i potwierdzenia, retencja.
  - E2E: klient przekazuje i czeka, konsultant przejmuje i odpowiada, klient widzi odpowiedź, zamknięcie, powrót po ponownym logowaniu.
