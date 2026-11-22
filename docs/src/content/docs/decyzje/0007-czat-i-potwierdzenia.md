---
title: "0007 - Czat, narzędzia i potwierdzenie aktywacji"
description: Kontrakt SSE czatu, zamknięta lista narzędzi, adapter demo i podpisany token potwierdzenia aktywacji kuponu.
sidebar:
  order: 7
---

## Status

Decyzja przyjęta (2026-11-22). Zrealizowane w API: endpoint czatu ze strumieniem SSE, narzędzia, deterministyczny adapter demo, potwierdzenie aktywacji. Interfejs czatu: [P6, planowany](/moduly/assistant/).

## Problem

Czat ma odpowiadać na pytania o saldo, oferty i kupony oraz proponować aktywację kuponu. Model (także przyszły, zewnętrzny) nie może:

- wybrać, czyje dane czyta;
- zmienić stanu bez zgody człowieka;
- wywołać czegokolwiek spoza zamkniętej listy narzędzi.

Tekst modelu („aktywuję kupon”) nie jest zgodą. Potrzebny jest kontrakt strumienia, który front i testy mogą typować, oraz kontrakt potwierdzenia: czym jest, jak długo żyje i co się dzieje przy ponowieniu.

## Rozwiązanie

### Endpoint i strumień

- `POST /api/chat` (rola `User`, CSRF). Treść: `{ message, history? }`.
  - `message`: 1-1000 znaków po przycięciu.
  - `history`: najwyżej 20 wpisów `{ role: "user" | "assistant", text ≤ 2000 }`.
  - Walidacja przed strumieniem: błąd to zwykłe `400` ProblemDetails.
- Odpowiedź `text/event-stream` z nagłówkami:
  - `Cache-Control: no-store`, `X-Accel-Buffering: no` (proxy nie buforuje);
  - `Content-Encoding: identity`.
- Ramka: `event: <typ>` + `data: <JSON w jednej linii>` + pusta linia. Heartbeat to komentarz `: ping` co `HeartbeatInterval` (domyślnie 15 s).
- Zdarzenia (payloady w OpenAPI jako `ChatStreamContract`, typy na froncie z `schema.d.ts`):

| Zdarzenie | Payload | Kiedy |
| --- | --- | --- |
| `meta` | `correlationId`, `provider`, `model`, `promptVersion`, `demo` | zawsze pierwsze |
| `tool` | `name`, `status` (`ok`, `error`, `rejected`) | po każdym wywołaniu narzędzia; bez argumentów i danych |
| `delta` | `text` | fragment odpowiedzi |
| `confirmation` | `proposalId`, `couponId`, `offerName`, `discountPerLiter`, `maxLiters`, `expiresAt`, `token` | propozycja aktywacji |
| `done` | `toolCalls` | koniec sukcesem - **tylko** wtedy |
| `error` | `code`, `message` | koniec błędem; po nim nie ma `done` |

- Każda tura kończy się dokładnie jednym `done` albo jednym `error`. Częściowy tekst przed błędem nie jest sukcesem.
- Kody błędów:
  - `assistant.timeout`: przekroczony czas tury (`TurnTimeout`, 30 s) albo narzędzia (`ToolTimeout`, 5 s);
  - `assistant.tool_limit`: więcej niż `MaxToolCalls` (4) wywołań w turze;
  - `assistant.provider_error`: wyjątek adaptera.
- Rozłączenie klienta (`RequestAborted`) anuluje turę: adapter i narzędzia dostają anulowany token.
- Limit: `ChatPermitLimit` tur na `ChatWindow` na użytkownika (domyślnie 20/min) → `429` przed strumieniem.

### Narzędzia

Zamknięta lista. Nazwa spoza listy, argument nieznany, brakujący lub w złym formacie → `rejected`. Wtedy nie ma wywołania i nie ma danych.

| Narzędzie | Argumenty | Wynik dla modelu |
| --- | --- | --- |
| `get_my_balance` | brak | saldo |
| `list_available_rewards` | brak | aktywne oferty (nazwa, koszt, stawka, limit) |
| `list_my_coupons` | `status?`: `Purchased`, `Active`, `Redeemed` | kupony: id, nazwa, stan, warunki; **bez kodów** |
| `propose_coupon_activation` | `couponId` (GUID) | propozycja albo odmowa; niczego nie zmienia |

