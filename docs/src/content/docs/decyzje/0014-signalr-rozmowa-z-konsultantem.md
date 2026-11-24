---
title: "0014 - SignalR w rozmowie z konsultantem"
description: Wiadomości bez opóźnienia i status „pisze” przez SignalR; zapis nadal przez POST z idempotencją, kursor zostaje źródłem prawdy.
sidebar:
  order: 14
---

## Status

Decyzja przyjęta (2026-11-23), P11. Zmienia transport z [ADR 0012](/decyzje/0012-przekazanie-do-konsultanta/) (polling co 3 s). Zrealizowana: hub ([Support](/moduly/support/#hub-signalr)) i UI ([interfejs](/moduly/support/#interfejs)). Proxy E2E przekazuje WebSocket, więc E2E sprawdza połączenie na żywo, nie tylko polling.

## Problem

Właściciel chce statusu „Konsultant pisze…” / „Klient pisze…” i wiadomości widocznych od razu. Polling co 3 s daje opóźnienie i nie przeniesie ulotnego sygnału pisania bez zapisu w bazie.

Czat z AI zostaje na SSE ([ADR 0007](/decyzje/0007-czat-i-potwierdzenia/)) - to jednokierunkowy strumień jednej odpowiedzi.

## Rozwiązanie

### Co się nie zmienia

- Wiadomości zapisuje **tylko** `POST .../messages` z `Idempotency-Key` i CSRF. Hub nie zapisuje wiadomości.
- Kursor (`GET .../messages?after={seq}`) jest źródłem prawdy: treść wiadomości klient zawsze pobiera nim, także po ponownym połączeniu.
- Uprawnienia jak w ADR 0012: zapis i odczyt konsultanta tylko jako opiekun otwartego zgłoszenia.

### Hub

- ASP.NET Core SignalR (część frameworka, bez nowego pakietu po stronie API). Ścieżka `/api/support/hub` - pod istniejącym przepisaniem `/api/*` w Next (proxy Nexta przekazuje upgrade WebSocket).
- Wymaga zalogowania z rolą `User` albo `Support`. Uwierzytelnienie ciasteczkiem sesji, jak reszta API. Konsultanci są w grupie `support-agents` (tylko `queueChanged`, bez danych zgłoszeń).
- **Origin**: hub przyjmuje połączenie (negocjacja i upgrade WebSocket; dalsze żądania SSE/long polling przeglądarka wysyła bez originu i chroni je token połączenia z negocjacji) tylko z dozwolonego originu aplikacji - przeglądarka nie wyśle nagłówka CSRF w WebSocket, a sprawdzenie originu chroni przed przejęciem gniazda z obcej strony. Dozwolone originy z konfiguracji (`Support:HubAllowedOrigins`); lokalnie origin Nexta.
- Transporty: WebSocket, przy braku - automatyczny fallback SignalR (SSE, long polling).
- Jedna instancja API; kilka instancji wymagałoby backplane (Redis) - poza zakresem demo.

### Zdarzenia serwer → klient

Bez treści wiadomości - tylko powiadomienia. Odbiorcy wyliczani **w chwili wysyłki z bazy**, po identyfikatorze użytkownika (`Clients.User`), nie z grup dołączonych wcześniej. Po oddaniu, przejęciu przez innego, zamknięciu albo powrocie do kolejki były opiekun przestaje dostawać zdarzenia bez osobnego wypisywania.

| Zdarzenie | Do kogo | Treść |
| --- | --- | --- |
| `messageAdded` | właściciel zgłoszenia i aktualny opiekun | `{ caseId, seq }` - klient dociąga kursorem |
| `caseChanged` | właściciel, aktualny opiekun, a przy zmianie opiekuna także poprzedni | `{ caseId, status }` (przejęcie, oddanie, zamknięcie, wygaśnięcie, powrót do kolejki) |
| `queueChanged` | wszyscy połączeni konsultanci | `{}` - konsultant odświeża kolejkę |
| `typing` | druga strona: opiekun albo właściciel | `{ caseId, author }`, `author` = `Customer` / `Support` |

Zdarzenia wysyła serwer po zatwierdzeniu transakcji: endpointy przez `Response.OnCompleted` (filtr idempotencji zatwierdza przed zapisem odpowiedzi), worker po swoim `Commit`. Wysyłka jest „best effort” - nieudana idzie do logu (sam typ błędu), klient nadrabia kursorem. Payloady w OpenAPI jako schemat `SupportHubContract`.

### Status „pisze”

- Klient i konsultant wywołują metodę huba `Typing(caseId)` podczas pisania, najwyżej co 2 s.
- Serwer przy każdym wywołaniu sprawdza w bazie: właściciel otwartego zgłoszenia albo opiekun zgłoszenia `Assigned`. Inaczej sygnał jest ignorowany (bez błędu, bez przekazania). Ponad 1 sygnał na sekundę z połączenia - ignorowany.
- Odbiorca pokazuje „… pisze” 5 s od ostatniego sygnału; zdarzenie `messageAdded` od tej strony gasi status od razu.
- Status nie trafia do bazy ani logów.

### Ponowne połączenie

- Klient SignalR łączy się ponownie automatycznie. Po połączeniu (pierwszym i każdym kolejnym) pobiera brakujące wiadomości kursorem i stan zgłoszenia.
- Bez połączenia UI wraca do pollingu co 3 s, więc rozmowa działa także wtedy, gdy hub jest niedostępny.

## Alternatywy

- **Polling co 3 s (ADR 0012)**: bez statusu „pisze”, opóźnienie do 3 s. Zostaje jako awaryjny tryb.
- **Treść wiadomości w zdarzeniu huba**: jedno zapytanie mniej, ale dwie drogi treści i kontroli dostępu. Odrzucone - treść tylko przez kursor.
- **Grupy SignalR per zgłoszenie**: członkostwo trzeba by odbierać przy każdej zmianie opiekuna; pominięcie wysyłałoby zdarzenia byłemu opiekunowi. Odrzucone na rzecz odbiorców liczonych z bazy.
- **Wysyłanie wiadomości metodą huba**: bez idempotencji i CSRF z istniejącego potoku. Odrzucone.

## Konsekwencje

- Wdrożenie nowej rewizji api (zmierzone 2026-11-24 na Cloud Run): WebSocket zostaje na starej instancji, która nie obsługuje już nowych wiadomości. Hub milczy, ale się nie rozłącza, więc sam „polling bez połączenia” nie wystarczał - rozmowa stała do zamknięcia połączenia (do 300 s). Dlatego odczyt z kursora co 10 s także przy połączonym hubie. Szczegóły i wyniki: [instrukcja](/instrukcje/obrazy-i-hosting/#rozmowa-w-trakcie-wdrożenia).

- Web: zależność `@microsoft/signalr`.
- Konfiguracja: `Support:HubAllowedOrigins`.
- Testy:
  - API: klient SignalR w testach - powiadomienia trafiają tylko do właściciela i aktualnego opiekuna; po oddaniu były opiekun nie dostaje `messageAdded` ani `typing`, a jego `Typing` nie dociera; obcy origin odrzucony; bez logowania i z rolą `Cashier`/`Admin` odrzucony; sygnał od obcego klienta ignorowany.
  - E2E: konsultant pisze → klient widzi „Konsultant pisze…”, potem wiadomość bez czekania na polling; po zerwaniu i ponownym połączeniu brakujące wiadomości dociągnięte kursorem.
