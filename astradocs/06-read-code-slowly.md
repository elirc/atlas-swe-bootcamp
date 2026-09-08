# Read C# a few pieces at a time

**One objective:** translate a short real handler into ordinary sentences. You do not need to know every C# feature before doing this.

## Start with a familiar line

```csharp
var country = await db.Countries.FindAsync(code.ToUpperInvariant());
```

| Piece | Read it as |
| --- | --- |
| `code` | The text supplied in the route |
| `.ToUpperInvariant()` | Produce its uppercase form |
| `db.Countries` | Access country entities through this database context |
| `.FindAsync(...)` | Find the entity with that key |
| `await` | Await the operation's result before using it here |
| `var country =` | Store that result in a local variable whose type the compiler infers |
| `;` | End this statement |

Read the whole line again: “Find the country matching the uppercase code, await the result, and call it `country`.” `var` does not mean the value has no type.

## Expand the compact return

Actual code:

```csharp
return country is null ? Results.NotFound() : Results.Ok(ToResponse(country));
```

The same decision written more slowly:

```csharp
if (country is null)
{
    return Results.NotFound();
}

return Results.Ok(ToResponse(country));
```

This second block is an explanatory expansion, not another handler you need to add. The `? :` expression selects between two results.

## Read an entity method

From [EmploymentContract.cs](../src/Atlas.Domain/Entities/EmploymentContract.cs):

```csharp
public void Activate(DateTimeOffset nowUtc)
{
    if (Status != ContractStatus.Draft)
    {
        throw new DomainException($"Only draft contracts can be activated; this contract is {Status}.");
    }

    Status = ContractStatus.Active;
    ActivatedAtUtc = nowUtc;
}
```

`public` means callers can access this method. `void` means it returns no value. `nowUtc` is an input. `!=` means “is not equal to.” `throw` stops the normal path with an exception. The `$` string embeds the current status in the message.

Notice what is absent: no HTTP status code, no database context, and no save. The method changes this object's state. The endpoint and persistence code do the other work.

## Say it again with a blank

“If the contract is not __, throw. Otherwise set status to __ and record __.” Fill this without looking, then compare with the method.

## When one symbol blocks you

Use [words and symbols](14-words-and-symbols.md). Choose one unfamiliar symbol, explain it on the current line, and continue. You can return for the rest later.

Checkpoint: explain why calling `Activate` alone does not send HTTP 200 or persist a row. Next: [memory and storage](07-memory-and-storage.md).
