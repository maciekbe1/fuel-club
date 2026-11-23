# Fuel Club - plan wykonania

Specyfikacja: specs/SPEC.md. Każdy krok osobno przechodzi pętlę z specs/LOOP.md. Nie uznawaj obecności kodu za dowód działania.

## Kolejność i kryteria odbioru

1. **P0 - baza pracy.** Przeczytaj instrukcje i dokumentację; sprawdź HEAD i lokalne zmiany. Review c4a7883 już wykonane (`/code-review high`, uwagi rozstrzygnięte w tym commicie). Uruchom baseline na odizolowanych bazach. Popraw status PLAN-MVP.md. Wynik: zapisane faktyczne wyniki i brak nierozwiązanych błędów blokujących.
2. **P1 - katalog i wymiana.** UI oraz E2E: sukces, brak punktów, zmieniona wersja, utrata odpowiedzi i bezpieczne ponowienie. Jedna operacja daje jeden kupon i jeden debit.
3. **P2 - moje kupony.** Lista stanów, aktywacja, QR i kopiowanie. E2E odczytuje QR, sprawdza ponowienie, odświeżenie GET i izolację dwóch kont.
4. **P3 - kupon w kasie i historia.** Podgląd (warunki po pełnym kodzie, rabat na „Przelicz”, w granicach limitu prób), unieważnianie nieaktualnego podglądu, skan/wklejanie, rabat w wyniku i historii. E2E całego obiegu, zużytego kuponu i utraty odpowiedzi. Skanowanie niczego nie zatwierdza.
5. **P4 - administracja.** Strażnik `admin/layout.tsx`, lista i formularze RHF, konflikty wersji, wyłączenie oferty i nieznany wynik. E2E dwóch formularzy oraz braku uprawnień klienta/kasjera.
6. **P5 - kontrakt czatu i narzędzia.** ADR, payloady SSE/potwierdzenia, adapter lokalny i wspólna usługa aktywacji. Testy izolacji, walidacji, limitów i zgody. Bez UI na tym kroku.
7. **P6 - transport i ekran czatu.** Streaming, anulowanie, błędy, potwierdzenie. Test podzielonych ramek SSE i E2E zgody/odmowy/utraty odpowiedzi. Narzędzia wyświetlają tylko dane dozwolone użytkownikowi.
8. **P7 - evals i obserwowalność.** Wersjonowane scenariusze, ślady, retencja, anomalie, ograniczony widok admina. Testy progów, redakcji i izolacji. Uzupełnienie brakujących limitów.
9. **P8 - CI i odbiór lokalnego MVP.** Pełne testy, docs, świeży start według README i scenariusz demonstracji. Wszystkie kryteria specyfikacji mają dowód lub jawny blocker.
10. **P9 - hosting** (od 2026-11-23 docelowo Cloud Run). Obrazy, instrukcja i konfiguracja wdrożenia, następnie HTTPS i telefon, jeśli dostępne konto. Bez udawania ukończenia przy braku dostępu. Weryfikacja backup/restore i SSE.
11. **P10 - opcjonalny Gemini.** Oddzielny zakres po MVP, bez zależności lokalnych testów od chmury. Pozostałe integracje: projekt, nie automatyczna rozbudowa.
12. **P14 - regulamin i FAQ w czacie (RAG).** Po zamknięciu P11. Zrealizowany 2026-11-23 ([ADR 0016](../docs/src/content/docs/decyzje/0016-regulamin-w-czacie.md)).
    - **Dokument:** demonstracyjny regulamin zgodny z działaniem aplikacji (`apps/api/Modules/Knowledge/Documents/regulamin-v1.md`, zaakceptowany). Zasady zatwierdza właściciel; agent nie wymyśla ich jako obowiązujących. Wersja `regulamin-v1` dopiero po akceptacji całej treści; ograniczenia demo opisane jako ograniczenia demo, nie ogólne zakazy.
    - **Narzędzie:** `search_program_rules(query)` zwraca fragmenty z numerem punktu (§) i wersją dokumentu.
    - **Wyszukiwanie:** PostgreSQL, bez osobnej bazy wektorowej. `simple` + `unaccent` + `pg_trgm` to wariant do sprawdzenia, nie decyzja. Usunięcie akcentów nie rozwiązuje odmiany, więc najpierw testy na polskich pytaniach: odmiana („punktów/punkty”, „nagrody/nagrodę”) i pytania bez trafnego źródła (brak wyniku zamiast słabego trafienia).
    - **Odpowiedzi:**
      - czat odpowiada na podstawie fragmentów i podaje źródło („Regulamin, §4.2”);
      - gdy dokument nie odpowiada, mówi to wprost i proponuje konsultanta;
      - saldo, stan kuponu i szczegóły zakupu nadal pochodzą z narzędzi konta.
    - **Evals:**
      - punkty po rabacie;
      - aktywacja a odbiór;
      - nagrody produktowe;
      - brak terminu ważności;
      - pytanie bez odpowiedzi w dokumencie;
      - sprzeczna instrukcja zaszyta w dokumencie (fragment nie jest poleceniem).

