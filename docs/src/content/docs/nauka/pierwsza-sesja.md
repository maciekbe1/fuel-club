---
title: Pierwsza sesja - od rejestracji do wylogowania
description: Przejdź pełny cykl konta w interfejsie i w API.
sidebar:
  order: 1
---

Przejdziesz cały cykl konta: rejestrację, logowanie, odczyt profilu i wylogowanie. Najpierw w przeglądarce, potem tymi samymi żądaniami w terminalu, żeby zobaczyć, co dzieje się pod spodem.

**Wymagania:** działająca aplikacja według [Uruchomienie lokalne](/instrukcje/uruchomienie-lokalne/).

## 1. Rejestracja w przeglądarce

1. Otwórz `http://localhost:3000`. Bez sesji zostaniesz przekierowany na `/login`.
2. Kliknij **Załóż konto**.
3. Wpisz imię, e-mail i hasło. Lista pod polem hasła odhacza spełnione wymagania.
4. Kliknij **Załóż konto**. Konto powstaje, frontend od razu Cię loguje i przenosi na `/`.

W nagłówku widzisz swoje imię i przycisk **Wyloguj**.

## 2. Wylogowanie

Kliknij **Wyloguj**. Wracasz na `/login`, a cache danych w przeglądarce zostaje wyczyszczony.

## 3. To samo przez API

Zobacz te same kroki jako żądania HTTP. Wszystkie idą przez frontend (`:3000`), tak jak w przeglądarce.

```sh
JAR=$(mktemp)
BASE=http://localhost:3000

csrf() { curl -s -c "$JAR" -b "$JAR" "$BASE/api/auth/csrf" | sed 's/.*"token":"\([^"]*\)".*/\1/'; }

# rejestracja
curl -s -o /dev/null -w "%{http_code}\n" -c "$JAR" -b "$JAR" \
  -H "X-CSRF-TOKEN: $(csrf)" -H 'Content-Type: application/json' \
  -d '{"email":"ania@example.test","password":"Bardzo-Dobre1!","displayName":"Ania"}' \
  "$BASE/api/auth/register"            # 201

# logowanie
curl -s -o /dev/null -w "%{http_code}\n" -c "$JAR" -b "$JAR" \
  -H "X-CSRF-TOKEN: $(csrf)" -H 'Content-Type: application/json' \
  -d '{"email":"ania@example.test","password":"Bardzo-Dobre1!"}' \
  "$BASE/api/auth/login"               # 204 + cookie sesji

# profil
curl -s -b "$JAR" "$BASE/api/auth/me"  # {"id":"...","email":"...","displayName":"Ania","roles":["User"]}

# wylogowanie - nowy token, bo poprzedni był wydany przed logowaniem
curl -s -o /dev/null -w "%{http_code}\n" -c "$JAR" -b "$JAR" \
  -H "X-CSRF-TOKEN: $(csrf)" -X POST "$BASE/api/auth/logout"   # 204

curl -s -o /dev/null -w "%{http_code}\n" -b "$JAR" "$BASE/api/auth/me"  # 401
```

## Co tu zaszło

- Każdy `POST` wymaga nagłówka `X-CSRF-TOKEN`. Bez niego dostaniesz `403` z kodem `csrf.invalid`.
- Token CSRF jest związany z tożsamością. Ten sprzed logowania nie zadziała po zalogowaniu, dlatego przed wylogowaniem pobieramy nowy.
- Sesję trzyma ciasteczko HttpOnly. JavaScript go nie odczyta, a przeglądarka wysyła je sama.

Szczegóły: [Sesja i cookies](/wyjasnienia/sesja-i-cookies/), [CSRF](/wyjasnienia/csrf/).
