---
title: Knowledge (regulamin)
description: Zatwierdzony regulamin programu jako baza wiedzy czatu - punkty w PostgreSQL i wyszukiwanie polskich pytań.
sidebar:
  order: 7
---

:::note[P14]
Zrealizowane: `regulamin-v1`, tabela `RuleFragments` z migracji, wyszukiwanie, narzędzie czatu `search_program_rules`, odpowiedzi ze źródłem (§), ewaluacje. Decyzja: [ADR 0016](/decyzje/0016-regulamin-w-czacie/).
:::

## Co robi

Czat odpowiada na pytania o zasady programu z zatwierdzonego regulaminu i podaje źródło („Regulamin, §3.2”). Gdy regulamin nie odpowiada, mówi to wprost i proponuje konsultanta. Regulamin wyjaśnia zasady; saldo, stan kuponów i zakupy nadal pochodzą z narzędzi konta.

## Gdzie jest kod

| Element | Ścieżka |
| --- | --- |
| Regulamin (wersja zatwierdzona) | `apps/api/Modules/Knowledge/Documents/regulamin-v1.md` (zasób wbudowany) |
| Podział na punkty, wersja bieżąca | `Modules/Knowledge/ProgramRules.cs` |
| Encja i indeks | `Modules/Knowledge/RuleFragment.cs` |
| Wyszukiwanie | `Modules/Knowledge/ProgramRulesSearch.cs` |
| Wczytanie do bazy | `Migrations/20261123132812_RuleFragments.cs` |
| Narzędzie czatu | `Modules/Assistant/Tools/AssistantTools.cs` (`search_program_rules`) |
| Normalizacja tekstu (wspólna z modelem demo) | `Infrastructure/PolishText.cs` |

## Przepływ

1. Model woła `search_program_rules` z pytaniem (demo: cała wiadomość, przycięta do 300 znaków).
2. Pytanie: małe litery bez znaków diakrytycznych, bez słów pomocniczych, każde słowo przycięte do prefiksu (do 5 liter), z synonimami pojęć programu.
3. Jedno zapytanie SQL liczy wagę każdego słowa (rzadkość w dokumencie), pokrycie każdego punktu i podobieństwo trygramowe. Zwraca najwyżej 5 punktów z pokryciem ≥ 0,45.
4. Model dostaje wersję dokumentu i fragmenty jako dane. Pusta lista (`no_match`) oznacza: regulamin tego nie opisuje; narzędzie samo dołącza propozycję konsultanta.

## Dane

### `RuleFragments`

| Kolumna | Typ | Uwagi |
| --- | --- | --- |
| `Id` | `uuid` | PK |
| `DocumentVersion` | `varchar(32)` | `regulamin-v1`; `UNIQUE (DocumentVersion, Section)` |
| `Section` | `varchar(8)` | `3.2`; `CHECK (~ '^[1-9][0-9]?\.[1-9][0-9]?$')` |
| `Heading`, `Text` | `varchar(100)`, `varchar(2000)` | nagłówek sekcji i treść punktu |
| `SearchText` | `varchar(2200)` | nagłówek i treść po normalizacji; `CHECK` małe litery |
| `SearchVector` | `tsvector` | generowana z `SearchText` (`simple`), indeks GIN |

Rozszerzenie `pg_trgm` (migracja). Wiersze zapisuje tylko migracja; nowa wersja regulaminu to nowy plik i nowa migracja.

## Zabezpieczenia

- Do `tsquery` trafiają tylko `[a-z0-9]` i operatory budowane w kodzie; wartości idą jako parametry SQL.
- Wynik nie zawiera danych klienta i nie zależy od użytkownika.
- Fragmenty to dane, nie polecenia (prompt Gemini; ewaluacja z podrzuconą instrukcją).
- Zatwierdzony plik jest chroniony testem skrótu SHA-256.

## Testy

| Plik | Co sprawdza |
| --- | --- |
| `apps/api.tests/ProgramRulesSearchTests.cs` | skrót `regulamin-v1`, podział na 32 punkty, baza = plik; 23 pytania (13 strojących, 10 kontrolnych) trafiają właściwy punkt wśród wyników; 9 pytań spoza regulaminu nic nie znajduje; odmiana („punktów/punkty”, „nagrody/nagrodę”) daje ten sam termin; do zapytania trafiają tylko litery i cyfry |
| `apps/api.tests/AssistantTests.cs` | demo cytuje §6.1 z wersją; brak odpowiedzi → `propose_handoff` i zdarzenie `handoff`; złe argumenty odrzucone; model dostaje fragmenty jako dane |
| `apps/api.tests/GeminiTests.cs` | fragmenty wracają do Gemini ze źródłem i wersją |
| `apps/api.tests/Evals/assistant-cases.v2.json` | 6 przypadków regulaminu (kategorie `regulamin-*`) |
| `apps/web/e2e/chat.spec.ts` | pytanie o zasady → odpowiedź z §6.1 i znacznik „Regulamin”; pytanie spoza regulaminu → przycisk konsultanta |

Znane pudła i margines progu: [ADR 0016](/decyzje/0016-regulamin-w-czacie/#konsekwencje).
