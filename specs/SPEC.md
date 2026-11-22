# Fuel Club - specyfikacja pozostałego MVP

Stan odniesienia: 2026-11-22, HEAD c4a7883, czysty katalog roboczy. Źródło: PLAN-MVP.md i aktualne pliki repo. To inwentaryzacja, nie certyfikacja testów ani pełny review commitu.

## Stan

Gotowe w kodzie: Identity i role, karta i QR, kasa z tankowaniem, saldo i historia, infrastruktura idempotencji, API ofert, wymiana punktów, własne kupony, aktywacja, podgląd i odbiór razem z zakupem. Frontend ma konto, kartę, saldo, historię i podstawową kasę.

Pozostałe wymagania obowiązkowe opisano poniżej. Gemini, RAG, voice, Slack, Vertex i Dialogflow nie blokują ukończenia lokalnego MVP.

## 1. Interfejs ofert i kuponów

- Katalog: nazwa, opis, koszt, stawka zł/l i limit litrów. Stany ładowania, pustej listy, błędu i ponowienia.
- Wymiana: jawne potwierdzenie kosztu i warunków; wysłanie wersji pokazanej oferty. Zmienione warunki wymagają ponownego potwierdzenia. Utrata odpowiedzi zachowuje klucz i dane, ponowienie nie kupuje drugiego kuponu. Po sukcesie odśwież saldo i kupony.
- Moje kupony: Purchased, Active, Redeemed; migawka warunków, daty. Aktywacja nie jest odbiorem. Po mutacji pobierz aktualny stan przez GET. QR i kopiowanie kodu tylko dla aktywnego kuponu; zużyty nie jest prezentowany jako możliwy do realizacji.
- Kasa: osobne wejścia karty i kuponu, skan tylko wypełnia pole. Podgląd bez zużycia. Podgląd ma limit prób na kasjera (30/min), więc nie odpytuje API przy każdym znaku: warunki kuponu po wpisaniu/zeskanowaniu pełnego kodu, wyliczenie rabatu na jawne „Przelicz”. Zmiana karty lub kuponu usuwa podgląd; zmiana kwoty lub litrów oznacza wyliczenie jako nieaktualne (bez nowego zapytania). Spóźniona odpowiedź nie nadpisuje nowszych danych. Zapis przesyła kod, nie obliczony rabat. Wynik pokazuje discount, paidAmount, pointsAwarded. Zachowaj obecny mechanizm nieznanego wyniku i ponowienia.
- Historia: pokaż brutto, rabat i zapłacono z istniejącego kontraktu (`grossAmount`, `discount`, `paidAmount`); wpisy wymiany są już odróżnione ikoną według typu. Zakupy za zero punktów nadal nie należą do historii punktów.
- Admin: strażnik `app/(app)/admin/layout.tsx` (konto bez roli Admin → przekierowanie; decyduje backend). Lista, tworzenie, edycja i włączanie/wyłączanie. Brak DELETE. POST z idempotencją; PUT z wersją. Konflikt lub nieznany wynik nie może automatycznie nadpisać danych; zachowaj formularz, pozwól odczytać aktualną wersję i świadomie ponowić.
- Wspólne: polski UI, telefon i desktop, dostępna klawiatura i komunikaty, typy z OpenAPI, istniejące warstwy services/hooks/components. Cache danych użytkownika czyszczony przy zmianie konta.

## 2. Lokalny czat i bezpieczne narzędzia

- Moduł Assistant; domyślny deterministyczny adapter jawnie oznaczony jako demo. Brak sekretów nie blokuje uruchomienia.
- POST /api/chat, User, CSRF, strumień SSE przez fetch. Zdarzenia meta, tool, delta, confirmation, done, error. Określ i wygeneruj typy payloadów; parser toleruje podział ramki na wiele fragmentów sieciowych.
- Limit rozmiaru wejścia, czasu rozmowy/wywołania i liczby narzędzi; anulowanie przy rozłączeniu, heartbeat i brak buforowania proxy. Błąd nie emituje pozornego sukcesu.
- Narzędzia: get_my_balance, list_available_rewards, list_my_coupons, propose_coupon_activation. Tożsamość wyłącznie z sesji; model nie wybiera właściciela. Argumenty walidowane, narzędzia z zamkniętej listy.
- Propozycja aktywacji nie mutuje danych. Potwierdzenie przyciskiem związane z zalogowanym użytkownikiem, konkretnym kuponem i propozycją; tekst modelu nie jest zgodą. Wykonanie korzysta ze wspólnej logiki aktywacji i Idempotency-Key. Żadnego naliczania punktów, wymiany punktów, administracji ani odbioru przez agenta.
- Przed implementacją zapisz w ADR dokładny kontrakt potwierdzenia, jego czas ważności i sposób obsługi ponowienia; decyzję podejmuje agent zgodnie z tą specyfikacją, bez osobnego zatwierdzania. Oddziel logikę przypadku użycia od handlera HTTP tylko w zakresie potrzebnym do współdzielenia.

## 3. Evals i obserwowalność

- Wersjonowane deterministyczne przypadki: saldo, oferty, brak punktów, własny/cudzy kupon, niedozwolone naliczenie, prompt injection, błędne argumenty, brak zgody, awaria dostawcy i przekroczenie limitu.
- Asercje dotyczą wybranych narzędzi, właściciela danych, skutków w bazie i braku mutacji bez potwierdzenia, nie tylko tekstu odpowiedzi.
- Strukturalne ślady: correlationId, pseudonim użytkownika, narzędzie, wynik, czas, tryb dostawcy. Bez kodów, cookies, haseł i surowych rozmów domyślnie.
- Konfigurowalne reguły anomalii: odmowy dostępu, konflikty kluczy, powtarzane odbiory, limity narzędzi i błędy AI. Panel/endpoint admina pokazuje metryki, nie sekrety ani cudze rozmowy.
- Zweryfikuj istniejące limity logowania, realizacji i czatu; uzupełnij brakujące. Podgląd ma już własny limit. Ustal retencję nowych śladów.