- Tożsamość pochodzi tylko z sesji (`ToolContext.UserId`); żadne narzędzie nie ma parametru użytkownika.
- Kod kuponu nie trafia do modelu ani do zdarzeń czatu - pokazuje go ekran „Moje kupony” i odpowiedź potwierdzenia.
- Brak narzędzi naliczania, wymiany punktów, administracji i odbioru.

### Potwierdzenie aktywacji

- `propose_coupon_activation` sprawdza:
  - kupon jest własny i ma stan `Purchased`;
  - cudzy, nieistniejący, aktywny i odebrany → odmowa, bez propozycji.
- Gdy sprawdzenie przejdzie, narzędzie wydaje **token potwierdzenia**: Data Protection (`IDataProtector`, cel `FuelClub.Assistant.CouponActivation.v1`) nad `{ proposalId, userId, couponId, expiresAt }`.
  - Czas ważności: `ConfirmationLifetime`, domyślnie 10 min, liczone z `TimeProvider`.
  - Token jest bezstanowy - nie ma tabeli propozycji.
- `POST /api/chat/confirmations` (rola `User`, CSRF, `Idempotency-Key`, treść `{ token }`). To przycisk w UI, nigdy tekst modelu.
  1. Odszyfrowanie. Zły podpis, inny użytkownik niż sesja albo zły format → `404 assistant.confirmation_invalid` (bez ujawniania, czy kupon istnieje).
  2. Wygasły → `410 assistant.confirmation_expired`.
  3. Wspólna aktywacja (`CouponActivation`, ta sama co `POST /api/me/coupons/{id}/activate`) → `200` z kuponem (`Active`, kod), `404` lub `409` jak przy zwykłej aktywacji.
- **Ponowienie**:
  - Ten sam `Idempotency-Key` → odtworzona odpowiedź.
  - Nowy klucz z tym samym tokenem → aktywny kupon zwraca ten sam kod. Aktywacja jest idempotentna co do skutku, więc token nie musi być jednorazowy.
  - Po odbiorze → `409`.
  - Po wygaśnięciu → `410`: trzeba poprosić czat o nową propozycję.
- Token wiąże zgodę z użytkownikiem, kuponem i propozycją. Kupon innego użytkownika albo podmieniony `couponId` nie przejdzie podpisu.

### Adapter

- `IChatModel.NextAsync(kontekst) → Tool(call) | Reply(text)`. Pętlę (limity, wykonanie narzędzi, zdarzenia) prowadzi `ChatService`. Przyszły adapter Gemini wpina się w ten sam kontrakt (function calling).
- Domyślny `DemoChatModel` działa na regułach słów kluczowych i nie wymaga sekretów. Jest jawnie oznaczony: `meta.provider = "demo"`, `meta.demo = true`, a pierwsza wypowiedź mówi, że to tryb demo.
- Prośby o naliczenie punktów, odbiór, wymianę lub administrację → odmowa tekstem, bez narzędzia.

### Moduły

`Modules/Assistant` czyta saldo, katalog i kupony przez publiczne zapytania modułów Loyalty i Rewards. Aktywacja idzie przez `Rewards/Coupons/CouponActivation`, wydzieloną z handlera HTTP tylko po to, żeby obie ścieżki miały jedną logikę.

## Alternatywy

- **Propozycje w tabeli** (stan, jednorazowość): więcej migracji i sprzątania. Skutek aktywacji jest i tak idempotentny, a wiązanie z użytkownikiem i kuponem daje podpis. Odrzucone.
- **Zgoda w tekście** („tak”): model mógłby ją zmyślić albo dać się przekonać (prompt injection). Odrzucone.
- **WebSocket**: większa złożoność, brak potrzeby dwukierunkowości. SSE przez `fetch` (POST z CSRF) wystarcza.
- **`ITimeLimitedDataProtector`**: używa własnego zegara, więc testy nie mogą przesunąć czasu. Data ważności jest więc w payloadzie i sprawdzana przez `TimeProvider`.

## Konsekwencje

- Kontrakt strumienia jest w OpenAPI, ale bez opisu sekwencji. Kolejność i końce tury opisuje ten ADR i testują testy API.
- Token zawiera identyfikatory (zaszyfrowane). Odzyskanie Data Protection keys pozwala je odczytać. Na produkcji klucze muszą być trwałe i chronione ([P9](/instrukcje/)), inaczej restart unieważnia propozycje.
- Adapter demo nie rozumie języka. Evals (P7) sprawdzają narzędzia, właściciela danych i skutki, nie jakość tekstu.