### Kolejność po P14 (uzgodniona 2026-11-23)

1. Push i zielone CI.
2. Evale Gemini: te same scenariusze, kilka powtórzeń; poprawność narzędzi i argumentów, izolacja, handoff, RAG; znane pudła wyszukiwania jako jawne braki. Zapisany raport jako punkt odniesienia; bez blokowania CI.
3. P15a-d.
4. Judge offline (rubryka, kalibracja na ręcznie ocenionej próbce), potem online (P15e).
5. Publiczne wdrożenie na Cloud Run (decyzja właściciela 2026-11-23, zamiast Railway). Dostęp do modelu przez Vertex AI to osobna decyzja; Cloud Run nie oznacza Vertex ani Agent Buildera. Stan: `docs/.../referencja/stan-wymagan.md`.

Po evalach: porównanie wyszukiwania leksykalnego z hybrydowym na tym samym zbiorze. Slack i voice na końcu.

### P15 - panel LLMOps (planowane, po P14)

Rozbudowa istniejącej obserwowalności, nie drugi system. Zakres: SPEC, sekcja „P15”. Kolejność:

1. **P15a - kontrakt i trace tury.** ADR: identyfikacja rozmowy i zakończenia, korelacja tur, wywołań modelu (także ponowień), narzędzi i decyzji klienta. Lista dozwolonych danych, retencja i dostęp Admin. Widok osi czasu z czasami, wynikami, wersjami promptu/modelu i dostępnymi tokenami. Potwierdzenie wykonane osobnym HTTP jest powiązane z turą, nie sztucznie zagnieżdżone w zakończonym spanie.
2. **P15b - metryki i koszty.** Mediana/p95, kroki na turę, błędy narzędzi, handoff i odmowy potwierdzenia z jawnymi mianownikami. Koszt szacowany z użycia i wersjonowanego cennika. Kryterium: testy obliczeń, niepełnych danych, ponowień oraz braku podwójnego naliczenia.
3. **P15c - rozmowy i anomalie.** Lista rozmów z metadanymi, filtr „tylko podejrzane”, powody i przejście do trace. Wykorzystanie istniejącej kolejki przeglądu. Kryterium: izolacja ról, paginacja, poprawne powiązania i retencja, bez sekretów w API/eksporcie.
4. **P15d - raporty offline.** Zakładka Evale: wersja zestawu, prompt/model, commit, wyniki checków i szczegóły awarii. Runner nadal korzysta z apps/api.tests/Evals; raporty jako artefakty, bez surowych rozmów w Git. Kryterium: raport sukcesu, porażki i przerwanego przebiegu; krytyczne naruszenia blokują niezależnie od pass rate.
5. **P15e - online judge (realizacja otwartego P7c).** Ocena po zakończeniu rozmowy poza ścieżką odpowiedzi. Konfigurowalna próbka losowa i osobna próbka błędów, budżet, limity, kolejka, wersja rubryki i kalibracja ocenami człowieka. Domyślnie wyłączone bez dostawcy i zatwierdzonego zakresu danych. Kryterium: testy samplingu, budżetu, braku duplikatów ocen, błędów judge'a i braku skutków domenowych. Ocena każdej rozmowy nie jest domyślnym wymaganiem.

Po każdym kroku: testy, niezależne review, dokumentacja i zapis stanu zgodnie z LOOP. Przed zapisywaniem/eksportem treści rozmów lub argumentów wymagana decyzja właściciela o polach, odbiorcy i retencji; samo dopisanie planu jej nie zastępuje. P15e opcjonalny dla lokalnego MVP; brak klucza nie blokuje P15a-d.

### Rozbicie P7

