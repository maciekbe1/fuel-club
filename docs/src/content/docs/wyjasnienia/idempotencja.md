---
title: Idempotencja
description: Jak działa Idempotency-Key - rezerwacja, savepoint, odtwarzanie odpowiedzi i równoległe żądania.
sidebar:
  order: 5
---

Ponowienie żądania po utracie odpowiedzi (np. zerwane połączenie) nie może drugi raz naliczyć punktów ani unieważnić właśnie wydanej karty. Klient wysyła `Idempotency-Key` (UUID) i przy ponowieniu używa tego samego klucza. Decyzja: [ADR 0005](/decyzje/0005-idempotencja/).

## Gdzie jest kod

| Element | Ścieżka |
| --- | --- |
| Filtr, oznaczenie endpointu, buforowanie treści, nagłówek w OpenAPI | `apps/api/Infrastructure/Idempotency/Idempotency.cs` |
| Transakcja, savepoint, zapis i odczyt odpowiedzi | `Idempotency/IdempotencyStore.cs` |
| Odcisk żądania | `Idempotency/RequestFingerprint.cs` |
| Tabela | `Idempotency/IdempotencyRecord.cs`, migracja `*_IdempotencyRecords` |
| Kody błędów | `Idempotency/IdempotencyErrorCodes.cs` |
| Frontend | `apps/web/lib/idempotency.ts` (`useIdempotentMutation`) |

Endpoint włącza mechanizm przez `.RequireIdempotency()`:

```csharp
group.MapPost("/loyalty-card", IssueCardEndpoint.Handle).RequireIdempotency();
```

## Przepływ

1. Uwierzytelnianie, autoryzacja i filtry grupy (CSRF) działają **przed** filtrem idempotencji. Zapisana odpowiedź trafia więc tylko do kogoś, kto nadal może wywołać endpoint. Po odebraniu roli powtórzenie dostaje `403`, nie zapisany wynik.
2. Filtr sprawdza nagłówek: brak → `400 idempotency.key_required`, nie-UUID → `400 idempotency.key_invalid`.
3. Filtr liczy odcisk (SHA-256) z metody, szablonu trasy, **wartości** trasy, query i kanonicznej treści JSON. `/coupons/A/activate` i `/coupons/B/activate` mają różne odciski.
4. `IdempotencyStore` otwiera transakcję i wstawia wiersz klucza (`INSERT ... ON CONFLICT DO NOTHING`):
   - wstawiony: klucz zarezerwowany, powstaje savepoint, handler wykonuje operację w tej samej transakcji,
   - istnieje (zatwierdzony): ten sam odcisk → odtworzenie zapisanej odpowiedzi z nagłówkiem `Idempotency-Replayed: true`; inny odcisk → `409 idempotency.key_reused`,
   - trzyma go inne trwające żądanie: `INSERT` czeka na jego koniec, najdłużej `Idempotency:LockTimeout`.
5. Filtr wykonuje wynik handlera do bufora. Te same bajty idą do bazy i do klienta.
6. Zależnie od statusu:

| Wynik | Zmiany operacji | Zapis odpowiedzi | Klucz po wszystkim |
| --- | --- | --- | --- |
| `2xx` | zatwierdzone | tak | zwraca ten wynik |
| `4xx` domenowe (`404`, `409`, `422` ...) | wycofane do savepointu, śledzenie EF wyczyszczone | tak | zwraca ten błąd |
| `400` (walidacja), `408`, `429` | wycofane | nie | wolny - można poprawić żądanie |
| `5xx`, wyjątek | wycofane | nie | wolny - można ponowić |

Przy błędzie domenowym rezerwacja nie jest zwalniana między wycofaniem operacji a zapisem błędu: wiersz klucza powstał przed savepointem, więc rollback do savepointu go nie usuwa.

`400` oznacza odrzucenie żądania przed operacją. Handler idempotentnego endpointu nie może więc zwrócić `400` po zmianie danych; błąd po zmianie to kod domenowy (`409`, `422`).

## Co jest zapisywane

Status, `Content-Type`, `Location` i treść. Nigdy cookies ani nagłówki uwierzytelnienia - tabela nie ma na nie kolumn. Odpowiedź po odtworzeniu może mieć `Set-Cookie`, ale ustawia je bieżące żądanie (np. odświeżenie sesji), nie zapis.

## Odcisk żądania

- Klucze obiektów JSON sortowane, zbędne białe znaki pominięte: inna kolejność pól to to samo żądanie.
- Liczby i napisy w postaci dosłownej: `10` i `10.0` to różne żądania. Wynik: `409`, nie błędne odtworzenie.
- Powtórzona nazwa pola → `400 idempotency.duplicate_json_property`, także gdy różni się tylko wielkością liter (`{"amount":1,"Amount":2}`). Wiązanie DTO ignoruje wielkość liter, więc binder wziąłby jedną z wartości, a odcisk byłby taki sam dla obu kolejności.
- Pola różniące się tylko wielkością liter (`amount` vs `Amount`) w osobnych żądaniach dają różne odciski → `409`, nie błędne odtworzenie.
- Query wchodzi w odcisk w całości (kolejność parametrów bez znaczenia).
- Treść inna niż JSON wchodzi w odcisk bajt po bajcie.

