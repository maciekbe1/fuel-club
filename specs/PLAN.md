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
10. **P9 - hosting.** Obrazy, instrukcja i konfiguracja wdrożenia, następnie HTTPS i telefon, jeśli dostępne konto. Bez udawania ukończenia przy braku dostępu. Weryfikacja backup/restore i SSE.
11. **P10 - opcjonalny Gemini.** Oddzielny zakres po MVP, bez zależności lokalnych testów od chmury. Pozostałe integracje: projekt, nie automatyczna rozbudowa.

### Rozbicie P7

- P7a: lokalne evals i metadane trace’ów, test redakcji, opcjonalny eksport OpenTelemetry do lokalnego Phoenix.
- P7b: metryki i reguły anomalii, kolejka przeglądu, dostęp i retencja.
- P7c: opcjonalny judge na próbce z budżetem, raportem awarii i kalibracją. Bez klucza etap raportuje tę część jako niewykonaną, nie blokuje lokalnego MVP.
- Przed gromadzeniem treści rozmów lub wysyłaniem ich do zewnętrznego dostawcy uzgodnij zakres danych, odbiorcę i retencję. Domyślnie tylko metadane; pełne treści nie są automatycznie autoryzowane.

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
