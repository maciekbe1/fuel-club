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
| Gemini API (czat, wybór narzędzi) | tak | na atrapie odpowiedzi; na prawdziwym modelu jedna tura (saldo) | nie | Jakość wyboru narzędzi i argumentów nie jest jeszcze zmierzona - [evale Gemini](#evale-na-prawdziwym-modelu). [ADR 0011](/decyzje/0011-adapter-gemini/) |
| RAG - regulamin | tak | tak, lokalnie i w CI (model demo); z Gemini na atrapie | nie | Wyszukiwanie leksykalne w PostgreSQL, bez embeddingów; znane pudła. [ADR 0016](/decyzje/0016-regulamin-w-czacie/) |
| Hosting - Cloud Run | obrazy Docker, próba produkcyjna lokalnie | obrazy budowane w CI | nie | [Instrukcja](/instrukcje/obrazy-i-hosting/#cloud-run) |
| Vertex AI | nie | nie | nie | [Niżej](#vertex-ai) |
| Agent Builder / Dialogflow CX | nie | nie | nie | Ewentualnie osobny eksperyment po ukończeniu aplikacji, bez przepisywania agenta. |
| Agent głosowy | nie | nie | nie | Mikrofon z zamianą mowy na tekst to jeszcze nie agent głosowy. |
| Obserwowalność (ślady, metryki, anomalie) | tak | tak | nie | Panel LLMOps rozbudowuje P15. [Observability](/moduly/observability/) |
| Alerty anomalii na Slacka | reguły tak, wysyłka nie | reguły tak | nie | |
| LLM-as-judge | nie | nie | nie | P15e: najpierw rubryka i kalibracja offline. |
| Konsultant (przekazanie rozmowy) | tak | tak | nie | Własny panel, nie integracja z LiveChat. [Support](/moduly/support/) |
| Twilio / WhatsApp / CRM / LiveChat | nie | nie | nie | |

## Evale na prawdziwym modelu

Evale `assistant-evals-v2` są deterministyczne i używają modelu demo. Nie mówią, jak często prawdziwy model myli narzędzia.

:::caution[Planowane]
Pilotaż na Gemini (5 scenariuszy × 1) z pomiarem tokenów. Potem oszacowanie kosztu pełnego przebiegu i raport: skuteczność narzędzi, argumentów, odpowiedzi, naruszenia bezpieczeństwa. Bez blokowania CI. Klucz tylko w user-secrets.
:::

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
