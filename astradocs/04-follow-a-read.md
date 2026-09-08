# Follow one read: GET a country

**One objective:** explain a request that reads data without creating or changing a country.

Our recurring example is `GET /api/countries/PH` with the development admin key. Start the local server using [page 00](00-first-20-minutes.md).

## Version 1: the five questions

| Question | This request |
| --- | --- |
| Who asks? | An authenticated API user |
| Where does it go? | The `/{code}` GET handler in `CountryEndpoints` |
| What gets checked? | Authentication; then whether the country exists |
| What gets read? | The country identified by the uppercase code |
| What comes back? | 200 and a country response, or 404 if absent |

## Version 2: read the actual handler

From [CountryEndpoints.cs](../src/Atlas.Api/Endpoints/CountryEndpoints.cs):

```csharp
group.MapGet("/{code}", async (string code, AtlasDbContext db) =>
{
    var country = await db.Countries.FindAsync(code.ToUpperInvariant());
    return country is null ? Results.NotFound() : Results.Ok(ToResponse(country));
});
```

The group supplies `/api/countries`; `/{code}` supplies the last piece. The URL value becomes the `code` parameter. The framework supplies `db` from dependency injection; the HTTP caller does not send a DbContext.

`ToUpperInvariant()` makes `ph` into `PH`. `FindAsync` looks up the entity by its key; it can reuse an already tracked entity, otherwise it queries the database. With no matching country, the result is null.

`ToResponse` selects the values that form the public response. `Results.Ok` means HTTP 200. There is no `SaveChangesAsync` in this handler.

## Version 3: act it out

You hand a clerk the code `ph`. The clerk normalizes it to `PH`, finds that entry in a catalog, and copies the public details onto a reply. If the entry is absent, the reply says it was not found. The catalog is not edited.

The analogy describes only the country handler. The complete request also passes through authentication and logging.

## Now compare the list route

`GET /api/countries?page=1&pageSize=2` selects a different handler: `group.MapGet("/", ...)`. It validates paging, sorts by code, and calls `ToPageAsync`.

In [Pagination.cs](../src/Atlas.Api/Pagination.cs), `ToPageAsync` counts matching records and then fetches a page. That is more than one database query. The array is in the body; `X-Total-Count`, `X-Page`, and `X-Page-Size` are headers. A detail lookup does not use this paging helper.

## Checkpoint

Predict the result of looking up `ph` versus `PH`. Explain why `/api/countries?page=0` returns 400 with a valid key. Find the exact return statement that prevents its list query from running.

Say it again: **read the request, locate the handler, follow the query, inspect the reply.** Next: [follow a write](05-follow-a-write.md).
