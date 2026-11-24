---
title: "0021 - Powiadomienie klienta o przyznanych punktach"
description: Toast u klienta po zarejestrowaniu zakupu w kasie - hub SignalR tylko serwer → klient, wysyłka po commicie.
sidebar:
  order: 21
---

## Status

Decyzja przyjęta (2026-11-24). Uzupełnia [ADR 0014](/decyzje/0014-signalr-rozmowa-z-konsultantem/) (SignalR) i [ADR 0004](/decyzje/0004-karta-i-kasa/) (kasa).

## Problem

Klient pokazuje kartę, kasjer rejestruje zakup na innym urządzeniu. Klient dowiadywał się o punktach dopiero po odświeżeniu salda albo historii. Ma od razu zobaczyć, ile punktów dostał i za co.

## Rozwiązanie

- Hub `LoyaltyHub` (`/api/loyalty/hub`), rola `User`, tylko zdarzenia serwer → klient. Brak metod klienta.
- Po `201` z `POST /api/cashier/fuel-purchases` serwer wysyła do właściciela karty (`Clients.User`) zdarzenie `pointsAwarded`: `purchaseId`, `points`, `grossAmount`, `discount`, `paidAmount`, `liters`, `saleDate`, `stationCode`. Bez kodu karty i danych kasjera.
- Wysyłka w `Response.OnCompleted`, czyli po commicie transakcji idempotencji. Błąd (`4xx`/`5xx`) nic nie wysyła. Odtworzenie odpowiedzi z klucza idempotencji nie uruchamia endpointu, więc nie wysyła drugi raz.
- Wysyłka „best effort”: błąd trafia do logu (sam typ), zakup i saldo są już zapisane.
- Front: `features/points-notifications` łączy się dla roli `User` na każdej stronie aplikacji. Toast (sonner) na 10 s z przyciskiem „Zamknij powiadomienie”. Odświeża saldo, historię i kupony z API (zakup mógł wykorzystać kupon) - zdarzenie nie jest źródłem prawdy. Połączenie jak hub konsultanta: ponawia pierwszy start, zaczyna od nowa po poddaniu się automatycznego ponawiania, po ponownym połączeniu czyta saldo, historię i kupony jeszcze raz.
- Ochrona originu wspólna dla obu hubów: moduł rejestruje `GuardedHub(ścieżka, kod błędu)`, `HubOriginGuard` sprawdza `Support:HubAllowedOrigins`. Obcy origin → `403 loyalty.origin_not_allowed`.
- Kontrakt zdarzenia w OpenAPI jako schemat `LoyaltyHubContract` (typy na froncie z `schema.d.ts`).

## Alternatywy

- **SSE `/api/me/events`**: prostszy kanał jednokierunkowy, ale rewrite Next ucina żądanie po 30 s - ciągłe ponowne łączenie i luki.
- **Polling historii co kilka sekund**: bez nowego kanału, ale opóźnienie i stały ruch od każdego klienta.
- **Wspólny hub z konsultantem**: łączyłby moduły Support i Loyalty w jednej klasie.

## Konsekwencje

- WebSocket przez rewrite Next na Cloud Run: sprawdzone 75 s otwartego połączenia z pingami (limit 30 s rewrite dotyczy żądań HTTP, nie WebSocket). Według dokumentacji Cloud Run timeout usługi (300 s) obejmuje też WebSocket - nie sprawdzone; klient łączy się wtedy ponownie sam (`withAutomaticReconnect`).
- Każdy zalogowany klient trzyma połączenie z hubem. Jedna instancja api (Cloud Run `max=1`) - bez backplane'u. Więcej instancji wymaga backplane'u albo affinity, jak hub konsultanta.
- Zdarzenie wysłane, gdy klient był rozłączony, przepada: brak toastu, ale po ponownym połączeniu saldo, historia i kupony są czytane z API.
- Wylogowanie w jednej karcie nie zamyka połączeń w innych kartach tej przeglądarki: do wygaśnięcia ciasteczka mogą tam przyjść toasty poprzedniego użytkownika. Akceptowane w demo; rozwiązanie to zamykanie połączeń użytkownika przy wylogowaniu.
- Toast dostaje każde otwarte okno klienta (zakładki i urządzenia), bo `Clients.User` obejmuje wszystkie połączenia użytkownika.
