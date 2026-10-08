using System;
using Microsoft.Extensions.DependencyInjection;

namespace DotNative.LocalDatabase;

public static class LocalDatabaseServiceProviderExtensions
{
#if NET10_0_OR_GREATER
    extension(IServiceProvider services)
    {
        /// <summary>Resolves the registered plugin using the provider's DI lifetime.</summary>
        public ILocalDatabase LocalDatabase => services.GetRequiredService<ILocalDatabase>();
    }
#else
    /// <summary>Resolves the registered plugin using the provider's DI lifetime.</summary>
    public static ILocalDatabase LocalDatabase(this IServiceProvider services) =>
        services.GetRequiredService<ILocalDatabase>();
#endif
}
