---
title: Assistant (czat)
description: Czat z narzędziami nad danymi zalogowanego klienta, adapter demo i potwierdzanie aktywacji kuponu.
sidebar:
  order: 4
---

:::caution[W trakcie]
Zrealizowane: `POST /api/chat` (SSE), cztery narzędzia, deterministyczny adapter demo, potwierdzenie aktywacji, ekran `/czat`. Planowane: evals i obserwowalność (P7), adapter dostawcy (opcjonalnie).
:::

## Co robi

Klient pyta o saldo, oferty i swoje kupony, może też poprosić o aktywację kuponu. Asystent odpowiada strumieniem zdarzeń. Aktywację tylko **proponuje**: zmiana następuje dopiero po naciśnięciu przycisku potwierdzenia. Kontrakt, czas ważności potwierdzenia i ponowienia: [ADR 0007](/decyzje/0007-czat-i-potwierdzenia/).

## Gdzie jest kod

| Co | Plik |
| --- | --- |
| Rejestracja, trasy, limit tur | `apps/api/Modules/Assistant/AssistantModule.cs` |
| Ustawienia i walidacja | `AssistantOptions.cs` |
| Kody błędów | `AssistantErrorCodes.cs` |
| Endpointy (czat, potwierdzenie), walidacja wejścia, heartbeat | `Chat/ChatEndpoints.cs` |
| Pętla tury: limity, narzędzia, zdarzenia | `Chat/ChatService.cs` |
| Ramki SSE | `Chat/SseWriter.cs` |
| Payloady zdarzeń (`ChatStreamContract` w OpenAPI) | `Chat/ChatContracts.cs` |
| Narzędzia (zamknięta lista) | `Tools/AssistantTools.cs` |
| Kontrakt modelu, adapter demo | `Model/IChatModel.cs`, `Model/DemoChatModel.cs` |
| Token potwierdzenia | `Confirmation/ConfirmationTokens.cs` |
| Wspólna aktywacja kuponu | `Modules/Rewards/Coupons/CouponActivation.cs` (używa jej też `POST /api/me/coupons/{id}/activate`) |
| Ekran `/czat` | `apps/web/app/(app)/czat/page.tsx`, `apps/web/features/assistant/components/ChatScreen.tsx`, `ConfirmationCard.tsx` |
| Strumień w przeglądarce (fetch + parser SSE) | `apps/web/features/assistant/services.ts` |
| Stan rozmowy, potwierdzenie | `apps/web/features/assistant/hooks.ts` (`useChat`, `useConfirmActivation`) |
| Odczyty z innych modułów | `WalletEndpoints.BalanceAsync`, `CatalogEndpoint.ActiveOffers`, `MyCouponsEndpoints.Own` |

## Przepływ

1. `POST /api/chat`: walidacja (`400`) i limit (`429`) przed strumieniem.
2. Nagłówki SSE, zdarzenie `meta` (`correlationId` także w nagłówku `X-Correlation-Id`), heartbeat co `HeartbeatInterval`.
3. Pętla: model zwraca `Tool` albo `Reply`. Narzędzie działa z `ToolContext.UserId` z sesji i z limitem `ToolTimeout`. Wynik idzie do modelu, a klient dostaje zdarzenie `tool` (nazwa i stan). Propozycja aktywacji daje dodatkowo `confirmation`.
4. `Reply` → `delta` + `done`. Limit narzędzi, czas tury albo wyjątek → `error` bez `done`. Rozłączenie klienta anuluje turę.
5. Przycisk potwierdzenia: `POST /api/chat/confirmations { token }` z `Idempotency-Key` → `CouponActivation` → kupon `Active` z kodem.

Adapter demo (`DemoChatModel`) rozpoznaje słowa kluczowe bez polskich znaków i wielkości liter:

- „saldo/punkty” → `get_my_balance`;
- „oferty/nagrody” → `list_available_rewards`;
- „kupon” → `list_my_coupons`;
- „aktywuj/aktywacja” → lista kuponów `Purchased` (albo GUID z wiadomości) → `propose_coupon_activation` dla jedynego kuponu albo kuponu nazwanej oferty; przy kilku bez nazwy - pytanie, który. „Aktywne kupony” to pytanie o listę, nie prośba;
- prośby o naliczenie, wymianę, odbiór i administrację → odmowa bez narzędzi.

