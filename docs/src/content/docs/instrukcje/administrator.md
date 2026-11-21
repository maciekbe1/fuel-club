---
title: Utworzenie administratora
description: Konto z rolą Admin z konfiguracji przy starcie API.
sidebar:
  order: 2
---

Rejestracja tworzy wyłącznie konta z rolą `User`. Administratora tworzy `AdminAccountProvisioner` przy starcie API, na podstawie konfiguracji.

## Kroki

```sh
dotnet user-secrets set "BootstrapAdmin:Email" "admin@example.test" --project apps/api
dotnet user-secrets set "BootstrapAdmin:Password" "<silne-haslo>" --project apps/api
```

Na serwerze użyj zmiennych środowiskowych `BootstrapAdmin__Email` i `BootstrapAdmin__Password`.

Uruchom ponownie API. Konto o nazwie wyświetlanej „Administrator” powstanie z rolą `Admin`.

## Zachowanie

| Sytuacja | Wynik |
| --- | --- |
| Obie wartości puste | nic się nie dzieje |
| Ustawiona tylko jedna | API nie startuje - błąd konfiguracji |
| Konto nie istnieje | tworzy konto i nadaje rolę `Admin` w jednej transakcji |
| Konto istnieje i ma rolę `Admin` | nic się nie dzieje |
| Konto istnieje bez roli `Admin` | API nie startuje - odmowa awansu zwykłego konta |

Ostatni przypadek blokuje przejęcie uprawnień: ktoś mógłby zarejestrować adres z konfiguracji, zanim administrator powstanie.

Hasło musi spełniać te same reguły co przy rejestracji. Inaczej API nie wystartuje i poda kody błędów Identity.

Kasjer i konta demo: [Konta personelu i demo](/instrukcje/kasjer-i-konta-demo/).
