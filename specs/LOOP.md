# Instrukcja wykonawcza pętli Fuel Club

Pracuj w /Users/maciejbednarczyk/Projekty/fuel-club. Przeczytaj AGENTS.md, CLAUDE.md i lokalne instrukcje oraz specs/SPEC.md, specs/PLAN.md i IMPLEMENTATION-STATE.md. Nie zmieniaj instrukcji właściciela tylko po to, by ominąć ograniczenia.

Cel: realizuj kolejne obowiązkowe etapy P0-P8 bez pytania o zgodę. Właściciel zatwierdził specyfikację i plan; decyzje szczegółowe (w tym ADR czatu) podejmujesz sam zgodnie ze specyfikacją i zapisujesz w ADR/dzienniku. Zmiany API i frontendu oraz aktualizacja planu są dozwolone. P9 wykonuj w dostępnym zakresie; brak konta/sekretu nie oznacza zgody na utworzenie zasobów. P10 jest opcjonalny i nie rozszerza automatycznie MVP.

## Jedna iteracja

1. Odczytaj stan repo i dziennik. Wybierz pierwszy nieukończony krok i niewielki, sprawdzalny zakres.
2. Zapisz kryteria odbioru i plan testów. Rozstrzygaj zwykłe szczegóły zgodnie ze specyfikacją; pytaj tylko przy sprzeczności lub decyzji zmieniającej zatwierdzony zakres.
3. Zaimplementuj czysty moduł zgodnie z istniejącymi konwencjami. Bez zbędnych abstrakcji. Zachowaj atomowość, własność danych, CSRF i semantykę idempotencji.
4. Uruchom adekwatne kontrole. Przy zmianie kontraktu wygeneruj typy. Testy tylko na bazach testowych; nie czyść bazy dev. Krok z UI sprawdź też ręcznie w przeglądarce (Claude in Chrome): przepływ, szerokość telefonu i desktopu, stany błędu. Do tego własna instancja: API na :5291 z bazą `fuelclub_browser` (kontener `postgres-test`, konta demo włączone) i Next na :3200 z `API_INTERNAL_URL` na to API - nie dotykaj bazy dev ani procesów właściciela (:5271, :3000).
5. Uruchom niezależne `/code-review high` na diffie kroku (bezpieczeństwo, transakcje, współbieżność, utrata odpowiedzi, stan UI, zgodność testów i dokumentacji). Oceń każdą uwagę: przyjęta albo odrzucona z powodem.
6. Popraw potwierdzone uwagi. Dodaj regresję dla realnego błędu, powtórz odpowiednie kontrole i review zmienionego zakresu.
7. Uzupełnij dokumentację, zbuduj docs, sformatuj kod, zapisz IMPLEMENTATION-STATE.md z dowodami i zrób lokalny commit kroku (bez push). Dopiero wtedy oznacz krok done i przejdź do następnego.

## Warunki zatrzymania i wznowienia

- Brak dostępu, sekretów lub niezbędna decyzja: zapisz blocker i dokładny następny krok; kontynuuj jedynie niezależne zadania. Nie udawaj uruchomienia testów ani wdrożenia.
- Dwie bezskuteczne próby tej samej naprawy: zmień diagnozę, zamiast powtarzać tę samą czynność. Jeśli dalsza praca wymaga użytkownika, zapisz stan i zakończ raportem.
- Pętla działa w aktywnej sesji (`/loop` bez interwału: jedna iteracja = jeden krok planu). Nie uruchamiaj crona, nieskończonego skryptu shell ani automatycznych kliknięć zgód. To nie jest gwarancja działania po zamknięciu sesji.
- Po wznowieniu czytaj dziennik, git diff i najnowsze instrukcje; nie zaczynaj projektu od nowa.
- Raportuj krótko po etapie: wynik, testy, review, blocker i następny krok. Nie powtarzaj całego planu.

## Start

Zacznij od P0. Najpierw potwierdź stan c4a7883 lub nowszy, sprawdź migrację odbioru i uruchom baseline. Następnie przejdź do katalogu i wymiany. Nie zatrzymuj się na samym opisie planu, jeśli masz dostęp do implementacji i testów.