Pierwsza odpowiedź mówi, że to tryb demo.

## Ekran czatu

- Skrót „Asystent” na ekranie głównym klienta. Rozmowa żyje tylko na tym ekranie: bez cache i zapisu, wyjście z ekranu przerywa turę.
- `fetch` z `parseAs: "stream"`; `eventsource-parser` składa ramki podzielone na dowolne fragmenty, `TextDecoder` z `stream: true` - znaki wielobajtowe na granicy fragmentów.
- Tura ma stan `streaming`, `done`, `error` albo `cancelled`. Sukces to tylko zdarzenie `done`; koniec strumienia bez `done`/`error` → „Połączenie zostało przerwane - odpowiedź jest niepełna”. Częściowy tekst zostaje, oznaczony błędem.
- „Zatrzymaj” przerywa `fetch` (serwer anuluje turę). Jedna tura naraz; Enter wysyła, Shift+Enter - nowa linia; licznik 1000 znaków.
- Historia wysyłana z kolejnym pytaniem: tylko zakończone tury (`done`), najwyżej 20 wpisów po 2000 znaków.
- Narzędzia jako etykiety („Saldo punktów: sprawdzono”, „Propozycja aktywacji: odrzucono”); nieznana nazwa → „Nieznane narzędzie”.
- Karta potwierdzenia: oferta, warunki, termin ważności, „Aktywuj kupon” / „Nie teraz”. Zdarzenie `confirmation` przychodzi przed tekstem odpowiedzi, więc do `done` przyciski są nieaktywne („Czekam na koniec odpowiedzi…”). Karta z tury zakończonej błędem lub zatrzymanej nie ma przycisku.
- Po `done`/`error` można wysłać kolejne pytanie, zanim serwer zamknie połączenie. Pytanie odrzucone (trwa inna tura) zostaje w polu. Podpowiedź nie czyści wpisanego tekstu; Enter w trakcie składania znaku (IME) nie wysyła. Zła ramka zamyka połączenie (serwer kończy turę).
- Komunikaty błędów przed strumieniem: `429` limit, `401` sesja, `403` „tylko dla kont klientów”, `400` wiadomość albo historia (wg pola w `errors`). Po aktywacji odświeżane są kupony; kod nie jest pokazywany w czacie - link do „Moje kupony”.
- Utracona odpowiedź potwierdzenia → „Ponów aktywację” z tym samym kluczem. Klucz nie jest trwały: nowy klucz z tym samym tokenem jest bezpieczny (aktywny kupon zachowuje kod). `410` → „Propozycja wygasła”, `404` → „nieważna”, `409` → „wykorzystany”.

### Buforowanie przez proxy

Next (`next dev`/`next start`) kompresował odpowiedź z rewrite, gdy przeglądarka wysyła `Accept-Encoding: gzip`, i oddawał cały strumień dopiero na końcu. Dlatego `compress: false` w `next.config.ts`. API wysyła też `Cache-Control: no-store, no-transform`, ale odnowienie ciasteczka sesji podmienia ten nagłówek - nie można na nim polegać. Kompresja, jeśli potrzebna, należy do reverse proxy przed Next (P9), które musi przepuszczać `text/event-stream` bez buforowania.

## Dane

Moduł nie ma tabel. Potwierdzenie jest bezstanowym tokenem Data Protection (`proposalId`, `userId`, `couponId`, `expiresAt`). Rozmowy nie są zapisywane.

## Zabezpieczenia

