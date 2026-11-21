---
title: Generowanie typów API
description: Typy TypeScript z dokumentu OpenAPI.
sidebar:
  order: 5
---

Frontend nie ma ręcznie pisanych typów DTO. Generuje je z OpenAPI.

1. Uruchom API (`dotnet run --project apps/api --launch-profile http`).
2. Wygeneruj typy:

   ```sh
   cd apps/web
   npm run api:types
   ```

3. Zacommituj `apps/web/lib/api/schema.d.ts` razem ze zmianą w API.

Inny adres dokumentu: `OPENAPI_URL=http://host/openapi/v1.json npm run api:types`.

## Gdy typ odpowiedzi jest pusty

Endpoint zwraca `IResult` albo obiekt anonimowy, więc OpenAPI nie zna schematu. Zwracaj `TypedResults` z nazwanym rekordem:

```csharp
public static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> Handle(...)
```

albo zadeklaruj odpowiedź przez `.Produces<T>()` / `.ProducesProblem()` przy mapowaniu.