## Równoległe żądania

Dwa żądania z tym samym kluczem: pierwsze rezerwuje, drugie czeka na indeksie unikalnym. Gdy pierwsze zatwierdzi, drugie odczytuje wynik w nowej transakcji i go odtwarza. Gdy pierwsze się wycofa (5xx), drugie wykonuje operację samo.

Czekanie ma limit (`SET LOCAL lock_timeout`, potem przywracany do domyślnego dla operacji; `Idempotency:LockTimeout` od 1 ms do 30 s, inna wartość zatrzymuje start API - `0` w PostgreSQL oznaczałoby czekanie bez końca) i reaguje na anulowanie żądania. Po limicie: `409 idempotency.in_progress` z `Retry-After`. Ten błąd nie jest zapisywany - klient ponawia z tym samym kluczem.

## Rozłączenie klienta

Gdy klient rozłączy się po zmianie domenowej, wynik i tak jest zapisany, a ponowienie go odtwarza:

- przechwycenie odpowiedzi działa z `RequestAborted = CancellationToken.None` (wynik, który respektuje anulowanie, inaczej rzuciłby wyjątek i operacja zostałaby wycofana),
- zapis i commit nie przyjmują anulowania,
- odpowiedź nie jest wysyłana, jeśli klienta już nie ma.

`TypedResults` (JSON) w .NET 10 nie reagują na anulowany token przy zapisie do bufora - sprawdzone. Zabezpieczenie dotyczy wyników strumieniowych i własnych `IResult`.

Rozłączenie **w trakcie** operacji (handler sam przekazuje `RequestAborted` do zapytań) przerywa ją. Część zapisów mogła się już wykonać; rollback transakcji wycofuje wszystkie zmiany w PostgreSQL, nic nie jest zapisywane, a ponowienie z tym samym kluczem wykona operację od nowa.

:::caution[Efekty poza bazą]
Rollback cofa tylko zmiany w PostgreSQL. Efektów zewnętrznych (e-mail, wywołanie innej usługi, plik) nic nie cofa automatycznie. Operacja idempotentna nie powinna ich wykonywać przed commitem; jeśli musi, potrzebny jest osobny mechanizm (np. outbox), nieobjęty tym krokiem.
:::

## Ograniczenia

- Zakres klucza: zalogowany wykonawca. Ten sam klucz u dwóch użytkowników to dwie operacje.
- Brak retencji kluczy w MVP.
- Odebranie roli działa od następnej walidacji sesji (Identity sprawdza znacznik bezpieczeństwa domyślnie co 30 minut). Do tego czasu sesja ma stare role - dotyczy wszystkich endpointów, nie tylko idempotentnych.

## Testy

| Plik | Sprawdza |
| --- | --- |
| `RequestFingerprintTests.cs` | różne ID w trasie, szablon, metoda, query; kolejność kluczy i białe znaki bez znaczenia; wartości i kolejność tablic mają znaczenie; `10` ≠ `10.0`; duplikaty pól na każdym poziomie, także różniące się wielkością liter; zły JSON; treść nie-JSON |
| `IdempotencyOptionsTests.cs` | zakres `LockTimeout` (0, ujemne, < 1 ms, > 30 s odrzucone); API nie startuje ze złą wartością |
| `IdempotencyTests.cs` | odtworzenie statusu, treści, `Location` i `Content-Type` bez ponownego wykonania; inne ID w trasie, query, treść → `409`; przestawione pola → odtworzenie; duplikat pola (także `amount`/`Amount` na endpoincie z typowanym DTO, w obu kolejnościach) → `400` bez zapisu; rozłączenie klienta po zapisie domenowym → wynik zapisany i odtworzony (dla `TypedResults` i dla wyniku respektującego anulowanie); brak i zły klucz; zapis i odtworzenie błędu domenowego; rollback zapisu częściowego; ponowienie po wyjątku i po zwróconym `5xx`; `400` walidacji nie blokuje klucza; 8 równoległych żądań → jedno wykonanie; ten sam klucz innego użytkownika; limit czekania → `409 in_progress` i udane ponowienie; anulowanie czekania; odtworzenie wymaga CSRF; odmowa odtworzenia po odebraniu roli; anonim → `401`; nagłówek w OpenAPI |

Testy używają endpointu dostępnego tylko w hoście testowym (`IdempotencyTestEndpoints`, rejestracja przez `IAdditionalEndpoints`). Host testowy ustawia `Idempotency:LockTimeout` na 1 s i waliduje sesję przy każdym żądaniu.