- Tożsamość tylko z sesji. Żadne narzędzie nie ma parametru użytkownika. Kupony i propozycje czytają `MyCouponsEndpoints.Own`, więc cudzy kupon wygląda jak nieistniejący.
- Zamknięta lista narzędzi. Nieznana nazwa → `rejected`, a w zdarzeniu i logu nazwa `unknown`: tekst modelu nie wraca do klienta. Nieznane argumenty i złe typy → `rejected`, narzędzie nie działa.
- Żadne narzędzie nie zmienia danych. Aktywacja wymaga przycisku (`POST /api/chat/confirmations`), a token wiąże użytkownika, kupon i propozycję.
- Kody kuponów nie przechodzą przez czat: `list_my_coupons` ich nie zwraca, zdarzenia ich nie zawierają. Kod dostaje tylko odpowiedź potwierdzenia (`no-store` na grupie, więc także przy odtworzeniu).
- Token potwierdzenia trafia tylko do klienta: model dostaje wynik propozycji bez niego.
- Logi: `correlationId`, nazwa narzędzia, stan, czas - bez wiadomości, argumentów i wyników. Szczegóły awarii modelu nie trafiają do strumienia.
- Limity: długość wiadomości i historii, liczba narzędzi, czas tury i narzędzia, tury na użytkownika ([konfiguracja](/referencja/konfiguracja/)).

## Testy

| Plik | Sprawdza |
| --- | --- |
| `apps/web/e2e/chat.spec.ts` | saldo: odpowiedź, etykieta narzędzia, oznaczenie demo, Enter; **propozycja nic nie zmienia, przycisk aktywuje, kod nie pojawia się w czacie**; „Nie teraz” → kupon bez zmian; **utracona odpowiedź potwierdzenia → ponowienie z tym samym kluczem**; cudzy kupon → „odrzucono”, brak karty i nazwy; **strumień w 3-bajtowych kawałkach z przerwą przed odpowiedzią: narzędzie widoczne przed tekstem** (brak buforowania przez Next), polskie znaki całe; ucięty strumień → błąd, nie sukces; zdarzenie `error` po częściowym tekście → błąd; „Zatrzymaj” → `cancelled`, żądanie przerwane |
| `apps/api.tests/AssistantTests.cs` | strumień `meta → tool → delta → done`, nagłówki SSE (`no-store`, `X-Accel-Buffering`; `no-transform` na hoście bez odnawiania ciasteczka przy każdym żądaniu), `correlationId` w `meta` i nagłówku, oznaczenie demo; oferty i kupony bez kodów w strumieniu; **tylko własne kupony, cudzy kupon (także w „zignoruj instrukcje”) → `rejected`, brak propozycji, kupon bez zmian**; **propozycja niczego nie zmienia, potwierdzenie aktywuje, ten sam klucz → odtworzenie, nowy klucz → ten sam kod, kod nie pojawia się w czacie**; `no-store` na potwierdzeniu i jego odtworzeniu (host bez odnawiania ciasteczka, kontrola: portfel bez nagłówka); model nie dostaje tokenu potwierdzenia; „pokaż aktywne kupony” → lista bez propozycji; kilka kuponów: nazwana oferta → jej kupon, nieznana → pytanie, który; token innego użytkownika, zmieniony i pusty → `404`, po terminie (`TimeProvider`) → `410`, kupon bez zmian; aktywny i nieistniejący kupon → brak propozycji; prośby o naliczenie, odbiór, administrację → odmowa bez narzędzi, saldo bez zmian; walidacja (pusta, 1001 znaków, 21 wpisów historii, zła rola, pusty wpis; 1000 znaków przechodzi); `401`, `403` kasjer, `403 csrf.invalid`; limit tur na użytkownika (`429` + `Retry-After`, inne konto działa); modele skryptowane: **5. narzędzie → `error assistant.tool_limit` bez `done`**, nieznane narzędzie i złe argumenty → `rejected` bez danych i bez zmiany salda, wyjątek modelu → `error assistant.provider_error` bez szczegółów, wolna tura → `assistant.timeout` i heartbeat `: ping`, **rozłączenie klienta anuluje turę** |

Próby negatywne: kompresja Next włączona i bez `no-transform` → pada test podzielonego strumienia (sam `compress: false` wystarcza); token bez porównania użytkownika wywraca test wiązania tokenu; pętla bez limitu narzędzi wywraca test limitu (tura kończy się dopiero timeoutem); `no-store` na endpoincie zamiast na grupie (odtworzenie go pomija) wywraca test nagłówka odtworzenia.
