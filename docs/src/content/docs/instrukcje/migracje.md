---
title: Migracje bazy danych
description: Dodawanie i stosowanie migracji EF Core.
sidebar:
  order: 4
---

Migracje to osobny, jawny krok. API ich nie stosuje. Przy starcie `DatabaseSchemaVerifier` porównuje migracje w kodzie z historią w tabeli `__EFMigrationsHistory`. Jeśli są oczekujące migracje, API kończy się błędem:

```text
Database schema is out of date. Pending migrations: <nazwy>.
Apply them with 'dotnet ef database update --project apps/api' before starting the API.
```

:::note[Co ta kontrola wykrywa, a czego nie]
Kontrola sprawdza **historię migracji**, nie rzeczywisty schemat. Nie wykryje między innymi:

- ręcznych zmian w bazie (usunięta kolumna, indeks albo ograniczenie, zmieniony typ),
- wpisu w `__EFMigrationsHistory` dla migracji, która nie została w pełni zastosowana,
- bazy z migracją nowszą niż kod (np. po wycofaniu wersji aplikacji).

Zmiany schematu robimy wyłącznie przez migracje. Wtedy historia odpowiada schematowi.
:::

Dlaczego tak: [ADR 0003](/decyzje/0003-migracje/).

`dotnet-ef` jest narzędziem lokalnym repozytorium (`dotnet-tools.json`):

```sh
dotnet tool restore
```

## Lokalnie

```sh
dotnet ef database update --project apps/api
```

Connection string pochodzi z user-secrets API. Inną bazę wskażesz przez `--connection "<connection string>"`.

## Nowa migracja

```sh
dotnet ef migrations add <Nazwa> --project apps/api
dotnet ef database update --project apps/api
```

Pliki trafiają do `apps/api/Migrations/`. Przejrzyj wygenerowany kod przed commitem, zwłaszcza ograniczenia `UNIQUE` i `CHECK`.

## Testy

`ApiFactory` migruje bazę `fuelclub_test` przed startem hosta testowego. Nie trzeba nic robić ręcznie.

## Wdrożenie

Budujemy samodzielny plik wykonywalny z migracjami i uruchamiamy go przed startem nowej wersji API:

```sh
dotnet ef migrations bundle --project apps/api -o efbundle
./efbundle --connection "$MIGRATIONS_CONNECTION"
```

- `MIGRATIONS_CONNECTION` to użytkownik bazy z prawami do zmiany schematu. API łączy się użytkownikiem bez tych praw.
- Przed migracją wykonaj kopię bazy.
- Nieudana migracja zatrzymuje wdrożenie nowej wersji API.

:::caution[Poprzednia wersja API nie zawsze przetrwa nieudaną migrację]
To, czy poprzednia wersja dalej działa poprawnie, zależy od dwóch rzeczy:

1. **Transakcji.** PostgreSQL obsługuje transakcyjne DDL, a EF Core zwykle stosuje migrację w transakcji, więc błąd ją wycofuje. Ale migracje zastosowane wcześniej w tym samym uruchomieniu mogą już być zatwierdzone. Operacje poza transakcją (np. `suppressTransaction`, `CREATE INDEX CONCURRENTLY`) nie zostaną wycofane.
2. **Zgodności wstecznej.** Nawet udana albo częściowo zastosowana migracja może zepsuć starą wersję API, np. przez usunięcie lub zmianę nazwy kolumny, której ta wersja używa.

Dlatego zmiany niezgodne wstecz rozbijamy na etapy (expand/contract): najpierw dodajemy nowe elementy, potem wdrażamy kod, który ich używa, a stare usuwamy w osobnej, późniejszej migracji.
:::

:::caution[Planowane]
Konfiguracja na Railway (krok przed startem, osobni użytkownicy bazy) powstanie przy pierwszym wdrożeniu.
:::
