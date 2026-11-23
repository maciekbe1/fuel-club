---
title: Assistant (czat)
description: Czat z narzędziami nad danymi zalogowanego klienta, adapter demo i potwierdzanie aktywacji kuponu.
sidebar:
  order: 4
---

:::caution[W trakcie]
Zrealizowane: `POST /api/chat` (SSE), osiem narzędzi (w tym `search_program_rules` - regulamin, [ADR 0016](/decyzje/0016-regulamin-w-czacie/), i `propose_handoff`, [ADR 0012](/decyzje/0012-przekazanie-do-konsultanta/)), deterministyczny adapter demo, potwierdzenie aktywacji, ekran `/czat`. Zrealizowane w P7a: ślady (metadane) i wersjonowane evale. Metryki, anomalie, kolejka admina i retencja: [Observability](/moduly/observability/) (P7b). Adapter Gemini: [ADR 0011](/decyzje/0011-adapter-gemini/) (`Model/GeminiChatModel.cs`, testy `GeminiTests`). Planowane: ocena przez judge (P7c).
:::

## Co robi

Klient pyta o saldo, oferty i swoje kupony, może też poprosić o aktywację kuponu. Asystent odpowiada strumieniem zdarzeń. Aktywację tylko **proponuje**: zmiana następuje dopiero po naciśnięciu przycisku potwierdzenia. Kontrakt, czas ważności potwierdzenia i ponowienia: [ADR 0007](/decyzje/0007-czat-i-potwierdzenia/).

Gdy klient ma otwarte zgłoszenie do konsultanta, czat i potwierdzenia odpowiadają `409 assistant.handoff_active` - potwierdzenie sprawdza to pod blokadą klienta ([Support](/moduly/support/)).

## Gdzie jest kod

| Co | Plik |
| --- | --- |
| Rejestracja, trasy, limit tur | `apps/api/Modules/Assistant/AssistantModule.cs` |
| Ustawienia i walidacja | `AssistantOptions.cs` |
| Kody błędów | `AssistantErrorCodes.cs` |
| Endpointy (czat, potwierdzenie), walidacja wejścia, heartbeat | `Chat/ChatEndpoints.cs` |
| Pętla tury: limity, narzędzia, zdarzenia | `Chat/ChatService.cs` |
| Rozmowy: start, stan tury, zakończenie, powiązanie ze zgłoszeniem, retencja | `Conversations/Conversations.cs`, `Conversations/ConversationEndpoints.cs`, `Conversations/AssistantConversation.cs` |
| Ramki SSE | `Chat/SseWriter.cs` |
| Payloady zdarzeń (`ChatStreamContract` w OpenAPI) | `Chat/ChatContracts.cs` |
| Narzędzia (zamknięta lista) | `Tools/AssistantTools.cs` |
| Kontrakt modelu, adapter demo | `Model/IChatModel.cs`, `Model/DemoChatModel.cs` |
| Token potwierdzenia | `Confirmation/ConfirmationTokens.cs` |
| Ślady tury (wiersz + span), pseudonim, lista atrybutów | `Tracing/TurnRecorder.cs`, `Tracing/TracePseudonyms.cs`, `Tracing/AssistantTelemetry.cs`, `Tracing/AssistantTurn.cs` |
| Eksport OpenTelemetry | `apps/api/Infrastructure/Telemetry.cs` |
| Evale | `apps/api.tests/Evals/assistant-cases.v2.json`, `AssistantEvals.cs` |
| Wspólna aktywacja kuponu | `Modules/Rewards/Coupons/CouponActivation.cs` (używa jej też `POST /api/me/coupons/{id}/activate`) |
| Ekran `/czat` | `apps/web/app/(app)/czat/page.tsx`, `apps/web/features/assistant/components/ChatScreen.tsx`, `ConfirmationCard.tsx` |
| Strumień w przeglądarce (fetch + parser SSE) | `apps/web/features/assistant/services.ts` |
| Stan rozmowy, potwierdzenie | `apps/web/features/assistant/hooks.ts` (`useChat`, `useConfirmActivation`) |
| Odczyty z innych modułów | `WalletEndpoints.BalanceAsync`, `CatalogEndpoint.ActiveOffers`, `MyCouponsEndpoints.Own` |