- P7a: lokalne evals i metadane trace’ów, test redakcji, opcjonalny eksport OpenTelemetry do lokalnego Phoenix.
- P7b: metryki i reguły anomalii, kolejka przeglądu, dostęp i retencja.
- P7c (realizacja w P15e, jedna pozycja zakresu): opcjonalny judge na próbce z budżetem, raportem awarii i kalibracją. Bez klucza etap raportuje tę część jako niewykonaną, nie blokuje lokalnego MVP.
- Przed gromadzeniem treści rozmów lub wysyłaniem ich do zewnętrznego dostawcy uzgodnij zakres danych, odbiorcę i retencję. Domyślnie tylko metadane; pełne treści nie są automatycznie autoryzowane.

### P16 - ruch syntetyczny i dane demo (decyzje: ADR 0017)

Cel: dane w panelu „Obserwowalność” na pokaz 25.11. Metryki to zakres P15b - P16 dostarcza ruch, oznaczenia i definicję rozmowy, nie drugi zestaw metryk.

1. **P16a - manifest i populacja.** `apps/tools/Simulator` (.NET, tylko publiczne API), osobna baza `fuelclub_demo`, instancja API z `Simulation:Enabled`/`RunId` z konfiguracji. Konta z rozkładem punktów i kuponów, manifest z kluczami idempotencji, ponowne uruchomienie bez podwojeń, sprzątanie jednego przebiegu z kontrolą bazy, limity API. Najpierw przebieg 5 kont.
2. **P16b - rozmowy.** Serwerowy `conversationId`, jawne zakończenie, przekazanie raz na rozmowę; oznaczenie tur i rozmów `Synthetic`/`RunId`. Symulator scenariuszy z wagami (saldo, oferty, aktywacja z potwierdzeniem i bez, porównanie, regulamin z odpowiedzią i bez, konsultant ze zgłoszeniem, złość, injection, cudzy kupon, spoza tematu, wieloturowe), SSE jak front, adapter demo domyślnie; Gemini tylko na instancji skonfigurowanej na Gemini, z limitem kosztu. Transkrypty lokalnie poza Git.
3. **P16c - metryki (P15b)** na danych P16: koszt z cennika (null = nieznany), kroki, p50/p95 pierwszego zdarzenia i całości, odsetek rozmów bez przekazania (nie containment), filtr wersji promptu/modelu i ruchu syntetycznego, jawne mianowniki.
4. **P16d - judge** na syntetycznych transkryptach (P15e w wersji offline): rubryka z wersją, limit kosztu, ocena negatywna ≠ awaria oceniającego.
5. Później: feedback (kciuki, oznaczane syntetycznie w symulacji), k6.

## Obowiązkowe dowody

- API: adekwatne testy jednostkowe/integracyjne na PostgreSQL. Migracja: dane sprzed migracji, ograniczenia i nowy zapis; nie migruj działającej bazy dev bez uzgodnionego zatrzymania.
- Web: typecheck, lint, E2E zmienionego przepływu na telefonie i desktopie. Pełny zestaw na końcu większego etapu.
- Przeglądarka: po każdym kroku z UI ręczny przebieg w prawdziwej przeglądarce (Claude in Chrome) na działającej aplikacji dev - szerokość telefonu i desktopu, klawiatura, stany błędu; zrzuty/obserwacje w dzienniku. Nie zastępuje E2E.
- Kontrakt HTTP: regeneracja schema.d.ts z aktualnego API.
- Dokumentacja: aktualizacja właściwego modułu/referencji/ADR i build.
- Review: niezależne `/code-review high` na diffie kroku; każda uwaga z oceną (przyjęta / odrzucona z powodem), poprawki wraz z regresją. Nie wymagaj sztucznych uwag, gdy kod jest poprawny.
- Formatowanie jak w IDE: `dotnet csharpier format apps`, `npm run format` w `apps/web`.
- Commit: jeden lokalny commit na zakończony krok (kod, testy, docs, `schema.d.ts`, dziennik). Wiadomość zwięzła, bez dopisków o autorstwie (`Co-Authored-By` itp.).

## Dziennik wznowienia

Prowadź w repo IMPLEMENTATION-STATE.md. Aktualizuj po każdym kroku i przed przerwaniem:

```text
HEAD / branch:
Etap i status: pending | implementing | verifying | reviewing | blocked | done
Zakres zakończony:
Zmienione pliki:
Testy: polecenie, data, wynik (passed/failed/not run), przyczyna
Review: uwagi i rozstrzygnięcie
Dokumentacja:
Blokery:
Dokładny następny krok:
```

Zielony wynik historyczny nie zastępuje weryfikacji obecnego diffu. Nie kasuj zmian użytkownika. Nie wykonuj automatycznego push, merge ani publikacji: commity zostają lokalnie do polecenia właściciela.
