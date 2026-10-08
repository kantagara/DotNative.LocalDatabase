# DotNative.LocalDatabase

Desktop SQLite based on `Microsoft.Data.Sqlite` and its bundled SQLitePCLRaw native assets. NuGet selects the native library at restore/publish; no hand-installed SQLite binary is required. This package currently implements macOS, Windows and Linux. Android/iOS need platform binding validation before support can be claimed.

```csharp
builder.Services.AddPaths("com.example.myapp");
builder.Services.AddLocalDatabase(Path.Combine(paths.Get(PathKind.ApplicationData), "app.sqlite"));
var db = provider.LocalDatabase;
await db.ExecuteAsync("CREATE TABLE IF NOT EXISTS item(id INTEGER PRIMARY KEY, name TEXT)", cancellationToken: cancellationToken);
```

`ExecuteAsync`, `ScalarAsync`, parameterized `QueryAsync` and transactional `ExecuteBatchAsync` are serialized per database instance. Batches roll back on the first failing statement. Queries return primitive SQLite values keyed by column name. Cancellation is checked before execution and between batch statements; a SQLite statement already running finishes before its result is delivered. Dispose the singleton with the application. WAL and foreign keys are enabled. The file is ordinary SQLite and is not encrypted.

Build locally: `dotnet build -p:DotNativeSourceRoot=../dotNative`. CRUD, query, and rollback validation passed on macOS, Windows 11 ARM64, and Linux ARM64 (Debian 12 container).

## Service access

Import `DotNative.LocalDatabase` to access the plugin through `IServiceProvider`:

```csharp
using DotNative.LocalDatabase;

var plugin = services.LocalDatabase;
```

The getter calls `GetRequiredService<ILocalDatabase>()` on every access, preserving
DI lifetimes and the usual missing-registration error. Register the plugin with
`AddLocalDatabase(...)` before building the provider.

A `net10.0` application uses the property syntax with C# 14 or later. A
`net9.0` application uses only the method equivalent:

```csharp
var plugin = services.LocalDatabase();
```

The package contains separate `net9.0` and `net10.0` assemblies. NuGet selects
the assembly matching the application target framework. `NET10_0_OR_GREATER`
selects the property; the `#else` branch selects the method.

Build and pack both targets with .NET 10 SDK. A source build using .NET 9 SDK
builds only `net9.0`; it does not produce the .NET 10 assembly.
