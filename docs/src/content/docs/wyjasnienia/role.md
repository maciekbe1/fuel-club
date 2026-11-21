---
title: Role i autoryzacja
description: Role User, Cashier i Admin oraz gdzie zapadają decyzje o dostępie.
sidebar:
  order: 3
---

## Role

| Rola | Kto | Skąd |
| --- | --- | --- |
| `User` | każde zarejestrowane konto | nadawana przy rejestracji |
| `Cashier` | obsługa symulatora kasy na przypisanym stanowisku | z konfiguracji `BootstrapCashier` lub z kont demo (tworzonych tylko w Development, ale działających wszędzie, gdzie trafiły do bazy) |
| `Admin` | zarządzanie ofertami i konfiguracją; bez automatycznych uprawnień kasjera | z konfiguracji `BootstrapAdmin` lub z kont demo (jw.) |

Role tworzy `RoleInitializer` przy starcie API. Nazwy są w `RoleNames`.

Nie ma endpointu, który nadaje role `Cashier` ani `Admin`. Rejestracja nie przyjmuje roli w żądaniu. Tworzenie kont z konfiguracji (`AccountProvisioning`) nigdy nie awansuje istniejącego konta: jeśli e-mail istnieje bez oczekiwanej roli, API nie wystartuje.

## Gdzie zapada decyzja

Endpoint deklaruje wymaganie:

```csharp
.RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));
```

- brak sesji → `401`
- sesja bez roli → `403`

Frontend dostaje role z `GET /api/auth/me` i używa ich tylko do wyglądu, np. ukrycia menu administratora.

## Właściciel danych

Rola nie wystarcza. Dla danych użytkownika (portfel, kupony) API bierze tożsamość z sesji, nie z URL ani treści żądania. Dostęp do cudzego zasobu kończy się bez ujawnienia jego danych.

:::caution[Planowane]
Endpointy Loyalty i Rewards wymagające kontroli właściciela są planowane; model danych Loyalty już istnieje.
:::

## Kasjer

- Konto kasjera ma tylko rolę `Cashier` oraz przypisane stanowisko (stacja + kasa) w `CashierStations`.
- Grupa tras `/api/cashier` wymaga roli `Cashier`. `Admin` jej nie dziedziczy: konto z samą rolą `Admin` dostaje `403`. Uprawnienia można łączyć tylko jawnie.
- Stanowisko odczytuje backend z sesji kasjera. Formularz kasy go nie przesyła ani nie nadpisuje.
- Kod karty klienta nie zastępuje sesji kasjera. Kasjer nie dostaje ogólnego wglądu w konta i historię klientów; po zakupie widzi tylko ID zakupu i przyznane punkty.

:::caution[Planowane]
Operacje kasowe: zapis zakupu po skanie karty, podgląd i realizacja kuponu. Dziś grupa `/api/cashier` ma tylko `GET /api/cashier/station`.
:::
