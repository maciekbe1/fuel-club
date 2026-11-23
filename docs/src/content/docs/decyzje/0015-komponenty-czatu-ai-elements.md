---
title: "0015 - Komponenty czatu z AI Elements"
description: Okno czatu z asystentem i rozmowa z konsultantem na komponentach AI Elements (shadcn); protokół i logika zostają nasze.
sidebar:
  order: 15
---

## Status

Decyzja przyjęta (2026-11-23), po uwadze właściciela: mniej własnego kodu UI, więcej sprawdzonych bibliotek.

## Problem

Ekrany czatu (`/czat`) i rozmowy z konsultantem miały ręcznie pisane dymki, przewijanie do dołu, podpowiedzi i zwykły tekst odpowiedzi (bez formatowania odpowiedzi modelu).

## Rozwiązanie

[AI Elements](https://ai-sdk.dev/elements) (Vercel) - komponenty w rejestrze shadcn, kopiowane do `apps/web/components/ai-elements/` jak `components/ui/` (nie edytujemy ich, ESLint je pomija):

- `conversation` - przewijany kontener trzymający dół (`use-stick-to-bottom`), przycisk „Przewiń na dół”, pusty stan; w czacie i w rozmowie z konsultantem;
- `message` - dymki (`Message`, `MessageContent`) i `MessageResponse`: odpowiedź asystenta jako markdown przez `streamdown` (`AssistantAnswer.tsx`; obsługa niedomkniętej składni w trakcie strumienia). Domyślnie `streamdown` przepuszcza obrazki z dowolnego adresu i ma angielskie przyciski, więc: bez elementów `img` i `a` (link zostaje tekstem - odpowiedź nie każe przeglądarce pobrać ani otworzyć obcego adresu), bez wbudowanych przycisków (`controls={false}`), `remark-breaks` zachowuje pojedyncze nowe linie. Wiadomości ludzi w rozmowie z konsultantem zostają zwykłym tekstem;
- `suggestion` - podpowiedzi pytań.

Zostaje nasze: protokół SSE i reducer tury (`features/assistant/hooks.ts`), potwierdzenia aktywacji, przekazanie do konsultanta, kursor i hub w `features/support`, pole wiadomości (shadcn `textarea`).

## Alternatywy

- **`prompt-input` z AI Elements**: 1414 linii (załączniki, zrzuty, mowa) i 6 dodatkowych komponentów shadcn w miejsce ok. 70 linii formularza. Odrzucone.
- **assistant-ui**: własny runtime czatu (trzeba by adaptować nasz protokół z potwierdzeniami), Radix i Zustand w zależnościach; projekt jest na base-ui i bez Zustand (ADR 0001). Odrzucone.
- **Chat UI Kit React, ChatUI (Alibaba)**: własne systemy stylów obok Tailwind/shadcn. Odrzucone.
- **TanStack AI**: SDK do dostawców modeli, nie komponenty; nasz czat idzie przez własne API. Nie dotyczy.

## Konsekwencje

- Zależności: `streamdown`, `remark-breaks`, `ai` (tylko typy, importowane przez `message.tsx`; nie używamy AI SDK w kodzie), komponenty shadcn `button-group`, `tooltip`, `scroll-area`, `separator`.
- `globals.css`: `@source` dla klas `streamdown`.
- Odpowiedzi modelu mogą zawierać markdown (listy, pogrubienia); dalej bez danych spoza narzędzi (ADR 0007).
- Okno czatu ma stałą wysokość (przewija się wnętrze), pole wiadomości pod nim.
