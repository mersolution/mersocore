using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Raw SQL Query desteği. connectionName boşsa: modelin [Connection] bağlantısı, yoksa kapsamdaki / varsayılan bağlantı.
    /// </summary>
    public static class RawQuery
    {
        /// <summary>
        /// Raw SQL çalıştır ve model listesi döndür
        /// </summary>
        public static List<T> Query<T>(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null) where T : Model<T>, new()
        {
            return DbRun.Sync(QueryCore<T>(sql, parameters, connectionName, false, default));
        }

        /// <summary>
        /// Raw SQL çalıştır ve model listesi döndür (async)
        /// </summary>
        public static Task<List<T>> QueryAsync<T>(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null, CancellationToken cancellationToken = default) where T : Model<T>, new()
        {
            return QueryCore<T>(sql, parameters, connectionName, true, cancellationToken);
        }

        private static async Task<List<T>> QueryCore<T>(string sql, Dictionary<string, object?>? parameters, string? connectionName, bool useAsync, CancellationToken ct) where T : Model<T>, new()
        {
            var metadata = Model<T>.GetMetadata<T>();

            using (var db = ModelBase.CreateCommand(connectionName ?? metadata.ConnectionName))
            {
                AddParameters(db, parameters);
                return await DbRun.Models(db, sql, metadata, () => new T { LoadedConnection = connectionName }, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Raw SQL çalıştır ve tek model döndür
        /// </summary>
        public static T? QueryFirst<T>(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null) where T : Model<T>, new()
        {
            return Query<T>(sql, parameters, connectionName).FirstOrDefault();
        }

        /// <summary>
        /// Raw SQL çalıştır ve tek model döndür (async)
        /// </summary>
        public static async Task<T?> QueryFirstAsync<T>(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null, CancellationToken cancellationToken = default) where T : Model<T>, new()
        {
            return (await QueryAsync<T>(sql, parameters, connectionName, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }

        /// <summary>
        /// Raw SQL çalıştır (INSERT, UPDATE, DELETE) — etkilenen satır sayısını döndürür
        /// </summary>
        public static int Execute(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null)
        {
            return DbRun.Sync(ExecuteCore(sql, parameters, connectionName, false, default));
        }

        /// <summary>
        /// Raw SQL çalıştır (async) — etkilenen satır sayısını döndürür
        /// </summary>
        public static Task<int> ExecuteAsync(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null, CancellationToken cancellationToken = default)
        {
            return ExecuteCore(sql, parameters, connectionName, true, cancellationToken);
        }

        private static async Task<int> ExecuteCore(string sql, Dictionary<string, object?>? parameters, string? connectionName, bool useAsync, CancellationToken ct)
        {
            using (var db = ModelBase.CreateCommand(connectionName))
            {
                AddParameters(db, parameters);
                return await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Raw SQL çalıştır ve scalar değer döndür (T nullable olabilir: int?, DateTime? ...)
        /// </summary>
        public static T? Scalar<T>(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null)
        {
            return DbRun.Sync(ScalarCore<T>(sql, parameters, connectionName, false, default));
        }

        /// <summary>
        /// Raw SQL çalıştır ve scalar değer döndür (async)
        /// </summary>
        public static Task<T?> ScalarAsync<T>(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null, CancellationToken cancellationToken = default)
        {
            return ScalarCore<T>(sql, parameters, connectionName, true, cancellationToken);
        }

        private static async Task<T?> ScalarCore<T>(string sql, Dictionary<string, object?>? parameters, string? connectionName, bool useAsync, CancellationToken ct)
        {
            using (var db = ModelBase.CreateCommand(connectionName))
            {
                AddParameters(db, parameters);

                var result = await DbRun.Scalar(db, sql, useAsync, ct).ConfigureAwait(false);
                if (result == null)
                    return default;

                return (T?)ModelMapper.ConvertTo(result, typeof(T));
            }
        }

        /// <summary>
        /// Raw SQL çalıştır ve DataTable döndür
        /// </summary>
        public static DataTable QueryTable(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null)
        {
            return DbRun.Sync(QueryTableCore(sql, parameters, connectionName, false, default));
        }

        /// <summary>
        /// Raw SQL çalıştır ve DataTable döndür (async)
        /// </summary>
        public static Task<DataTable> QueryTableAsync(string sql, Dictionary<string, object?>? parameters = null, string? connectionName = null, CancellationToken cancellationToken = default)
        {
            return QueryTableCore(sql, parameters, connectionName, true, cancellationToken);
        }

        private static async Task<DataTable> QueryTableCore(string sql, Dictionary<string, object?>? parameters, string? connectionName, bool useAsync, CancellationToken ct)
        {
            using (var db = ModelBase.CreateCommand(connectionName))
            {
                AddParameters(db, parameters);
                return await DbRun.Table(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        private static void AddParameters(DbCommandBase db, Dictionary<string, object?>? parameters)
        {
            if (parameters == null)
                return;

            foreach (var param in parameters)
            {
                db.ParametersAdd(param.Key.StartsWith("@") ? param.Key : $"@{param.Key}", param.Value);
            }
        }
    }

    /// <summary>
    /// Model extension for raw queries
    /// </summary>
    public static class RawQueryExtensions
    {
        /// <summary>
        /// Raw SQL sorgusu çalıştır
        /// </summary>
        public static List<T> Raw<T>(string sql, Dictionary<string, object?>? parameters = null) where T : Model<T>, new()
        {
            return RawQuery.Query<T>(sql, parameters);
        }
    }
}
