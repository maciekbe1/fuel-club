---
title: "0019 - Ocena człowieka w Phoenix"
description: Próbka kalibracji judge'a jako spany z treścią w lokalnym Phoenix, ocena w UI, zgodność z REST API; ocena na ślepo.
sidebar:
  order: 19
---

## Status

Decyzja przyjęta (2026-11-24, zgoda właściciela). Uzupełnia [ADR 0008](/decyzje/0008-slady-i-evale-czatu/) (spany bez treści, Phoenix lokalnie) i [ADR 0017](/decyzje/0017-ruch-syntetyczny-i-rozmowy/) (ruch syntetyczny, judge offline).

## Problem

Kalibracja judge'a wymaga oceny człowieka. Arkusz TSV i lista par w czacie z Claude są niewygodne i wiążą ocenę z agentem, który projektował rubrykę. Phoenix jest już lokalnym odbiorcą śladów, ale spany API celowo nie mają treści (ADR 0008), więc człowiek nie ma tam czego ocenić.

## Rozwiązanie

- `calibrate --phoenix http://127.0.0.1:6006` wybiera próbkę jak dotąd (arkusz TSV) i wysyła każdy wiersz jako osobny span do projektu `fuelclub-ocena`: wcześniejsze tury i pytanie (`input.value`), odpowiedź (`output.value`), w `metadata` scenariusz, narzędzia, cel propozycji i przycisk konsultanta. Mapowanie wiersz → span w `.simulation/<run>/judge/phoenix-<rubryka>-<model>.json`.
- Treść idzie **tylko z symulatora** i tylko z transkryptów syntetycznych przebiegów (manifest symulatora jak przy judge'u). Spany API bez zmian - bez treści.
- Tylko Phoenix na tej maszynie: adres musi być loopback, inaczej odmowa (spany niosą treść tur).
- Konfiguracja oceny `human_verdict` (kategorie `pass`/`fail`, komentarz w wyjaśnieniu) tworzona przez REST API i przypisana do projektu.
- `agreement --phoenix` pobiera oceny `HUMAN` (`/v1/projects/{projekt}/span_annotations`; przy kilku - najnowsza), wpisuje je do arkusza TSV (dowód przy przebiegu) i liczy zgodność i kappę jak dotąd.
- **Ocena na ślepo**: werdykty judge'a (`judge_verdict`, `LLM`, z uzasadnieniem) trafiają do Phoenix dopiero, gdy każdy wiersz ma ocenę człowieka. Wcześniej `agreement` podaje, ile wierszy zostało.
- Phoenix z wolumenem (`phoenix_data`): ślady i oceny przeżywają restart kontenera.
- Po wysłaniu werdyktów judge'a oceny człowieka są zamrożone w pliku mapowania: późniejsza zmiana w Phoenix nie zmienia zgodności, `agreement` tylko podaje liczbę zmian.
- Eksport sprawdza, że Phoenix ma każdy span (nieudany eksport OTLP jest tylko logowany), zanim zapisze mapowanie. Nieudany eksport można ponowić: istniejący arkusz bez mapowania jest użyty ponownie. Arkusz odzwierciedla Phoenix - ocena usunięta w UI znika z arkusza.

## Alternatywy

- **Arkusz TSV**: działa, ale komórki z długimi odpowiedziami są nieczytelne; zostaje jako zapis wyników i tryb bez Phoenix.
- **Treść w spanach API**: łamie ADR 0008 (treść klientów poza bazą); dla kalibracji wystarczą dane syntetyczne.
- **Datasety Phoenix**: zaprojektowane pod eksperymenty, ocena człowieka w UI jest przy spanach.
- **Langfuse / Argilla**: kolejne narzędzie do uruchomienia; Phoenix jest już w `compose.yml`.
- **Werdykt judge'a od razu w Phoenix**: szybsze porównanie, ale sugeruje człowiekowi odpowiedź.

## Konsekwencje

- Phoenix trzyma treść syntetycznych tur; `docker compose down -v` je usuwa. Na serwerze Phoenix się nie uruchamia (ADR 0008).
- Ponowny eksport tej samej rubryki i modelu jest odrzucany (plik mapowania istnieje) - bez duplikatów spanów.
- Próbka nadal jest dobierana według werdyktów judge'a (połowa pass, połowa fail); człowiek ich nie widzi, ale proporcja nie jest losowa.
- Fragmentów regulaminu w spanie nie ma (transkrypt ich nie zapisuje) - człowiek ocenia zgodność z regulaminem z własnej wiedzy, jak w arkuszu.
