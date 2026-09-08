using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure;

public static class DatabaseConflicts
{
    // Unique constraints protect first writers; a Version token protects later writers.
    // Foreign-key/check failures are deliberately not translated into a duplicate conflict.
    public static bool IsWriteConflict(Exception error) => error is DbUpdateConcurrencyException
        || error is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: 1555 or 2067 or 517 } }
        || error is SqliteException { SqliteExtendedErrorCode: 517 };
}
