---
title: Kasjer i konta demo
description: Konto kasjera ze stanowiskiem oraz konta demonstracyjne w Development.
sidebar:
  order: 3
---

## Kasjer startowy

Kasjer powstaje przy starcie API z konfiguracji, analogicznie do [administratora](/instrukcje/administrator/):

```sh
dotnet user-secrets set "BootstrapCashier:Email" "kasjer@example.test" --project apps/api
dotnet user-secrets set "BootstrapCashier:Password" "<silne-haslo>" --project apps/api
dotnet user-secrets set "BootstrapCashier:StationCode" "ST-001" --project apps/api
dotnet user-secrets set "BootstrapCashier:RegisterCode" "K1" --project apps/api
```

| Sytuacja | Wynik |
| --- | --- |
| Wszystkie cztery wartości puste | nic się nie dzieje |
| Ustawiona część wartości | API nie startuje - błąd konfiguracji |
| Konto nie istnieje | konto z rolą `Cashier` i stanowiskiem, w jednej transakcji |
| Konto istnieje z rolą `Cashier` | stanowisko aktualizowane do konfiguracji |
| Konto istnieje bez roli `Cashier` (np. `User` albo samo `Admin`) | API nie startuje - odmowa awansu |
| Stanowisko z niedozwolonymi znakami | API nie startuje, konto nie powstaje |

Kody stanowiska są normalizowane (przycięcie, wielkie litery) i muszą pasować do `A-Z 0-9 -`.

## Panel kasjera

Po zalogowaniu kontem kasjera na ekranie głównym jest „Kasa” (`/kasa`). Kod karty klienta można wkleić, wpisać albo zeskanować kamerą (QR z ekranu „Karta” klienta). Demo na jednym komputerze: dwa okna (np. zwykłe i prywatne), w oknie klienta „Kopiuj kod”, w oknie kasjera wklej.

### Kamera na telefonie (HTTPS)

Przeglądarka udostępnia kamerę tylko na `https://` albo `localhost`. Telefon w sieci lokalnej łączy się po IP, więc potrzebny jest HTTPS:

```sh
cd apps/web
npx next dev --experimental-https -H 0.0.0.0
```

Na telefonie otwórz `https://<IP-komputera>:3000` i zaakceptuj ostrzeżenie o certyfikacie (samopodpisany, generowany przez Next). API zostaje na `http://localhost:5271` - telefon go nie widzi, bo `/api/*` przechodzi przez serwer Next.

## Konta demo

Konta demonstracyjne są **opcjonalne** i włączasz je w user-secrets:

```sh
dotnet user-secrets set "DemoAccounts:Enabled" "true" --project apps/api
dotnet user-secrets set "DemoAccounts:Password" "<haslo-demo>" --project apps/api
```

Hasło musi spełniać reguły rejestracji. `Enabled=true` bez hasła zatrzymuje start API.

| E-mail | Rola | Uwagi |
| --- | --- | --- |
| `anna@demo.fuelclub.test` | `User` | klient 1 |
| `bartek@demo.fuelclub.test` | `User` | klient 2 - testy izolacji |
| `kasjer@demo.fuelclub.test` | `Cashier` | stanowisko `DEMO-01` / `K1` |
| `admin@demo.fuelclub.test` | `Admin` | bez uprawnień kasjera |

Tworzenie jest idempotentne. Jeśli któryś e-mail demo istnieje bez oczekiwanej roli, API nie wystartuje.

### Hasło istniejących kont

Seeder ustawia hasło tylko przy **tworzeniu** konta. Zmiana `DemoAccounts:Password` nie zmienia hasła kont, które już istnieją.

Żeby ustawić istniejącym kontom demo hasło z `DemoAccounts:Password`, uruchom jednorazową komendę:

```sh
dotnet run --project apps/api -- reset-demo-passwords
```

- Działa tylko w środowisku Development (profil uruchomieniowy `http`). W innym środowisku kończy się kodem 1.
- Zmienia hasła przez Identity (token resetu), zachowując ID kont, role i stanowisko kasjera.
- Nie tworzy ani nie usuwa kont. Brakujące konto pomija z ostrzeżeniem.
- Wszystkie zmiany w jednej transakcji: hasło odrzucone przez reguły Identity nie zmienia żadnego konta.
- Hasła nie wypisuje w logach. Nie uruchamia serwera ani zadań startowych i nigdy nie działa automatycznie przy starcie API.
- Reset zmienia znacznik bezpieczeństwa konta, więc istniejące sesje tych kont wygasną (Identity sprawdza go domyślnie co 30 minut).

Usuwanie kont demo nie jest dobrym sposobem na zmianę hasła: konto kasjera ma wiersz w `CashierStations` z `ON DELETE RESTRICT`, więc `DELETE` z `AspNetUsers` zostanie zablokowany. Do tego straciłbyś ID kont i powiązane dane.

:::caution[Środowisko nie chroni istniejących kont]
Poza środowiskiem Development seeder **nie tworzy** kont demo. Nie usuwa ich jednak ani nie blokuje. Konta demo, które już są w bazie, działają w każdym środowisku i można się na nie zalogować, także w Production. Zdarzy się to np. po skopiowaniu bazy dev albo po jednorazowym uruchomieniu w Development na bazie docelowej.

Dlatego:
- nigdy nie używaj bazy dev jako bazy docelowej,
- przed wdrożeniem sprawdź, że w bazie nie ma kont `@demo.fuelclub.test`:
  `SELECT "Email" FROM "AspNetUsers" WHERE "Email" LIKE '%@demo.fuelclub.test';`
:::

Testy integracyjne wyłączają konta demo i ignorują `BootstrapAdmin` / `BootstrapCashier` z user-secrets (`ApiFactory`).
