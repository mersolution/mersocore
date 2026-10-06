using System;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.Config;
using mersolutionCore.ORM;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

// Same namespace as AddLogging / AddDbContext so the extension shows up without an extra using
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Dependency injection registration for mersolutionCore
    /// <code>
    /// builder.Services.AddStackExchangeRedisCache(o =&gt; o.Configuration = "localhost:6379");
    /// builder.Services.AddMersoCore(o =&gt; o
    ///     .UsePostgreSql(builder.Configuration.GetConnectionString("Main"))
    ///     .AddConnection("reports", reportsCs, DbProviderType.MySQL)
    ///     .UseDistributedCache()
    ///     .LogSql());
    /// builder.Services.AddMersoDbContext&lt;AppDbContext&gt;();
    /// </code>
    /// </summary>
    public static class MersoServiceCollectionExtensions
    {
        /// <summary>
        /// Configure mersolutionCore. Databases and retry settings apply immediately; IDistributedCache and
        /// ILogger are wired when the host starts (or call <see cref="UseMersoCore"/> without a host).
        /// Registers <see cref="MersoCoreOptions"/> and a transient <see cref="DbCommandBase"/> for the default database.
        /// </summary>
        public static IServiceCollection AddMersoCore(this IServiceCollection services, Action<MersoCoreOptions> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var options = new MersoCoreOptions();
            configure(options);
            options.ApplyStatic();

            services.AddSingleton(options);
            services.TryAddTransient<DbCommandBase>(_ => DbConfig.CreateConnection());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MersoCoreHostedService>());
            return services;
        }

        /// <summary>
        /// Register a DbContext (scoped). Its constructor must be resolvable by the container, e.g. a
        /// parameterless one calling <c>base(DbConfig.CreateConnection)</c>.
        /// </summary>
        public static IServiceCollection AddMersoDbContext<TContext>(this IServiceCollection services) where TContext : DbContext
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            services.TryAddScoped<TContext>();
            return services;
        }

        /// <summary>
        /// Wire IDistributedCache / ILogger now (console apps or tests that build a ServiceProvider without a host)
        /// </summary>
        public static IServiceProvider UseMersoCore(this IServiceProvider services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            var options = services.GetService<MersoCoreOptions>()
                ?? throw new InvalidOperationException("Call services.AddMersoCore(...) first.");
            MersoCoreRuntime.Start(options, services);
            return services;
        }

        private sealed class MersoCoreHostedService : IHostedService
        {
            private readonly IServiceProvider _services;

            public MersoCoreHostedService(IServiceProvider services)
            {
                _services = services;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _services.UseMersoCore();
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
