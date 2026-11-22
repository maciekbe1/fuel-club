# Dziennik wznowienia

Spec: `specs/SPEC.md`. Plan: `specs/PLAN.md`. Pętla: `specs/LOOP.md`.

HEAD / branch: main (commity kroków lokalnie, bez push; ostatni push: c4a7883)
Etap i status: P0 - done; P1 - done; P2 - pending

## P0 - baza pracy (done, 2026-11-22)

Zakres: baseline na bazach testowych; korekty `PLAN-MVP.md` (role, reguła punktów od PaidAmount, nazwy RewardOffer/Coupon, status etapów 4-6, odnośnik do specs).
Zmienione pliki: `PLAN-MVP.md`, `IMPLEMENTATION-STATE.md`.
Testy (2026-11-22, HEAD 32b9005):
- `dotnet test apps/api.tests` (fuelclub_test) - passed 419/419
- `npm run typecheck`, `npm run lint`, `npm run format:check` - passed
- `npm run e2e` (fuelclub_e2e) - passed 46/46
- `docs: npm run build` - passed; `dotnet csharpier check apps` - passed
Review: c4a7883 - `/code-review high`, 4 uwagi: 3 przyjęte i poprawione, 1 odrzucona (dodatkowy indeks FK). P0 bez zmian kodu - review nie dotyczy.
Dokumentacja: `PLAN-MVP.md`.
Blokery: -

## P1 - katalog i wymiana (done, 2026-11-22)

Zakres: feature `rewards` (`/oferty`, skrót na ekranie głównym), potwierdzenie kosztu i warunków z wysyłką wersji z potwierdzenia, obsługa offer_changed/inactive/insufficient_points, nieznany wynik z ponowieniem tym samym kluczem; klucz nierozstrzygniętej operacji w sessionStorage (`useIdempotentMutation` `persist`), czyszczony przy logowaniu/rejestracji/wylogowaniu; 401 → „Zaloguj się”. E2E: admin testowy (`BootstrapAdmin`), fixture `staff` wyłączający oferty po teście. Narzędzie: `specs/browser-instance.sh` (instancja :5291/:3200, baza fuelclub_browser).
Zmienione pliki: `apps/web/features/rewards/*`, `app/(app)/oferty/page.tsx`, `components/home/HomeScreen.tsx`, `lib/idempotency.ts`, `features/auth/hooks.ts`, `eslint.config.mjs`, `.gitignore`, `playwright.config.ts`, `e2e/{fixtures,accounts,rewards.spec,loyalty-card.spec}.ts`, docs (rewards, loyalty, testy, ADR 0001), `specs/browser-instance.sh`.
Testy (2026-11-22): typecheck, lint, format - passed; `npx playwright test` - passed 58/58 (mobile + desktop); docs build - passed. API bez zmian (baseline P0: 419/419).
Próby negatywne: bez obsługi nieznanego wyniku → pada test utraconej odpowiedzi; bez `persist` → pada test wyjścia z ekranu.
Przeglądarka (Chrome, :3200, anna demo): katalog 3 ofert, wymiana klawiaturą (Enter na potwierdzeniu) 650 → 150 pkt + komunikat sukcesu; za mało punktów → komunikat, saldo bez zmian. Zmiana rozmiaru okna nie zmienia viewportu w tym narzędziu - szerokość telefonu pokrywa E2E (Pixel 7).
Review (`/code-review high`, 8 uwag, wszystkie przyjęte): stop zabijał przeglądarkę (tylko listenery); `.next-browser` w ESLint; zaokrąglenie maks. rabatu (liczby całkowite, 7,58); blokada nieznanego wyniku po nawigacji (sessionStorage + test); start na zajętych portach; 401 bez ponowienia; hasło w wyjściu skryptu; rosnący katalog E2E (retire). Poprawki zweryfikowane testami powyżej (samoprzegląd zmian po review).
Uwaga: Next 16 zachowuje stan ekranu przy nawigacji klienta (Activity) - po powrocie widać pierwotny komunikat; po przeładowaniu - przywrócony z sessionStorage.

## Następny krok

P2 - moje kupony: feature `coupons` (`/kupony`), lista Purchased/Active/Redeemed z migawką warunków i datami, aktywacja (idempotentna, `persist`, po mutacji GET), QR i kopiowanie kodu tylko dla aktywnego; link do „Moich kuponów” po wymianie i na ekranie głównym; E2E: QR dekodowany, ponowienie po utracie odpowiedzi, odświeżenie GET, izolacja dwóch kont; przeglądarka; review.
