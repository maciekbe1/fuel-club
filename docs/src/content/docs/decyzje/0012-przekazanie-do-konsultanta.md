---
title: "0012 - Przekazanie rozmowy do konsultanta"
description: Rola Support, zgłoszenie w oknie czatu, atomowe przejęcie, wiadomości z kursorem, zgoda na zapis i retencja 30 dni.
sidebar:
  order: 12
---

## Status

Proponowana (2026-11-22), P11. Nie zrealizowana - opis kontraktu do akceptacji przed implementacją.

## Problem

Klient czatu nie ma drogi do człowieka. Przekazanie wymaga:

- miejsca, gdzie ktoś odpowiada;
- zapisu treści rozmowy, czego czat dziś nie robi ([ADR 0008](/decyzje/0008-slady-i-evale-czatu/) - tylko metadane);
- zatrzymania bota, żeby dwie strony nie odpowiadały naraz.

Model nie może sam przekazać rozmowy ani dawać konsultantowi uprawnień, których klient by nie chciał.

## Rozwiązanie

### Rola i uprawnienia

- Nowa rola `Support`. Konto demo `konsultant@demo.fuelclub.test`. Na innych środowiskach `BootstrapSupport:Email`/`Password` jak kasjer.
- `Support` nie ma uprawnień `User`, `Cashier` ani `Admin`: nie nalicza punktów, nie realizuje kuponów, nie zmienia ofert, nie widzi salda, kart ani kuponów klienta.
- Konsultant widzi tylko zgłoszenia: nazwę wyświetlaną klienta (bez e-maila) i wiadomości.

### Stany zgłoszenia

`Waiting` → `Assigned` → `Closed`. W oknie czatu: bot → oczekiwanie → konsultant → zakończona.

- Klient ma najwyżej jedno otwarte zgłoszenie (`Waiting` lub `Assigned`) - częściowy indeks UNIQUE w bazie.
- Zgłoszenie żyje na serwerze: po przeładowaniu i ponownym zalogowaniu okno czatu wraca do niego.
- Zamyka konsultant przejmujący zgłoszenie albo klient. Po zamknięciu klient może zacząć nową rozmowę z botem.

### Rozpoczęcie przekazania

- Tylko przyciskiem klienta „Porozmawiaj z konsultantem”. Przycisk jest stale dostępny w oknie czatu.
- Model może tylko **zaproponować** przekazanie: nowe narzędzie `propose_handoff` (bez argumentów, niczego nie zmienia) daje zdarzenie SSE `handoff` i UI pokazuje przycisk przy odpowiedzi. Tekst modelu nie przekazuje rozmowy.
- Zdarzenie `error` czatu też pokazuje propozycję (po stronie UI). Bez automatycznego przekazania.
- Przed wysłaniem okno pokazuje zgodę:
  - „Rozmowa z konsultantem jest zapisywana i przechowywana 30 dni od zakończenia.”
  - pole „Dołącz dotychczasową rozmowę z asystentem” - domyślnie **wyłączone**.
- Konsultant widzi wiadomości od przekazania. Dołączona rozmowa jest oznaczona jako przekazana przez klienta (serwer nie przechowuje rozmów z botem, więc nie może jej potwierdzić).

### Bot w trakcie zgłoszenia

- Rozpoczęcie przekazania przerywa trwającą odpowiedź bota (UI anuluje strumień).
- Gdy klient ma otwarte zgłoszenie:
  - `POST /api/chat` → `409 assistant.handoff_active` przed strumieniem;
  - `POST /api/chat/confirmations` → `409 assistant.handoff_active` - stare przyciski aktywacji są wyłączone w UI, a backend odrzuca ich użycie.

### Endpointy

Klient (rola `User`, CSRF dla zmian):

| Metoda | Ścieżka | Opis |
| --- | --- | --- |
| `POST` | `/api/support/cases` | Utworzenie. `Idempotency-Key`. Treść `{ message, history? }` (`history` jak w czacie, tylko gdy klient zaznaczył dołączenie). `201`, a gdy otwarte zgłoszenie już jest - `409 support.case_open` z jego `id`. |
| `GET` | `/api/support/cases/current` | Otwarte zgłoszenie (`200`) albo `204`. Okno czatu woła to przy wejściu. |
| `GET` | `/api/support/cases/{id}/messages?after={seq}` | Wiadomości po kursorze + stan zgłoszenia. Cudze → `404`. |
| `POST` | `/api/support/cases/{id}/messages` | `Idempotency-Key`, `{ text }` 1-1000 znaków. Zamknięte → `409 support.case_closed`. |
| `POST` | `/api/support/cases/{id}/close` | Idempotentne co do skutku. |

Konsultant (rola `Support`, CSRF dla zmian):

| Metoda | Ścieżka | Opis |
| --- | --- | --- |
| `GET` | `/api/support/queue` | Oczekujące (najstarsze pierwsze) i moje przejęte: nazwa klienta, czas, pierwsza wiadomość. |
| `POST` | `/api/support/cases/{id}/claim` | Atomowe przejęcie: warunkowy `UPDATE ... WHERE Status = 'Waiting'`. Przegrany → `409 support.case_taken`; ponowienie przez tego samego konsultanta → `200`. |
| `GET` | `/api/support/cases/{id}/messages?after={seq}` | Tylko przejmujący. Inny konsultant → `403 support.not_assignee`. |
| `POST` | `/api/support/cases/{id}/messages` | Tylko przejmujący, `Idempotency-Key`. |
| `POST` | `/api/support/cases/{id}/close` | Tylko przejmujący. |

### Wiadomości i kursor

- `support_messages(CaseId, Seq)` z UNIQUE. `Seq` nadawany pod blokadą wiersza zgłoszenia (`FOR UPDATE`), więc kolejne wiadomości jednego zgłoszenia są widoczne w kolejności zapisu, bez luk.
- Odczyt: `after` = ostatni otrzymany `Seq`. Ten sam kursor daje te same wiadomości; nic nie ginie ani się nie dubluje.
- Autor: `Customer`, `Support`, `System` (np. „Konsultant dołączył”), plus znacznik `FromAssistantHistory` dla dołączonej rozmowy.

### Polling

- Klient i konsultant odpytują co 3 s, tylko przy otwartej karcie i otwartym zgłoszeniu. Po `Closed` odpytywanie się kończy.
- Odpowiedź niesie stan zgłoszenia, więc przejęcie i zamknięcie widać przy następnym odczycie.

### Dane, logi, retencja

- Treść wiadomości tylko w bazie. Nie trafia do logów, śladów ani zdarzeń bezpieczeństwa; logi i spany mają id zgłoszenia i stan.
- Zgłoszenie i jego wiadomości są usuwane 30 dni po zamknięciu (worker retencji jak w [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/)).
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
  - API: izolacja klientów, `Support` bez dostępu do operacji kasy i admina, wyścig dwóch przejęć (jeden wygrywa), kursor przy równoległych wiadomościach, idempotencja tworzenia i wiadomości, `409 handoff_active` dla czatu i potwierdzenia, retencja.
  - E2E: klient przekazuje i czeka, konsultant przejmuje i odpowiada, klient widzi odpowiedź, zamknięcie, powrót po ponownym logowaniu.

## Otwarte pytania

1. Nieobsłużone `Waiting` - zamykać automatycznie (np. po 7 dniach)?
2. Konsultant oddaje zgłoszenie do kolejki - potrzebne?
3. Konto `Support` na Railway: `BootstrapSupport` wystarczy?
