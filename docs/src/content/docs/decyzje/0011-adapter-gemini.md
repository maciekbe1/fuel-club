---
title: "0011 - Adapter Gemini"
description: Opcjonalny dostawca modelu za kontraktem IChatModel - konfiguracja, dane wysyłane do Google, podpisy wywołań.
sidebar:
  order: 11
---

## Status

Decyzja przyjęta (2026-11-22), P10. Testy z podstawionym API Gemini przechodzą. Wywołanie prawdziwego API wymaga klucza; test na żywo jest pomijany bez niego.

## Problem

Czat działa na deterministycznym adapterze demo. Prawdziwy model ma działać za tym samym kontraktem ([ADR 0007](/decyzje/0007-czat-i-potwierdzenia/)):

- te same 4 narzędzia tylko do odczytu;
- aktywacja wyłącznie przyciskiem;
- te same limity i ślady.

Lokalne testy i CI nie mogą zależeć od chmury.

## Rozwiązanie

- `Assistant:Provider`: `demo` (domyślnie) albo `gemini`. Przy `gemini` wymagany jest `Assistant:Gemini:ApiKey` - bez niego API nie startuje.
- `GeminiChatModel` woła REST `models/{model}:generateContent` z deklaracjami narzędzi (function calling).
  - Klucz idzie w nagłówku `x-goog-api-key`, nigdy w URL.
  - Błąd HTTP to `assistant.provider_error`. Treść odpowiedzi błędu nie trafia do logu.
- Krok modelu to odpowiedź albo **jedno lub więcej** wywołań narzędzi (Gemini może zwrócić kilka `functionCall` naraz).
  - `ChatService` wykonuje je po kolei.
  - Krok, który przekroczyłby `MaxToolCalls`, nie wykonuje żadnego wywołania (`assistant.tool_limit`).
  - Pętlę i czasy prowadzi `ChatService`.
- **Podpisy wywołań:** Gemini 3 wymaga odesłania `thoughtSignature` z każdym `functionCall` bieżącej tury (inaczej `400`).
  - `ToolCall.ProviderPart` przechowuje oryginalną część odpowiedzi modelu, a `ToolCall.Step` - numer kroku.
  - `ChatModelContext.ToolCalls` oddaje je w następnym kroku: wywołania jednego kroku w jednej wiadomości modelu, w kolejności, a ich wyniki w jednej wiadomości użytkownika. Odpowiedź nosi nazwę wywołania (także odrzuconego narzędzia spoza listy) i jego `id`, jeśli było.
- Tokeny: wejście `promptTokenCount`; wyjście `candidatesTokenCount` + `thoughtsTokenCount` (rozumowanie jest płatne). Trafiają do śladu tury.
  - Nieudane albo przerwane wywołanie modelu mogło zostać naliczone, więc suma tury jest nieznana (`null`), nie częściowa. Podsumowanie liczy takie tury osobno.
- Model `Assistant:Gemini:Model`, domyślnie `gemini-3.5-flash-lite` (kod z listy modeli Google, 2026-11). Przed zmianą sprawdź aktualną listę.

### Dane wysyłane do Google

Zakres uzgodniony z właścicielem:

- **Wysyłane:**
  - wiadomość i historia rozmowy z ekranu;
  - wyniki narzędzi: saldo, oferty (z brakującymi punktami), kupony (id, nazwa, stan, warunki - bez kodów), wynik propozycji aktywacji;
  - porównanie kuponów: planowane litry i kwota podane przez klienta, rabaty, koszt i brakujące punkty;
  - wyjaśnienie punktów (zgoda właściciela 2026-11-22): **tylko** brutto, rabat, zapłacono, naliczone punkty i zasada obliczenia ostatniego zakupu.
- **Nie wysyłane:**
  - id użytkownika, e-mail;
  - kody kart i kuponów;
  - token potwierdzenia (`ChatService` usuwa go przed modelem);
  - data, numer paragonu, stacja, kasa, litry i id zakupu.

Testy sprawdzają brak id i e-maila w żądaniu oraz dokładny zestaw pól wyjaśnienia punktów (bez stacji, kasy, paragonu i daty). Ślady tur nadal zawierają tylko metadane. Retencję po stronie Google określa umowa i plan konta Google, nie ta aplikacja.

## Alternatywy

- **Oficjalny SDK Google dla .NET**: dodatkowa zależność, a potrzebny jest jeden endpoint REST. Odrzucone na teraz.
- **Vertex AI**: wymaga projektu GCP i uwierzytelniania kontem usługi. Klucz Gemini API wystarcza do demo.

## Konsekwencje

- Odpowiedzi modelu nie są deterministyczne. Evale `assistant-evals-v1` dotyczą adaptera demo. Dla Gemini jest test na żywo `Live_gemini_answers_the_balance_from_the_tool` (zmienna `FUELCLUB_GEMINI_API_KEY`, opcjonalnie `FUELCLUB_GEMINI_MODEL`).
- Bezpieczeństwo nie zależy od modelu: narzędzia czytają tylko dane sesji, a aktywacja wymaga przycisku. Prompt injection może co najwyżej zmienić tekst odpowiedzi albo wywołać odrzucone narzędzie.