## Przepływ

0. Rozmowa: `POST /api/chat/conversations` z `Idempotency-Key`, zanim ruszy model ([ADR 0017](/decyzje/0017-ruch-syntetyczny-i-rozmowy/)). Ponowienie z tym samym kluczem (utracona odpowiedź) zwraca tę samą rozmowę.
1. `POST /api/chat`: walidacja (`400`, także brak `conversationId`) i limit (`429`) przed strumieniem; potem jednym warunkowym `UPDATE` właściciel, stan i bezczynność rozmowy (`404 assistant.conversation_not_found`, `409 assistant.conversation_ended`) - model nie rusza dla cudzej, zakończonej ani bezczynnej rozmowy.
2. Nagłówki SSE, zdarzenie `meta` (`correlationId` także w nagłówku `X-Correlation-Id`, oraz `conversationId`), heartbeat co `HeartbeatInterval`.
3. Pętla: model zwraca `Tool` albo `Reply`. Narzędzie działa z `ToolContext.UserId` z sesji i z limitem `ToolTimeout`. Wynik idzie do modelu, a klient dostaje zdarzenie `tool` (nazwa i stan). Propozycja aktywacji daje dodatkowo `confirmation`, propozycja rozmowy z konsultantem (`propose_handoff`, bez argumentów, niczego nie zmienia) - `handoff`: klient pokazuje przycisk, zgłoszenie tworzy dopiero jego naciśnięcie. Ekran `/czat`: komponenty AI Elements, odpowiedź jako markdown przez `streamdown` ([ADR 0015](/decyzje/0015-komponenty-czatu-ai-elements/)). Lista zdarzeń, które czyta klient (`features/assistant/services.ts`), jest typowana kluczami `ChatStreamContract` - nowe zdarzenie w API bez obsługi w web nie przejdzie `typecheck` (wcześniej `handoff` był po cichu pomijany). Gemini dostaje deklarację tego narzędzia (od `gemini-v4`), bez nowych danych klienta. Pytania o zasady programu: `search_program_rules` (prompt `gemini-v7`, model demo `demo-rules-v8`), szczegóły w [Knowledge](/moduly/knowledge/).
4. `Reply` → `delta` + `done`. Limit narzędzi, czas tury albo wyjątek → `error` bez `done`. Rozłączenie klienta anuluje turę.
6. `POST /api/chat/conversations/{id}/end`: idempotentne; bezczynna rozmowa kończy się jako `Idle` z czasem upływu limitu. Zgłoszenie z `conversationId` wiąże rozmowę raz i kończy ją jako `Handoff`.
5. Przycisk potwierdzenia: `POST /api/chat/confirmations { token }` z `Idempotency-Key` → `CouponActivation` → kupon `Active` z kodem.

Adapter demo (`DemoChatModel`) rozpoznaje słowa kluczowe bez polskich znaków i wielkości liter:

