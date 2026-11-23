---
title: "0017 - Ruch syntetyczny, rozmowy i dane dla judge'a"
description: Jak oznaczamy ruch symulatora, czym jest rozmowa i jak liczymy przekazania, oraz jakie dane wolno dać judge'owi.
sidebar:
  order: 17
---

## Status

Decyzja przyjęta (2026-11-23), P16. Wykonane po kolei: symulator z manifestem, dane demo, metryki P15b (P16c), judge offline (P16d). Feedback (kciuki) i k6 później.

## Problem

Panel obserwowalności liczy p95, tokeny i anomalie, ale ma tylko kilka tur z ręcznych testów. Potrzebny jest ruch syntetyczny, który:

- nie miesza się z ruchem prawdziwych użytkowników;
- daje sensowne metryki rozmów, a nie tylko pojedynczych tur;
- może posłużyć do oceny judge'em bez zapisywania treści rozmów w bazie (ADR 0008).

## Rozwiązanie

### 1. Identyfikacja symulacji

- Osobna baza `fuelclub_demo`. Instancja API do symulacji ma własny connection string i działa tylko w `Development`.
- Oznaczenie nadaje zaufana konfiguracja środowiska, nie klient: `Simulation:Enabled=true` i `Simulation:RunId=<id>` przy starcie instancji. Każda tura i rozmowa tej instancji dostaje `Synthetic=true` i ten `RunId`. Nagłówek ani pole żądania nie może tego włączyć ani zmienić.
- Poza `Development` opcja `Simulation:Enabled` blokuje start (walidacja opcji).
- Instancja w trybie symulacji udostępnia `GET /api/simulation` (tylko wtedy): `runId`, dostawca i model. Symulator sprawdza przed startem, że trafił w oczekiwany przebieg i dostawcę. Dostawca to konfiguracja serwera - symulator go nie przełącza, tylko weryfikuje. Trasy nie ma w `schema.d.ts`: zwykła instancja (z której generujemy typy) jej nie mapuje, a front jej nie używa - korzysta z niej tylko symulator.
- Manifest przebiegu (plik lokalny, poza Git): konta (e-mail, rola w rozkładzie, identyfikator użytkownika z API), wykonane operacje z ich kluczami idempotencji i wynikiem. Ponowne uruchomienie z tym samym manifestem powtarza operacje z tymi samymi kluczami: nic się nie podwaja. Hasła kont symulacji pochodzą z ziarna przebiegu, nie są zapisywane w manifeście.
- Sprzątanie dotyczy jednego przebiegu (`RunId` z manifestu) i odmawia działania, gdy baza nie jest `fuelclub_demo`.
- Symulator respektuje wszystkie limity API (logowanie i rejestracja, zakupy, podgląd, czat, zgłoszenia): własne tempo poniżej limitu i `Retry-After` przy `429`. Instancja demo może mieć wyższe limity w swojej konfiguracji - to ustawienie serwera, nie obejście.

### 2. Rozmowa i metryki

- `conversationId` wydaje serwer. Rozmowa powstaje osobnym `POST /api/chat/conversations` z `Idempotency-Key`, zanim ruszy model (doprecyzowanie z P16b: nie w pierwszej turze).
  - Odzyskanie po utracie odpowiedzi: klient trzyma klucz do skutku. Ponowienie z tym samym kluczem zwraca tę samą rozmowę, więc nie powstaje duplikat. Zdarzenie `meta` tylko potwierdza id; jego utrata niczego nie gubi, bo klient zna id sprzed tury.
- Każda tura podaje `conversationId`. Serwer sprawdza właściciela (pseudonim) i stan jednym warunkowym `UPDATE`, przed wywołaniem modelu:
  - cudze lub nieznane id daje `404 assistant.conversation_not_found` (ta sama odpowiedź, więc id niczego nie zdradza);
  - zakończone lub bezczynne daje `409 assistant.conversation_ended`.
  Front zakłada wtedy nową rozmowę i jednokrotnie ponawia pytanie, bo model jeszcze nie ruszył.
- Rozmowa ma jawne zakończenie: klient je wysyła (wyjście z ekranu, koniec scenariusza) albo serwer zamyka ją po bezczynności (`Assistant:ConversationIdleTimeout`, domyślnie 30 min). Zapisany powód: `Client`, `Idle`, `Handoff`.
  - Zakończenie jest idempotentne: rozmowa zachowuje pierwszy powód i czas.
  - Bezczynność kończy się z czasem upływu limitu, nie z chwilą zauważenia. Żaden powód nie oznacza rozwiązania sprawy.
- Przekazanie liczy się raz na rozmowę:
  - „zaproponowano konsultanta” to tury z `HandoffProposed`;
  - „utworzono zgłoszenie” to `SupportCaseId` rozmowy, wiązane przy tworzeniu zgłoszenia (`conversationId` w żądaniu, tylko własna rozmowa). Pierwsze powiązanie wygrywa, a ponowienie (ten sam klucz) i kolejne zgłoszenie go nie zmieniają (UNIQUE w bazie jako ostatnia linia).
- Metryka w panelu to **odsetek rozmów bez przekazania**, z mianownikiem (zakończone rozmowy w zakresie filtra). To nie jest containment ani skuteczność rozwiązania: brak zgłoszenia może oznaczać porzucenie. Containment wymaga jawnego sygnału rozwiązania sprawy, którego nie mamy.
- Metryki rozmów i tur (koszt z cennika, kroki, p50/p95, przekazania) należą do P15b. P16 dostarcza dane i oznaczenia, nie osobny zestaw metryk.
- Ruch syntetyczny jest w panelu osobno (filtr, domyślnie wyłączony w widoku rzeczywistym). Wyniki z adaptera demo pokazują wydajność aplikacji i panel, nie opóźnienia Gemini - raportujemy je oddzielnie.

### 3. Dane dla judge'a

- Wyłącznie transkrypty syntetycznych rozmów symulatora. Plik lokalny poza Git, bez sekretów, kodów odbioru i tokenów potwierdzeń (zastąpione znacznikami).
- Przechowywanie: 14 dni od przebiegu, potem usuwane poleceniem symulatora. Dostawca oceny: Gemini (ten sam klucz z user-secrets), twardy limit kosztu na przebieg oceny.
- Wynik oceny zawiera wersję rubryki, model oceniający i rozróżnia ocenę negatywną od awarii oceniającego (błąd, timeout, niepoprawny format).
- Ruch prawdziwych użytkowników nie trafia do judge'a.

## Alternatywy

- **Oznaczenie nagłówkiem klienta** (np. `X-Simulation-Run`) - prostsze, ale klient mógłby oznaczyć prawdziwy ruch jako syntetyczny albo odwrotnie.
- **Tylko osobna baza, bez kolumn** - nie da się odróżnić przebiegów ani pokazać ruchu syntetycznego obok rzeczywistego.
- **conversationId od klienta / okno czasowe** - klient mógłby podać dowolne id (sklejanie cudzych rozmów), a okno czasowe łączy dwie karty przeglądarki w jedną rozmowę.
- **Containment jako „brak zgłoszenia”** - zawyża wynik o porzucone rozmowy.

## Konsekwencje

- Nowa tabela rozmów i kolumny przy turze (`ConversationId`, `Synthetic`, `RunId`); zmiana kontraktu czatu (`conversationId` w żądaniu i w `meta`), regeneracja `schema.d.ts`, front zakłada i kończy rozmowę.
- Symulator `apps/tools/Simulator` (.NET, tylko publiczne HTTP API, bez referencji do projektu API).
- Losowe oceny i rozmowy symulatora testują panel i przepływy, nie jakość agenta.
