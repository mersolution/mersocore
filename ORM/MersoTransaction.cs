using System;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.Config;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// MersoTransaction - Transaction yönetimi için helper.
    /// Run içinde yapılan tüm model / QueryBuilder / RawQuery işlemleri aynı bağlantı ve transaction'ı kullanır;
    /// hata olursa hepsi geri alınır. İç içe Run çağrıları dıştaki transaction'a katılır.
    /// Farklı veritabanları için iç içe Run (connectionName ile) her veritabanına kendi transaction'ını açar.
    /// <c>attempts</c> verilirse deadlock / serileştirme hatası / bağlantı kopması gibi geçici hatalarda
    /// transaction baştan tekrar çalıştırılır (action yalnızca veritabanı işi yapmalı: e-posta vb. tekrar gider).
    /// Not: transaction bağlantısı paylaşıldığı için Run içinde paralel (Task.WhenAll) veritabanı işi yapmayın.
    /// </summary>
    public static class MersoTransaction
    {
        /// <summary>
        /// Transaction içinde çalıştır (varsayılan / kapsamdaki bağlantı)
        /// </summary>
        public static T Run<T>(Func<T> action)
        {
            return Run(null, action);
        }

        /// <summary>
        /// Transaction içinde çalıştır; geçici hatada en fazla <paramref name="attempts"/> kez dener
        /// </summary>
        public static T Run<T>(Func<T> action, int attempts)
        {
            return Run(null, action, attempts);
        }

        /// <summary>
        /// Transaction içinde çalıştır (isimli bağlantı)
        /// </summary>
        public static T Run<T>(string? connectionName, Func<T> action)
        {
            return Run(connectionName, action, 1);
        }

        /// <summary>
        /// Transaction içinde çalıştır (isimli bağlantı, geçici hatada <paramref name="attempts"/> deneme)
        /// </summary>
        public static T Run<T>(string? connectionName, Func<T> action, int attempts)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            return DbRun.Sync(RunCore(connectionName, () => Task.FromResult(action()), attempts, false, default));
        }

        /// <summary>
        /// Transaction içinde çalıştır (void)
        /// </summary>
        public static void Run(Action action)
        {
            Run(null, action);
        }

        /// <summary>
        /// Transaction içinde çalıştır (void); geçici hatada en fazla <paramref name="attempts"/> kez dener
        /// </summary>
        public static void Run(Action action, int attempts)
        {
            Run(null, action, attempts);
        }

        /// <summary>
        /// Transaction içinde çalıştır (void, isimli bağlantı)
        /// </summary>
        public static void Run(string? connectionName, Action action)
        {
            Run(connectionName, action, 1);
        }

        /// <summary>
        /// Transaction içinde çalıştır (void, isimli bağlantı, geçici hatada <paramref name="attempts"/> deneme)
        /// </summary>
        public static void Run(string? connectionName, Action action, int attempts)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            Run<object?>(connectionName, () =>
            {
                action();
                return null;
            }, attempts);
        }

        /// <summary>
        /// Transaction içinde çalıştır, hata olursa false döndür
        /// </summary>
        public static bool TryRun(Action action)
        {
            try
            {
                Run(action);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Async işlemi transaction içinde çalıştır
        /// </summary>
        public static Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            return RunAsync(null, action);
        }

        /// <summary>
        /// Async işlemi transaction içinde çalıştır; geçici hatada en fazla <paramref name="attempts"/> kez dener
        /// </summary>
        public static Task<T> RunAsync<T>(Func<Task<T>> action, int attempts, CancellationToken cancellationToken = default)
        {
            return RunAsync(null, action, attempts, cancellationToken);
        }

        /// <summary>
        /// Async işlemi transaction içinde çalıştır (isimli bağlantı)
        /// </summary>
        public static Task<T> RunAsync<T>(string? connectionName, Func<Task<T>> action)
        {
            return RunAsync(connectionName, action, 1);
        }

        /// <summary>
        /// Async işlemi transaction içinde çalıştır (isimli bağlantı, geçici hatada <paramref name="attempts"/> deneme)
        /// </summary>
        public static Task<T> RunAsync<T>(string? connectionName, Func<Task<T>> action, int attempts, CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            return RunCore(connectionName, action, attempts, true, cancellationToken);
        }

        /// <summary>
        /// Async işlemi transaction içinde çalıştır (sonuçsuz)
        /// </summary>
        public static Task RunAsync(Func<Task> action)
        {
            return RunAsync(null, action);
        }

        /// <summary>
        /// Async işlemi transaction içinde çalıştır (sonuçsuz); geçici hatada en fazla <paramref name="attempts"/> kez dener
        /// </summary>
        public static Task RunAsync(Func<Task> action, int attempts, CancellationToken cancellationToken = default)
        {
            return RunAsync(null, action, attempts, cancellationToken);
        }

        /// <summary>
        /// Async işlemi transaction içinde çalıştır (sonuçsuz, isimli bağlantı)
        /// </summary>
        public static Task RunAsync(string? connectionName, Func<Task> action)
        {
            return RunAsync(connectionName, action, 1);
        }

        /// <summary>
        /// Async işlemi transaction içinde çalıştır (sonuçsuz, isimli bağlantı, geçici hatada <paramref name="attempts"/> deneme)
        /// </summary>
        public static Task RunAsync(string? connectionName, Func<Task> action, int attempts, CancellationToken cancellationToken = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            return RunCore<object?>(connectionName, async () =>
            {
                await action().ConfigureAwait(false);
                return null;
            }, attempts, true, cancellationToken);
        }

        // A synchronous action (Task.FromResult) with useAsync false completes the returned task synchronously
        internal static async Task<T> RunCore<T>(string? connectionName, Func<Task<T>> action, int attempts = 1,
            bool useAsync = true, CancellationToken cancellationToken = default)
        {
            for (int attempt = 0; ; attempt++)
            {
                Exception failure;
                var outer = AmbientTransaction.Current;
                using (var db = ModelBase.CreateCommand(connectionName))
                {
                    // Nested Run on the same database: join the outer transaction (the outer Run decides about retries)
                    if (AmbientTransaction.Find(db) != null)
                        return await action().ConfigureAwait(false);

                    bool committing = false;
                    try
                    {
                        db.BeginTransaction();
                        AmbientTransaction.Current = db.CreateAmbientTransaction();
                        try
                        {
                            var result = await action().ConfigureAwait(false);
                            AmbientTransaction.Current = outer;
                            committing = true;
                            db.CommitTransaction();
                            return result;
                        }
                        catch
                        {
                            AmbientTransaction.Current = outer;
                            if (!committing)
                                SafeRollback(db);
                            throw;
                        }
                        finally
                        {
                            AmbientTransaction.Current = outer;
                        }
                    }
                    catch (Exception ex) when (attempt + 1 < attempts && MersoRetry.CanRerunTransaction(ex, committing))
                    {
                        failure = ex;
                    }
                }

                if (useAsync)
                    await MersoRetry.WaitAsync(failure, attempt, cancellationToken).ConfigureAwait(false);
                else
                    MersoRetry.Wait(failure, attempt);
            }
        }

        private static void SafeRollback(DbCommandBase db)
        {
            try
            {
                db.RollbackTransaction();
            }
            catch
            {
                // Keep the original exception (a dropped connection has already rolled back)
            }
        }
    }
}