- „saldo/punkty” → `get_my_balance`;
- „oferty/nagrody” → `list_available_rewards`;
- „kupon” → `list_my_coupons`;
- „który kupon / opłaca / wybrać / porównaj” + litry (np. „40 l”, opcjonalnie „250 zł”) → `compare_coupons`; bez litrów - pytanie o litry;
- „dostałem / naliczono / przyznano … punktów”, „punkty za tankowanie” → `explain_purchase_points` (po regule odmowy: „nalicz”, „dopisz” w tej samej wiadomości dają odmowę); kwota zakupu to liczba przy „zł”, nie cena za litr („po 6,20 zł/l”);
- „konsultant/człowiek/pracownik/obsługa klienta” → `propose_handoff`, potem odpowiedź z nazwą przycisku;
- „aktywuj/aktywacja” → GUID z wiadomości → `propose_coupon_activation` dla tego id (narzędzie sprawdza właściciela); inaczej lista kuponów `Purchased` i wybór (`demo-rules-v8`):
  - klient wskazał nazwę (słowa wiadomości bez słów aktywacji i wypełniaczy, np. „mój”, „proszę”, „czy mogę”) - propozycja tylko dla kuponu, którego oferta ma wszystkie te słowa; kilka kuponów tej samej oferty to ten sam wybór;
  - brak dopasowania - „Nie masz takiego kuponu do aktywacji” i lista własnych kuponów do wyboru, bez podstawiania innego (błąd `demo-rules-v7` znaleziony przez judge'a w `demo-1`);
  - kilka pasujących ofert - pytanie, który, z listą tylko pasujących;
  - ogólne „aktywuj mój kupon” - jedyny kupon może być zaproponowany, przy kilku pytanie, który. „Aktywne kupony” to pytanie o listę, nie prośba;
- prośby o naliczenie, wymianę, odbiór i administrację → odmowa bez narzędzi.

Pierwsza odpowiedź mówi, że to tryb demo.

## Ekran czatu

- Skrót „Asystent” na ekranie głównym klienta. Tury żyją tylko na tym ekranie: bez cache i zapisu, wyjście z ekranu przerywa turę i kończy rozmowę na serwerze (`keepalive`).
- Rozmowa startuje przed pierwszym pytaniem; klucz startu trzymany do skutku. `409 conversation_ended` albo `404 conversation_not_found` (np. pseudonimy po restarcie API bez `PseudonymKey`) → nowa rozmowa i jednokrotne ponowienie pytania (model jeszcze nie ruszył). „Porozmawiaj z konsultantem” przekazuje `conversationId` do zgłoszenia.
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

Potwierdzenie jest bezstanowym tokenem Data Protection (`proposalId`, `userId`, `couponId`, `expiresAt`). Treść rozmów nie jest zapisywana.

`assistant_conversations` - metadane rozmowy ([referencja](/referencja/baza-danych/)): właściciel (pseudonim), start, ostatnia aktywność, zakończenie i powód (`Client`, `Idle`, `Handoff` - żaden nie oznacza rozwiązania sprawy), liczba tur, powiązane zgłoszenie (raz), `Synthetic`/`RunId` z konfiguracji instancji symulacji (`Simulation`), nigdy z żądania. Retencja jak tury.

`assistant_turns` - jeden wiersz na turę, tylko metadane ([referencja](/referencja/baza-danych/), [ADR 0008](/decyzje/0008-slady-i-evale-czatu/)):

- `correlationId` (ten z `meta`), pseudonim HMAC, dostawca, model, wersja promptu, demo;
- czasy (`FirstEventMs` - pierwsze zdarzenie po `meta`, `TotalMs`);
- wynik i kod błędu, narzędzia (nazwa, stan, czas);
- tokeny tylko od dostawcy;
- `ConversationId`, `HandoffProposed`, `Synthetic`/`RunId` (z konfiguracji, jak w rozmowie).

Klient, który odszedł, zostawia turę `Cancelled`. Gdy tura skończyła się błędem, zanim klient odszedł, zostaje `Error`.

Retencja, metryki i kolejka przeglądu: [Observability](/moduly/observability/).

## Zabezpieczenia

- Tożsamość tylko z sesji. Żadne narzędzie nie ma parametru użytkownika. Kupony i propozycje czytają `MyCouponsEndpoints.Own`, więc cudzy kupon wygląda jak nieistniejący.
- Zamknięta lista narzędzi. Nieznana nazwa → `rejected`, a w zdarzeniu i logu nazwa `unknown`: tekst modelu nie wraca do klienta. Nieznane argumenty i złe typy → `rejected`, narzędzie nie działa.
- Żadne narzędzie nie zmienia danych. Aktywacja wymaga przycisku (`POST /api/chat/confirmations`), a token wiąże użytkownika, kupon i propozycję.
- Kody kuponów nie przechodzą przez czat: `list_my_coupons` ich nie zwraca, zdarzenia ich nie zawierają. Kod dostaje tylko odpowiedź potwierdzenia (`no-store` na grupie, więc także przy odtworzeniu).
- Token potwierdzenia trafia tylko do klienta: model dostaje wynik propozycji bez niego.
- Logi i ślady: `correlationId`, nazwa narzędzia, stan, czas - bez wiadomości, argumentów i wyników. Szczegóły awarii modelu nie trafiają do strumienia ani do śladu. Atrybuty spanów tylko z listy `AssistantTelemetry.Attributes`; eksport (opcjonalny) tylko źródła asystenta.
- Limity: długość wiadomości i historii, liczba narzędzi, czas tury i narzędzia, tury na użytkownika ([konfiguracja](/referencja/konfiguracja/)).

## Testy

| Plik | Sprawdza |
| --- | --- |
| `apps/api.tests/ConversationTests.cs` | ponowiony start z tym samym kluczem → ta sama rozmowa, jedna w bazie; tury liczone na rozmowie i niosą jej id; tura bez rozmowy → `400`; cudza/nieznana → `404`, model nie rusza; zakończenie idempotentne, zakończona → `409`; bezczynna → `Idle` z czasem upływu limitu; zgłoszenie wiąże rozmowę raz (ponowienie i drugie zgłoszenie nie zmieniają), kończy jako `Handoff`, tura z `HandoffProposed`; cudza rozmowa w zgłoszeniu → `400`; `Synthetic`/`RunId` tylko z konfiguracji, pola żądania ignorowane |
| `apps/web/e2e/chat.spec.ts` | saldo: odpowiedź, etykieta narzędzia, oznaczenie demo, Enter; **propozycja nic nie zmienia, przycisk aktywuje, kod nie pojawia się w czacie**; „Nie teraz” → kupon bez zmian; **utracona odpowiedź potwierdzenia → ponowienie z tym samym kluczem**; cudzy kupon → „odrzucono”, brak karty i nazwy; **strumień w 3-bajtowych kawałkach z przerwą przed odpowiedzią: narzędzie widoczne przed tekstem** (brak buforowania przez Next), polskie znaki całe; ucięty strumień → błąd, nie sukces; zdarzenie `error` po częściowym tekście → błąd; „Zatrzymaj” → `cancelled`, żądanie przerwane; porównanie kuponów i rachunek punktów z narzędzi serwera; **utracona odpowiedź startu rozmowy → ponowienie tym samym kluczem, jedna rozmowa**; zakończona rozmowa → nowa i pytanie ponowione raz |
| `apps/api.tests/Evals/AssistantEvals.cs` | 21 wersjonowanych przypadków (`assistant-evals-v2`): regulamin (6, kategorie `regulamin-*`), saldo, oferty, porównanie kuponów, wyjaśnienie punktów bez zakupów, brak punktów, własny kupon (propozycja, lista bez kodów), cudzy kupon, naliczenie, dwa prompt injection, błędne argumenty (nieznane narzędzie, `userId`, zły status, dodatkowy argument przy cudzym kuponie), tekst „potwierdzam” ≠ zgoda, awaria dostawcy, limit narzędzi; każdy: narzędzia, jeden koniec tury, brak zmian salda/historii/kuponów, wiersz śladu zgodny ze strumieniem i bez treści; pokrycie wszystkich kategorii SPEC §3 |
| `apps/api.tests/TurnRecorderTests.cs` | narzędzie z przekroczonym czasem / wyjątkiem zostaje w śladzie (`timeout`/`error`, span z błędem); brak danych o tokenach w jednym wywołaniu → `null`; span tury jako korzeń z linkiem do żądania, przywrócenie `Activity.Current`; klucz pseudonimu wymagany poza `Development`, min. 32 bajty |
| `apps/api.tests/AssistantTracingTests.cs` | spany tylko z atrybutów z listy, bez treści wiadomości, kodu kuponu, e-maila i id użytkownika, narzędzia jako dzieci tury, brak tokenów = brak atrybutu; **eksport do lokalnego kolektora OTLP bez tych danych**, niedostępny kolektor nie blokuje czatu; pseudonim = HMAC z kluczem, stały dla użytkownika, różny dla innych; wiersz z metadanymi i czasami; tokeny sumowane; odejście klienta → `Cancelled` |
| `apps/api.tests/AssistantTests.cs` | strumień `meta → tool → delta → done`, nagłówki SSE (`no-store`, `X-Accel-Buffering`; `no-transform` na hoście bez odnawiania ciasteczka przy każdym żądaniu), `correlationId` w `meta` i nagłówku, oznaczenie demo; oferty i kupony bez kodów w strumieniu; **tylko własne kupony, cudzy kupon (także w „zignoruj instrukcje”) → `rejected`, brak propozycji, kupon bez zmian**; **propozycja niczego nie zmienia, potwierdzenie aktywuje, ten sam klucz → odtworzenie, nowy klucz → ten sam kod, kod nie pojawia się w czacie**; `no-store` na potwierdzeniu i jego odtworzeniu (host bez odnawiania ciasteczka, kontrola: portfel bez nagłówka); model nie dostaje tokenu potwierdzenia; „pokaż aktywne kupony” → lista bez propozycji; kilka kuponów: nazwana oferta → jej kupon, nieznana → pytanie, który; token innego użytkownika, zmieniony i pusty → `404`, po terminie (`TimeProvider`) → `410`, kupon bez zmian; aktywny i nieistniejący kupon → brak propozycji; prośby o naliczenie, odbiór, administrację → odmowa bez narzędzi, saldo bez zmian; walidacja (pusta, 1001 znaków, 21 wpisów historii, zła rola, pusty wpis; 1000 znaków przechodzi); `401`, `403` kasjer, `403 csrf.invalid`; limit tur na użytkownika (`429` + `Retry-After`, inne konto działa); modele skryptowane: **5. narzędzie → `error assistant.tool_limit` bez `done`**, nieznane narzędzie i złe argumenty → `rejected` bez danych i bez zmiany salda, wyjątek modelu → `error assistant.provider_error` bez szczegółów, wolna tura → `assistant.timeout` i heartbeat `: ping`, **rozłączenie klienta anuluje turę**; `compare_coupons`: własne kupony osobno od ofert, cudze pominięte, rabat ograniczony kwotą, szacunek bez kwoty, złe argumenty odrzucone; `pointsMissing` w ofertach; `explain_purchase_points`: ostatni własny zakup z rozstrzyganiem remisu, zakup za 0 pkt, brak zakupów; demo porównuje i wyjaśnia; nazwany kupon, którego klient nie ma, nie jest zastępowany jedynym („Nie masz takiego kuponu” i lista), ogólna prośba proponuje jedyny; nazwa pasująca do kilku ofert - pytanie z listą tylko pasujących |

Próby negatywne: zapis narzędzia tylko po sukcesie → pada test narzędzia z przekroczonym czasem; tekst wiadomości jako atrybut spanu → padają testy spanów i eksportu; span tury pod aktywnością HTTP (bez korzenia) → nic nie jest eksportowane, pada test eksportu; kompresja Next włączona i bez `no-transform` → pada test podzielonego strumienia (sam `compress: false` wystarcza); token bez porównania użytkownika wywraca test wiązania tokenu; pętla bez limitu narzędzi wywraca test limitu (tura kończy się dopiero timeoutem); `no-store` na endpoincie zamiast na grupie (odtworzenie go pomija) wywraca test nagłówka odtworzenia.
