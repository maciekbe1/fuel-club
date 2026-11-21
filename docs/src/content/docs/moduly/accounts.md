---
title: Accounts
description: Konta, sesja, role, CSRF i administrator startowy.
sidebar:
  order: 1
---

## Co robi

- rejestracja konta z rolą `User`,
- logowanie i wylogowanie (sesja w ciasteczku HttpOnly),
- profil bieżącego użytkownika z rolami,
- utworzenie ról (`User`, `Admin`, `Cashier`) i administratora startowego przy starcie,
- wspólne, bezpieczne tworzenie kont z konfiguracji (`AccountProvisioning`), używane też dla kasjera i kont demo,
- przykładowy endpoint tylko dla administratora.

Poza zakresem: reset hasła, weryfikacja e-maila, zmiana danych konta.

## Gdzie jest kod

| Warstwa | Ścieżka |
| --- | --- |
| Trasy | `apps/api/Modules/Accounts/AccountsEndpoints.cs`, `Admin/AdminEndpoints.cs` |
| Endpointy | `Register/`, `Login/`, `Logout/`, `Me/` |
| Logika | `Application/IAccountsService.cs` → `Infrastructure/IdentityAccountsService.cs` |
| Start | `Infrastructure/RoleInitializer.cs`, `Infrastructure/AdminAccountProvisioner.cs`, `Infrastructure/AccountProvisioning.cs` |
| Konfiguracja Identity i cookie | `AccountsServiceCollectionExtensions.cs` |
| Kody błędów | `AccountErrorCodes.cs` |
| CSRF (wspólne) | `apps/api/Infrastructure/Csrf/Csrf.cs` |
| Frontend | `apps/web/features/auth/`, `app/(public)/`, `app/(app)/layout.tsx`, `proxy.ts` |

## Przepływ

**Rejestracja**
1. Frontend pobiera token CSRF i wysyła `POST /api/auth/register`.
2. Filtr CSRF sprawdza token.
3. Serwis sprawdza wymagane pola i otwiera transakcję.
4. Identity tworzy konto (hash hasła, reguły hasła, unikalny e-mail), potem nadaje rolę `User`. Commit.
5. `201`. Frontend od razu woła logowanie.

**Logowanie**
1. `POST /api/auth/login` z tokenem CSRF.
2. `PasswordSignInAsync` z blokadą po nieudanych próbach.
3. `204` i ciasteczko sesji. Frontend unieważnia token CSRF i czyści cache.

**Sesja w UI**
1. `proxy.ts`: brak ciasteczka oznacza `/login?next=...`.
2. `(app)/layout.tsx`: `GET /api/auth/me` na serwerze. `401` oznacza `/login`.
3. Dane `me` trafiają do `useMe()` bez ponownego żądania.

**Wylogowanie**: `POST /api/auth/logout`, potem `queryClient.clear()` i przejście na `/login`.

## Dane

`AspNetUsers` (+ `DisplayName`), `AspNetRoles`, `AspNetUserRoles`. Szczegóły: [Baza danych](/referencja/baza-danych/).

Odpowiedź `GET /api/auth/me`:

```json
{ "id": "…", "email": "ania@example.test", "displayName": "Ania", "roles": ["User"] }
```

## Zabezpieczenia

| Zagrożenie | Ochrona |
| --- | --- |
| kradzież sesji przez XSS | cookie HttpOnly |
| CSRF | token w nagłówku, także dla logowania - [CSRF](/wyjasnienia/csrf/) |
| zgadywanie haseł | blokada konta po 5 próbach; ten sam błąd dla złego hasła i nieistniejącego konta |
| słabe hasła | min. 10 znaków, mała i wielka litera, cyfra, znak specjalny |
| samodzielne nadanie roli Admin | rola tylko z konfiguracji; odmowa awansu istniejącego konta |
| niespójny stan konta | rejestracja i provisioning w transakcji |
| open redirect po logowaniu | parametr `next` akceptowany tylko jako ścieżka wewnętrzna |

:::caution[Planowane]
Rate limiting logowania po IP; konta demonstracyjne w Development.
:::

## Testy

`apps/api.tests/AccountsApiTests.cs`, na prawdziwym PostgreSQL:

| Test | Sprawdza |
| --- | --- |
| `Register_returns_field_errors_for_missing_fields` | błędy pól przy pustej rejestracji |
| `Login_rejects_missing_fields_with_field_errors` | błędy pól przy pustym logowaniu |
| `Account_session_supports_login_me_logout_and_rejects_bad_credentials` | pełny cykl sesji i odrzucenie złego hasła |
| `Admin_endpoint_requires_admin_role` | `401` / `403` / `204` dla anonima, użytkownika i admina |
| `Admin_provisioning_refuses_to_promote_an_existing_regular_account` | brak awansu zwykłego konta |
| `Mutating_request_without_csrf_token_is_rejected` | `403 csrf.invalid` bez tokenu |
| `Csrf_token_issued_before_login_is_rejected_after_login` | token związany z tożsamością |

Frontend: logowanie przez formularz jest częścią każdego testu E2E (`apps/web/e2e/fixtures.ts`).

Kasjer i stanowisko kasowe są opisane w module [Loyalty](/moduly/loyalty/#kasjer-i-stanowisko). Testy kasjera i kont demo: `CashierTests.cs`.
