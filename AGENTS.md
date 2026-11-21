# Instrukcje dla agentów

## Przed rozpoczęciem

- Przeczytaj i stosuj główny `CLAUDE.md`.
- Przeczytaj lokalny `AGENTS.md` dotyczący zmienianego obszaru.
- Przeczytaj istniejącą dokumentację funkcji.
- Domyślnie pomagaj użytkownikowi implementować samodzielnie.
  Edytuj kod, gdy użytkownik tego zażąda.

## Jakość

- Program.cs wyłącznie składa aplikację.
- Oddzielaj HTTP, logikę aplikacji i infrastrukturę.
- Zachowuj istniejące konwencje; nie dodawaj zbędnych warstw.
- Nie uzasadniaj złych praktyk zakresem MVP.

## Dokumentacja

- Źródła: docs/src/content/docs/.
- Aktualizuj dokumentację razem ze zmianą zachowania, API,
  konfiguracji, danych, zabezpieczeń lub sposobu uruchomienia.
- Aktualizuj istniejące strony zamiast je dublować.
- Oddzielaj stan zaimplementowany od planów.
- Istotne decyzje architektoniczne zapisuj jako ADR.

## Przed zakończeniem

- Uruchom kontrole odpowiednie do zmiany.
- Przy zmianach dokumentacji sprawdź jej build.
- Wskaż, co zmieniono i jak to sprawdzono.
- Rozróżniaj przegląd kodu od uruchomienia testów.
- Wskaż zaktualizowane dokumenty albo wyjaśnij,
  dlaczego aktualizacja nie była potrzebna.
- Jeśli czegoś nie udało się zweryfikować, napisz to wprost.
