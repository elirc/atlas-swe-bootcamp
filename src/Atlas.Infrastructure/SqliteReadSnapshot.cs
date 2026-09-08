using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atlas.Infrastructure;

/// <summary>A consistent deferred reader. With WAL, another connection can still renew a job lease.</summary>
public sealed class SqliteReadSnapshot : IAsyncDisposable
{
    private readonly AtlasDbContext db;
    private readonly SqliteTransaction transaction;
    private readonly IDbContextTransaction enlisted;
    private bool disposed;

    private SqliteReadSnapshot(AtlasDbContext db, SqliteTransaction transaction, IDbContextTransaction enlisted)
    { this.db = db; this.transaction = transaction; this.enlisted = enlisted; }

    public static async Task<SqliteReadSnapshot> BeginAsync(AtlasDbContext db, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null) throw new InvalidOperationException("The read snapshot must own its transaction.");
        await db.Database.OpenConnectionAsync(ct);
        SqliteTransaction? transaction = null;
        try
        {
            // The default immediate transaction would reserve SQLite's writer lock.
            // This phase must only read; the final fenced transaction owns all writes.
            transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
            var enlisted = await db.Database.UseTransactionAsync(transaction, ct)
                ?? throw new InvalidOperationException("The read transaction could not be enlisted.");
            return new(db, transaction, enlisted);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { await enlisted.DisposeAsync(); }
        finally
        {
            // UseTransaction does not own the supplied native transaction.
            try { await transaction.DisposeAsync(); }
            finally { await db.Database.CloseConnectionAsync(); }
        }
    }
}
