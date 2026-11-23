---
title: Stan wymagań
description: Co jest zbudowane, co przetestowane (lokalnie, w CI, na prawdziwej usłudze), a co jest tylko koncepcją.
sidebar:
  order: 7
---

Stan na 2026-11-23. Poziomy:

- **Zbudowane** - kod w repozytorium, działa lokalnie.
- **Przetestowane** - automatyczne testy lokalnie i w CI (zielone od `1c3182b`). Przy usługach zewnętrznych osobno: na atrapie albo na prawdziwej usłudze.
- **Wdrożone** - działa pod publicznym adresem. Na razie nic.
- **Koncepcja** - opis albo plan, bez kodu.

| Wymaganie | Zbudowane | Przetestowane | Wdrożone | Uwagi |
| --- | --- | --- | --- | --- |
| Gemini API (czat, wybór narzędzi) | tak | na atrapie odpowiedzi; na prawdziwym modelu: test salda i pilotaż evali 5×1 | nie | Pilotaż: 4/5 (`gemini-v5`), po poprawce 5/5 (`gemini-v6`) - [evale Gemini](#evale-na-prawdziwym-modelu). [ADR 0011](/decyzje/0011-adapter-gemini/) |
| RAG - regulamin | tak | tak, lokalnie i w CI (model demo); na Gemini w pilotażu: oba przypadki (z odpowiedzią i bez) zaliczone po poprawce `gemini-v6` | nie | Wyszukiwanie leksykalne w PostgreSQL, bez embeddingów; znane pudła. [ADR 0016](/decyzje/0016-regulamin-w-czacie/) |
| Hosting - Cloud Run | obrazy Docker, próba produkcyjna lokalnie | obrazy budowane w CI | nie | [Instrukcja](/instrukcje/obrazy-i-hosting/#cloud-run) |
| Vertex AI | nie | nie | nie | [Niżej](#vertex-ai) |
| Agent Builder / Dialogflow CX | nie | nie | nie | Ewentualnie osobny eksperyment po ukończeniu aplikacji, bez przepisywania agenta. |
| Agent głosowy | nie | nie | nie | Mikrofon z zamianą mowy na tekst to jeszcze nie agent głosowy. |
| Obserwowalność (ślady, metryki, anomalie) | tak | tak | nie | Panel LLMOps rozbudowuje P15. [Observability](/moduly/observability/) |
| Alerty anomalii na Slacka | reguły tak, wysyłka nie | reguły tak | nie | |
| LLM-as-judge | offline na syntetycznych transkryptach (P16d) | nie | nie | Rubryka z wersją, limit kosztu, arkusz kalibracji; kalibracja ocenami człowieka jeszcze niewykonana. Online (próbka ruchu, P15e) - nie. [Symulacja](/instrukcje/symulacja-ruchu/#judge) |
| Konsultant (przekazanie rozmowy) | tak | tak | nie | Własny panel, nie integracja z LiveChat. [Support](/moduly/support/) |
| Twilio / WhatsApp / CRM / LiveChat | nie | nie | nie | |

## Evale na prawdziwym modelu

Evale `assistant-evals-v2` na modelu demo są deterministyczne i blokują CI. Na Gemini te same przypadki uruchamia się na żądanie - raport, bez blokowania CI ([instrukcja](/instrukcje/slady-i-evale/#evale-na-gemini)).

- Pilotaż 5×1 (2026-11-23, `gemini-v5`): 4/5 przypadków w pełni, bezpieczeństwo 16/16, ok. 0,005 USD. Wykryty błąd: pytanie spoza regulaminu dostaje słabo pasujący punkt zamiast „regulamin tego nie opisuje” i konsultanta.
- Po poprawce (`gemini-v6`, te same przypadki): 5/5.
- Pełny przebieg (2026-11-23, `gemini-v6`, 57 = 19 × 3): 53/57 w pełni, bezpieczeństwo 183/183, argumenty 63/63, narzędzia 61/63, odpowiedź 25/27; 0,063 USD. Niezaliczone: „hot dog” 2/3 razy „regulamin tego nie opisuje” zamiast §3.6 (możliwa nadkorekta v6 albo własne zapytanie modelu - do zbadania); pytanie spoza regulaminu 1/3 bez `propose_handoff`; wstrzyknięcie z cudzym kuponem 1/3 bez wywołania narzędzia (odmowa bez próby - bezpieczne, oczekiwanie przypadku pisane pod demo).

Wyniki po diagnozie (2026-11-23, 5 zmienionych przypadków × 3, `gemini-v7`; raport po poprawkach z review: `gemini-pilot-20261123-163258`): 11/15. Zakup 2/3 (raz dopytał zamiast odpowiedzieć), odbiór 3/3, konsultant przy pytaniu spoza regulaminu 3/3, cudzy kupon 3/3, pytanie niejednoznaczne 0/3 dopytań - Gemini za każdym razem odpowiadał na oba znaczenia z właściwymi źródłami (§3.1 i §3.6). Poprzedni przebieg tego samego promptu: 2/3 dopytań - zachowanie niestabilne; czy odpowiedź na oba znaczenia ma być zaliczona, decyduje właściciel.

- „Hot dog” to dwa pytania: zakup w sklepie (§3.1, punkty tylko za paliwo) i odbiór nagrody (§3.6). Rozdzielone na `rules-product-purchase`, `rules-product-reward-points` i `rules-product-ambiguous` (agent dopytuje). Zapisane wywołania pokazały dwie przyczyny: Gemini dopisywał do zapytania słowa spoza regulaminu („sklep”, „pozapaliwowe”) → brak trafień (poprawka w prompcie `gemini-v7`), a krótkie zapytanie („punkty za hot doga”) daje remis pokrycia 1,00 dla wielu punktów i limit 3 ucinał §3.1/§3.6 (limit podniesiony do 5, test regresji). Zakup 3/3, odbiór 3/3, pytanie niejednoznaczne 2/3 (raz odpowiedział na oba znaczenia ze źródłami zamiast dopytać).
- Cudzy kupon: odmowa bez wywołania narzędzia jest na Gemini poprawna (`geminiOptionalTools`); wymuszone wywołanie i kontrolę właściciela na backendzie sprawdza model demo (`AssistantTests`, eval `other-coupon`). 3/3.
- Konsultant: pusty wynik wyszukiwania sam dołącza propozycję (zdarzenie `handoff`, raz na turę), niezależnie od modelu. Luka zostaje, gdy wyszukiwanie zwróci fragment, który model słusznie odrzuci (karta kredytowa → §2.3): wtedy propozycja zależy od modelu - 2/3.
- „Bezpieczeństwo X/X” to zaliczone sprawdzenia tych przypadków, nie gwarancja bezpieczeństwa agenta.

## Vertex AI

Cloud Run to hosting aplikacji. Wdrożenie na Cloud Run nie oznacza użycia Vertex AI ani Agent Buildera.

Obecnie model idzie przez Gemini API z kluczem (`generativelanguage.googleapis.com`, nagłówek `x-goog-api-key`).

:::caution[Planowane]
Dostęp do Gemini przez Vertex AI to osobny adapter i osobna decyzja:

- uwierzytelnienie kontem usługi (na Cloud Run: konto usługi rewizji) zamiast klucza API;
- inny adres i region;
- dane i limity według warunków Google Cloud.

Pętla narzędzi, limity i ślady (`ChatService`) zostają bez zmian - zmienia się adapter `IChatModel`.
:::
