---
title: "0004 - Karta lojalnościowa i symulator kasy"
description: Oddzielenie klienta, kasjera i administracji ofertami.
sidebar:
  order: 4
---

## Status

Decyzja przyjęta. Rola Cashier ze stanowiskiem i karta (odczyt, wydanie, wymiana) są zrealizowane. Zakup przez kasę (API) jest zrealizowany. Panel kasjera (frontend) i realizacja kuponów są planowane. Zastępuje wcześniejsze założenie, że administrator ręcznie wybiera klienta i nalicza punkty.

## Problem

Aplikacja ma odtwarzać obsługę programu lojalnościowego przy kasie: klient pokazuje kartę, a kasa rejestruje zakup. Aktywacja kuponu w telefonie i jego realizacja przy kasie to osobne zdarzenia.

## Decyzja

- User pokazuje losowy kod karty. Kod nie zawiera emaila ani userId i nie umożliwia logowania lub odczytu danych konta.
- Cashier skanuje kartę i zatwierdza zakup w panelu symulującym POS. Backend rozpoznaje klienta po karcie, liczy punkty i zapisuje wykonawcę z sesji kasjera.
- Admin zarządza ofertami i konfiguracją. Nie otrzymuje uprawnień Cashier automatycznie.
- Kupon nadal przechodzi Purchased -> Active -> Redeemed. Wymiana kosztuje punkty; aktywacja i realizacja nie pobierają ich ponownie.
- Kod aktywnego kuponu jest odrębny od kodu karty. Skan wyświetla korzyść; potwierdzenie kasjera zapisuje realizację i symulowany rabat atomowo w naszej bazie.
- Panel pozwala skanować kod kreskowy/QR lub wkleić ten sam kod. Wpisanie kodu jest alternatywą sprzętową, nie obejściem uprawnień.
- Idempotency-Key chroni ponowienia; unikalny paragon chroni zakup, a warunkowa zmiana statusu chroni kupon przed podwójną realizacją.

## Doprecyzowania (2026-11-21)

1. **Stacja i kasa** są przypisane do konta kasjera i odczytuje je backend. Formularz ich nie przesyła ani nie nadpisuje. Unikalność paragonu chroni przed duplikatem, nie przed celowym fałszowaniem pozostałych danych paragonu.
2. **Konta kasjerów** powstają z konfiguracji startowej, analogicznie do administratora. Konta demo są tworzone tylko w Development; seeder nie usuwa ich z baz, do których już trafiły. Bez automatycznego awansu istniejącego zwykłego konta. Zarządzanie personelem przez Admin nie przeczyłoby rozdzieleniu operacji kasowych, ale teraz nie jest potrzebne.
3. **Kod karty:** 128 bitów losowości, zapis jawny z `UNIQUE`, bez danych osobowych. Jawny zapis jest potrzebny, bo właściciel ogląda kod wielokrotnie, a kod tylko identyfikuje konto i niczego nie uwierzytelnia. Wymiana wymaga zalogowanego właściciela i natychmiast unieważnia poprzedni kod. Kodów nie logujemy.
4. **Zakup poniżej 1 zł** (0 pkt) zapisujemy bez wpisu w historii i bez zmiany salda. Idempotencja obowiązuje także dla niego.
5. **Powtórzony paragon** (inny klucz lub brak zapisu klucza) zwraca `409 loyalty.receipt_already_registered` bez informacji o właścicielu. Ten sam klucz idempotencji z tym samym żądaniem odtwarza pierwotny sukces.

6. **Format kodu karty:** Crockford Base32, 26 znaków (16 losowych bajtów). Prezentacja do skanowania lub kopiowania, ręczne wpisanie jako alternatywa.
7. **Odpowiedź dla kasjera:** ID zakupu i przyznane punkty, bez salda klienta.
8. **Wydanie karty** to osobny `POST /api/me/loyalty-card`; `GET` tylko odczytuje. Wydanie i wymiana wymagają Idempotency-Key: ponowienie po utracie odpowiedzi nie może unieważnić właśnie wydanej karty.
9. **Naliczenie salda** to jedno atomowe `INSERT ... ON CONFLICT (UserId) DO UPDATE SET Balance = Balance + EXCLUDED.Balance RETURNING Balance`. Wynik trafia do `BalanceAfter`.
10. **Wyścig zakup - wymiana karty**: obie operacje biorą tę samą blokadę klienta (`CustomerLock`). Zakup po uzyskaniu blokady sprawdza kartę ponownie: wymiana zatwierdzona wcześniej → stary kod odrzucony; zakup wcześniej → naliczenie poprawne.
11. **Po wydaniu lub wymianie** frontend pobiera kartę przez `GET`; odtworzona odpowiedź opisuje historyczną operację.

Model korzyści kuponu (rabat) zostaje na etap Rewards.

## Alternatywy

- Administrator wybierający użytkownika z listy: odrzucone, nie odpowiada procesowi kasowemu i łączy różne uprawnienia.
- Prawdziwa integracja POS: poza obecnym zakresem; wymaga umowy integracyjnej i obsługi awarii pomiędzy systemami.
- Stacja i kasa wybierane w formularzu: odrzucone, zmiana numeru kasy omijałaby unikalność paragonu.
- Hasz kodu karty zamiast zapisu jawnego: odrzucone, właściciel musi widzieć kod przy każdym wejściu, a kod nie jest sekretem uwierzytelniającym.
- Nadawanie Cashier endpointem administratora: niepotrzebne w MVP; konfiguracja startowa wystarcza.

## Konsekwencje

Istniejące reguły punktów i model tankowania pozostają. RegisteredByUserId będzie identyfikować kasjera, a UserId klienta rozpoznanego przez backend. Potrzebne będą model karty, rola Cashier, kontrolowane tworzenie kasjerów oraz testy odmowy dostępu dla User i samego Admin.

Nie deklarujemy transakcji obejmującej zewnętrzną kasę, płatność ani drukarkę fiskalną. Demo pokazuje zastosowanie rabatu w symulatorze. Szczegóły reguł rabatowych, przypisania kuponu do zakupu i przechowywania kodów trzeba określić przed implementacją Rewards.

## Weryfikacja planowana

- Karta kieruje punkty na właściwe konto; drugi klient nie odczytuje cudzej karty.
- User i samo Admin nie wykonują operacji Cashier.
- Kod karty nie realizuje kuponu; nieaktywny kupon nie daje rabatu.
- Sam podgląd nie zużywa kuponu; równoległe potwierdzenia dają tylko jedną realizację.
- Ponowienia zakupu i realizacji są bezpieczne; kody nie trafiają do logów.
