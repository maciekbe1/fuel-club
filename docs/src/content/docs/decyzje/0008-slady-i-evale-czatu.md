---
title: "0008 - Ślady i evale czatu"
description: Metadane tury bez treści w bazie i jako span OpenTelemetry, pseudonim użytkownika, opcjonalny Phoenix i wersjonowane evale.
sidebar:
  order: 8
---

## Status

Decyzja przyjęta (2026-11-22), zrealizowana w P7a. Metryki, reguły anomalii, kolejka przeglądu, dostęp admina i retencja: P7b. Ocena przez judge: P7c.

## Problem

Trzeba wiedzieć, co asystent zrobił w każdej turze: które narzędzia, z jakim wynikiem, jak długo i czym tura się skończyła. Trzeba też móc pokazać te dane w narzędziu do śladów. Jednocześnie treść rozmowy, kody kuponów i kart, cookies i tożsamość klienta nie mogą trafić do śladu ani do zewnętrznego odbiorcy. Zachowanie asystenta trzeba też sprawdzać powtarzalnie: na stałych przypadkach, a nie przez czytanie odpowiedzi.

## Rozwiązanie

### Co zawiera ślad

- Jedna tura to jeden wiersz `assistant_turns` i span `chat.turn` z potomnymi `chat.tool`.
- **Domyślnie i na razie wyłącznie metadane**:
  - `correlationId`, pseudonim użytkownika;
  - dostawca, model, wersja promptu, tryb demo;
  - czas do pierwszego zdarzenia i całkowity, wynik (`Done`/`Error`/`Cancelled`) i kod błędu;
  - narzędzia (nazwa z zamkniętej listy albo `unknown`, stan, czas); narzędzie, które przekroczyło czas albo rzuciło wyjątek, zostaje w śladzie jako `timeout`/`error`, a jego span ma status błędu;
  - tokeny tylko, gdy zgłosi je dostawca. `null` oznacza „nieznane”, nie zero. Jedno wywołanie modelu bez danych daje `null` dla całej tury, a nie częściową sumę.
- Bez pytania, odpowiedzi, argumentów i wyników narzędzi. Zapis treści wymaga odrębnej decyzji o zakresie i retencji (SPEC §3).
- „Redakcja” to brak treści już na wejściu, a nie maskowanie: każdy atrybut pochodzi z listy `AssistantTelemetry.Attributes`, a wartości tworzy serwer. Wyjątków nie dołącza się do spanu.

### Pseudonim

- HMAC-SHA256 identyfikatora użytkownika z kluczem `Assistant:PseudonymKey` (sekret, base64 ≥ 32 bajty), pierwsze 16 bajtów hex.
- Bez klucza w `Development`: losowy klucz na czas procesu, czyli pseudonimy zmieniają się po restarcie; ostrzeżenie w logu. Poza `Development` brak klucza zatrzymuje start API.

### Zapis i eksport

- Wiersz zapisuje się po wysłaniu ostatniego zdarzenia, także gdy klient odszedł. Zapis idzie we własnym zakresie DI (nie zatwierdza niczego, co narzędzie zostawiło w kontekście żądania) i z limitem `Assistant:TraceSaveTimeout` (2 s), bo strumień kończy się dopiero po nim. Błąd lub przekroczenie czasu zapisu trafia tylko do logu (nazwa typu wyjątku).
- Wynik `Cancelled` ma pierwszeństwo: gdy klient odszedł, późniejszy błąd zapisu do zamkniętego połączenia nie zostaje zapisany jako `provider_error`.
- OpenTelemetry jest włączane przez `Telemetry:OtlpEndpoint` (OTLP/HTTP protobuf).
  - Eksportowane jest tylko źródło `FuelClub.Assistant`, bez instrumentacji HTTP: URL-e, nagłówki i treści żądań nie wychodzą.
  - Eksport idzie w tle (batch), więc niedostępny odbiorca nie spowalnia czatu.
  - Nagłówki eksportera (klucz API) tylko z `OTEL_EXPORTER_OTLP_HEADERS`.
- Span tury jest **korzeniem śladu** z linkiem (`ActivityLink`) do aktywności żądania HTTP. Aktywność żądania z ASP.NET Core nie jest próbkowana, a domyślny sampler (`ParentBased`) odrzucał przez to całą turę. Wyszło to przy pierwszym teście eksportu. Logi z tury mają `TraceId` tury; link wskazuje ślad żądania.
- Lokalny odbiorca: Phoenix w `compose.yml`, profil `observability`.
  - Obraz przypięty digestem, bez wolumenu.
  - Port tylko `127.0.0.1`, auth wyłączone - to lokalna przeglądarka śladów.
  - Langfuse byłby alternatywą przez ten sam OTLP, nie drugą integracją.

### Evale

- `apps/api.tests/Evals/assistant-cases.v1.json` (wersja `assistant-evals-v1`): przypadki z kategorią, wiadomością, opcjonalnym modelem skryptowym i oczekiwaniami.
- Runner `AssistantEvals` uruchamia każdy przypadek na prawdziwym API i bazie. Sprawdza:
  - narzędzia (nazwa i stan);
  - koniec tury: dokładnie jedno `done` albo `error`;
  - propozycję potwierdzenia;
  - brak treści zakazanych (cudza oferta, kod kuponu);
  - **brak skutków**: saldo, liczbę wpisów historii i stany wszystkich kuponów;
  - zgodność wiersza śladu ze strumieniem.
- Osobny test wymaga, żeby zestaw pokrywał wszystkie kategorie z SPEC §3, a identyfikatory były unikalne.
- Nowa wersja zestawu to nowy plik (`v2`) - wyniki są porównywalne w obrębie wersji. Potwierdzony problem z rzeczywistej rozmowy wraca jako syntetyczny przypadek, nie jako zapis rozmowy.

## Alternatywy

- **Maskowanie treści regexem przed eksportem**: nie gwarantuje anonimizacji (SPEC). Prościej i pewniej nie zbierać treści.
- **Tylko OpenTelemetry, bez tabeli**: metryki, anomalie i kolejka admina (P7b) potrzebują danych bez zewnętrznego odbiorcy, a Phoenix jest opcjonalny.
- **Phoenix z auth**: eksport wymagałby klucza API w środowisku. Dla lokalnej przeglądarki na `127.0.0.1` to zbędne. Na serwerze nie uruchamiamy Phoenix bez zabezpieczenia.
- **Sampler `AlwaysOn` zamiast korzenia**: span tury wisiałby pod nieeksportowanym rodzicem (osierocony ślad w Phoenix).

## Konsekwencje

- `assistant_turns` rośnie z każdą turą; retencję, metryki i dostęp admina opisuje [ADR 0009](/decyzje/0009-zdarzenia-anomalie-i-retencja/).
- Phoenix pokazuje brak tokenów jako `0` - to jego prezentacja. W bazie i w spanie wartość jest nieobecna.
- Pseudonim nie jest anonimizacją: z kluczem i listą identyfikatorów da się go odwrócić. Klucz to sekret.