### Monitoring rzeczywistych rozmów i pętla poprawy

- Każda tura ma trace z correlationId, wersją promptu/modelu, czasem, błędami i wywołaniami narzędzi. Tokeny i koszt zapisuj tylko, gdy dostawca udostępnia dane; brak danych nie oznacza zera.
- Domyślnie zapisuj metadane. Pełne pytania, odpowiedzi, argumenty i wyniki narzędzi są opcjonalne, po odrębnej decyzji o zakresie i retencji. Redakcja następuje przed zapisem lub eksportem; kodów kart/kuponów, cookies i sekretów nigdy nie eksportuj. Maskowanie regexem nie gwarantuje anonimizacji.
- Telemetria przez OpenTelemetry; opcjonalny lokalny Phoenix jako pierwszy demonstracyjny odbiorca, Langfuse jako alternatywa, nie druga równoległa integracja. Niedostępność odbiorcy nie blokuje rozmowy. Nie wymaga to osobnego agenta Node ani kopii narzędzi backendu.
- Na całym ruchu: liczba żądań, błędy, p95 czasu do pierwszego zdarzenia i zakończenia, wyniki narzędzi, odmowy dostępu oraz naruszenia limitów. Feedback klienta opcjonalny. Nie raportuj containment ani przekazań człowiekowi, dopóki produkt nie ma zdefiniowanego zakończenia sprawy i procesu przekazania.
- Ocena próbek działa poza ścieżką odpowiedzi. Konfigurowalny udział, np. 2%, limit kosztu i liczby ocen; obok próbki losowej osobna próbka błędów. Raportuj je oddzielnie, z liczebnością i wersją judge’a. Awaria judge’a nie jest automatycznie błędem agenta.
- Współdziel checki lokalne i produkcyjne, gdy mają potrzebne dane. Rzeczywiste rozmowy zwykle nie mają odpowiedzi wzorcowej: oceniaj zgodność z wynikami narzędzi i regułami. Nigdy nie odtwarzaj mutujących narzędzi na produkcji w ramach evala.
- Podejrzane trace’y trafiają do kolejki admina: negatywny feedback, powtarzane błędy, naruszenia reguł, niska ocena. Porzucenie rozmowy jest sygnałem do analizy, nie dowodem porażki.
- Alerty mają konfigurowalne okno, minimalną próbkę i ograniczenie powtórzeń. Naruszenia izolacji lub mutacje bez zgody są krytyczne niezależnie od średniej ocen. Slack pozostaje opcjonalny, wyłączony bez konfiguracji.
- Potwierdzony problem staje się zanonimizowanym lub syntetycznym przypadkiem regresyjnym w `apps/api.tests/Evals/` (zestaw `assistant-evals-v1`), nie surową rozmową w Git. Oceny człowieka służą kalibracji judge’a. Zmiany promptu/modelu przechodzą evals i review; canary dopiero przy gotowym wdrożeniu, metrykach i możliwości rollbacku.
- Dostęp do trace’ów ograniczony rolą; automatyczne usuwanie według jawnej retencji. Te mechanizmy nie są deklaracją pełnej zgodności prawnej.
- Testy: redakcja przed eksportem, brak sekretów w błędach eksportera, izolacja użytkowników, awaria odbiorcy, sampling i budżet, progi alertów, usuwanie starych danych oraz brak skutków domenowych oceny.

## 4. Dostarczenie demo

- Pełny E2E: klient i kasjer w osobnych kontekstach, zakup, punkty, wymiana, aktywacja, podgląd, jeden odbiór, historia. Dodatkowo aktywacja przez potwierdzenie czatu.
- Utrata odpowiedzi oraz drugi odbiór nie podwajają efektu. Drugie konto nie dostaje pierwszego konta w UI, HTTP ani narzędziach.
- README: świeży klon, konfiguracja, migracje, konta demo, start i wszystkie komendy kontroli. Dokumentacja odróżnia stan gotowy od planowanego.
- CI dla API na PostgreSQL, typecheck/lint, E2E, evals i build docs. Nie używa bazy dev ani kluczy chmurowych.
- Przygotuj obrazy web/API i instrukcję hostingu Railway z prywatną bazą/API, HTTPS przez web, trwałymi kluczami Data Protection, migracjami, backupem/odtworzeniem oraz testem SSE i kamery na telefonie. Wdrożenie wymaga dostępnego konta i konfiguracji; nie twórz płatnych zasobów w ciemno. Aktualne ustawienia i ceny sprawdź przy wdrożeniu.

## Opcjonalne po lokalnym MVP

Adapter Gemini po stronie backendu za tym samym kontraktem; osobne evals dostawcy tylko z kluczem. Projekt integracji Vertex/Dialogflow/Cloud Run/Slack/RAG/voice pozostaje dokumentacją, bez wymogu implementowania wszystkiego.

## Korekty starego planu

Usuń nieaktualne oznaczenia Cashier/LoyaltyCard/CashierStation jako planowanych. Ujednolić RewardDefinition/UserCoupon z rzeczywistymi nazwami. Regułę punktów opisz jako floor(PaidAmount), z dozwolonym zerem po rabacie; limit 5000 zł dotyczy brutto. Historyczne polecenia zostaw wyraźnie historyczne. Nie przenoś dawnych liczników testów jako nowych wyników.
