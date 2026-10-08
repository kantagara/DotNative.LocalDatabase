using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNative.LocalDatabase;

public sealed record DatabaseStatement(
    string Sql,
    IReadOnlyDictionary<string, object?>? Parameters = null
);

public interface ILocalDatabase : IAsyncDisposable
{
    Task<int> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default
    );
    Task<object?> ScalarAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<int>> ExecuteBatchAsync(
        IReadOnlyList<DatabaseStatement> statements,
        CancellationToken cancellationToken = default
    );
}

public sealed class SqliteLocalDatabase : ILocalDatabase
{
    private readonly SqliteConnection connection;

    private readonly SemaphoreSlim gate = new(1, 1);

    private bool disposed;

    public SqliteLocalDatabase(string filePath, PresentationTarget? target = null)
    {
        PlatformGuard.Desktop(target ?? PresentationTarget.Local);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var full = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = full,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString()
        );
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
    }

    public Task<int> ExecuteAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default
    ) => WithCommand(sql, parameters, c => c.ExecuteNonQuery(), cancellationToken);

    public Task<object?> ScalarAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default
    ) =>
        WithCommand(
            sql,
            parameters,
            c =>
            {
                var x = c.ExecuteScalar();
                return x is DBNull ? null : x;
            },
            cancellationToken
        );

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default
    ) =>
        WithCommand(
            sql,
            parameters,
            c =>
            {
                using var reader = c.ExecuteReader();
                var rows = new List<IReadOnlyDictionary<string, object?>>();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object?>(
                        reader.FieldCount,
                        StringComparer.Ordinal
                    );
                    for (var i = 0; i < reader.FieldCount; ++i)
                        row.Add(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
                    rows.Add(row);
                }
                return (IReadOnlyList<IReadOnlyDictionary<string, object?>>)rows;
            },
            cancellationToken
        );

    public async Task<IReadOnlyList<int>> ExecuteBatchAsync(
        IReadOnlyList<DatabaseStatement> statements,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(statements);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await Task.Run(
                    () =>
                    {
                        using var transaction = connection.BeginTransaction();
                        var results = new int[statements.Count];
                        for (var i = 0; i < statements.Count; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            using var cmd = connection.CreateCommand();
                            cmd.Transaction = transaction;
                            cmd.CommandText = statements[i].Sql;
                            Add(cmd, statements[i].Parameters);
                            results[i] = cmd.ExecuteNonQuery();
                        }
                        transaction.Commit();
                        return (IReadOnlyList<int>)results;
                    },
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<T> WithCommand<T>(
        string sql,
        IReadOnlyDictionary<string, object?>? args,
        Func<SqliteCommand, T> action,
        CancellationToken token
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            Add(cmd, args);
            token.ThrowIfCancellationRequested();
            return await Task.Run(() => action(cmd), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static void Add(SqliteCommand cmd, IReadOnlyDictionary<string, object?>? args)
    {
        if (args is null)
            return;
        foreach (var (key, value) in args)
            cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!disposed)
            {
                disposed = true;
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }
}

public static class LocalDatabaseServices
{
    public static IServiceCollection AddLocalDatabase(
        this IServiceCollection services,
        string databasePath
    )
    {
        services.TryAddSingleton<ILocalDatabase>(p => new SqliteLocalDatabase(
            databasePath,
            p.GetService<PresentationTarget>()
        ));
        return services;
    }
}
