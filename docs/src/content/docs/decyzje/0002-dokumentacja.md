---
title: "0002 - Dokumentacja: Starlight + Diátaxis"
description: Narzędzie i układ dokumentacji projektu.
sidebar:
  order: 2
---

Status: przyjęta, 2026-11-21

## Problem

Projekt ma się dać zrozumieć i uruchomić bez autora. Wiedza leżała w `PLAN-MVP.md`, pojedynczym ADR i w kodzie. Brakowało nawigacji, wyszukiwania i rozdzielenia „jak to zrobić” od „dlaczego tak jest”.

## Rozwiązanie

- **Astro Starlight** w `docs/`: Markdown w repozytorium, nawigacja, wbudowana wyszukiwarka (działa na zbudowanej stronie).
- **Diátaxis**: Nauka, Instrukcje, Referencja, Wyjaśnienia, a do tego Moduły i Decyzje (ADR).
- Strona modułu ma stały układ: co robi → gdzie jest kod → przepływ → dane → zabezpieczenia → testy.
- Funkcje planowane są wyraźnie oznaczone jako niezrealizowane.

## Alternatywy

- **Sam Markdown w `docs/`**: zero narzędzi, ale bez nawigacji i wyszukiwania.
- **Docusaurus**: dojrzały, ale cięższy (React, więcej konfiguracji) przy podobnym efekcie.
- **Wiki lub Notion**: poza repozytorium, łatwo się rozjeżdża z kodem.

## Konsekwencje

- Zmiana zachowania aplikacji i aktualizacja dokumentacji trafiają do tego samego commita lub PR-a. Generator nie zapewni aktualności sam.
- Kontrakt HTTP jest w OpenAPI. Dokumentacja opisuje zachowanie, którego OpenAPI nie pokazuje (CSRF, autoryzacja, kody błędów).
- Kolejny proces Node do utrzymania (`docs/package.json`).
