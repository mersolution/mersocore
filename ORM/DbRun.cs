using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// One code path for sync and async ORM calls: with <c>useAsync = false</c> every method runs the
    /// synchronous ADO.NET call and returns an already completed task, so <see cref="Sync{T}"/> never blocks
    /// on pending I/O (no sync-over-async deadlock).
    /// </summary>
    internal static class DbRun
    {
        internal static T Sync<T>(Task<T> completed) => completed.GetAwaiter().GetResult();

        internal static void Sync(Task completed) => completed.GetAwaiter().GetResult();

        internal static void Bind(DbCommandBase db, IEnumerable<KeyValuePair<string, object?>>? parameters)
        {
            if (parameters == null)
                return;

            foreach (var p in parameters)
                db.ParametersAdd(p.Key, p.Value);
        }

        /// <summary>
        /// Models of a query, filled straight from the data reader. <paramref name="create"/> makes a new model
        /// per row; <paramref name="each"/> sees the filled model with its row (e.g. to read key columns).
        /// </summary>
        internal static Task<List<TModel>> Models<TModel>(DbCommandBase db, string sql, ModelMetadata metadata, Func<TModel> create,
            bool useAsync, CancellationToken ct, Action<IDataRecord, ModelMapper.ReaderMap, TModel>? each = null) where TModel : class
        {
            if (useAsync)
            {
                return db.RunReaderAsync(sql, async reader =>
                {
                    var list = new List<TModel>();
                    ModelMapper.ReaderMap? map = null;
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        list.Add(Fill(reader, ref map, metadata, create, each));
                    return list;
                }, ct);
            }

            return Task.FromResult(db.RunReader(sql, reader =>
            {
                var list = new List<TModel>();
                ModelMapper.ReaderMap? map = null;
                while (reader.Read())
                    list.Add(Fill(reader, ref map, metadata, create, each));
                return list;
            }));
        }

        private static TModel Fill<TModel>(IDataRecord record, ref ModelMapper.ReaderMap? map, ModelMetadata metadata, Func<TModel> create,
            Action<IDataRecord, ModelMapper.ReaderMap, TModel>? each) where TModel : class
        {
            map = map ?? new ModelMapper.ReaderMap(record, metadata);
            var model = create();
            map.Fill(record, model);
            each?.Invoke(record, map, model);
            return model;
        }

        /// <summary>
        /// First column of every row (DBNull → null)
        /// </summary>
        internal static Task<List<object?>> Column(DbCommandBase db, string sql, bool useAsync, CancellationToken ct)
        {
            if (useAsync)
            {
                return db.RunReaderAsync(sql, async reader =>
                {
                    var list = new List<object?>();
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        list.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
                    return list;
                }, ct);
            }

            return Task.FromResult(db.RunReader(sql, reader =>
            {
                var list = new List<object?>();
                while (reader.Read())
                    list.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
                return list;
            }));
        }

        internal static async Task<DataTable> Table(DbCommandBase db, string sql, bool useAsync, CancellationToken ct)
        {
            return useAsync
                ? await db.RunDataTableAsync(sql, ct).ConfigureAwait(false)
                : db.RunDataTable(sql);
        }

        internal static async Task<int> NonQuery(DbCommandBase db, string sql, bool useAsync, CancellationToken ct)
        {
            return useAsync
                ? await db.RunNonQueryAsync(sql, ct).ConfigureAwait(false)
                : db.RunNonQuery(sql);
        }

        internal static async Task<object?> Scalar(DbCommandBase db, string sql, bool useAsync, CancellationToken ct)
        {
            var result = useAsync
                ? await db.RunToObjectScalerAsync(sql, ct).ConfigureAwait(false)
                : db.RunToObjectScaler(sql);
            return result == DBNull.Value ? null : result;
        }

        internal static async Task<string> String(DbCommandBase db, string sql, bool useAsync, CancellationToken ct)
        {
            return useAsync
                ? await db.RunToStringScalerAsync(sql, ct).ConfigureAwait(false)
                : db.RunToStringScaler(sql);
        }

        internal static async Task<int> Int32(DbCommandBase db, string sql, bool useAsync, CancellationToken ct)
        {
            var result = await Scalar(db, sql, useAsync, ct).ConfigureAwait(false);
            return result == null ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        internal static async Task<decimal> Decimal(DbCommandBase db, string sql, bool useAsync, CancellationToken ct)
        {
            var result = await Scalar(db, sql, useAsync, ct).ConfigureAwait(false);
            return result == null ? 0 : Convert.ToDecimal(result, CultureInfo.InvariantCulture);
        }

        internal static async Task<long> InsertGetId(DbCommandBase db, string sql, string primaryKeyColumn, bool useAsync, CancellationToken ct)
        {
            return useAsync
                ? await db.RunInsertGetIdAsync(sql, primaryKeyColumn, ct).ConfigureAwait(false)
                : db.RunInsertGetId(sql, primaryKeyColumn);
        }
    }
}
