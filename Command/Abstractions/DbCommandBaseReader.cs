using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace mersolutionCore.Command.Abstractions
{
    /// <summary>
    /// Data reader access without a DataTable copy (models are filled straight from the reader)
    /// </summary>
    public abstract partial class DbCommandBase
    {
        /// <summary>
        /// Run a query and hand the open reader to <paramref name="read"/>. Retried, logged and enlisted in the
        /// ambient transaction like <see cref="RunDataTable"/>; the reader and connection are closed afterwards.
        /// </summary>
        public virtual T RunReader<T>(string sql, Func<DbDataReader, T> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));

            return WithRetry(sql, false, () =>
            {
                try
                {
                    PrepareCommand(sql);
                    using (var reader = Command!.ExecuteReader())
                    {
                        return read(reader);
                    }
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            });
        }

        /// <summary>
        /// Async counterpart of <see cref="RunReader{T}"/> (read rows with <c>await reader.ReadAsync(ct)</c>)
        /// </summary>
        public virtual Task<T> RunReaderAsync<T>(string sql, Func<DbDataReader, Task<T>> read, CancellationToken cancellationToken = default)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));

            return WithRetryAsync(sql, false, async () =>
            {
                try
                {
                    await PrepareCommandAsync(sql, cancellationToken).ConfigureAwait(false);
                    using (var reader = await Command!.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return await read(reader).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
                finally
                {
                    CloseConnection();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Rows one by one while the caller iterates: the same reader positioned on the next row (do not keep it).
        /// Opening is retried; the reader and connection close when the loop ends or breaks. Inside a
        /// transaction run no other query on the same connection before the loop ends.
        /// </summary>
        public virtual IEnumerable<DbDataReader> StreamReader(string sql)
        {
            var reader = WithRetry(sql, false, () => OpenReader(sql));
            try
            {
                while (reader.Read())
                    yield return reader;
            }
            finally
            {
                reader.Dispose();
                CloseConnection();
            }
        }

        /// <summary>
        /// Async counterpart of <see cref="StreamReader"/>
        /// </summary>
        public virtual async IAsyncEnumerable<DbDataReader> StreamReaderAsync(string sql, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var reader = await WithRetryAsync(sql, false, () => OpenReaderAsync(sql, cancellationToken), cancellationToken).ConfigureAwait(false);
            try
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    yield return reader;
            }
            finally
            {
                reader.Dispose();
                CloseConnection();
            }
        }

        private DbDataReader OpenReader(string sql)
        {
            try
            {
                PrepareCommand(sql);
                return Command!.ExecuteReader();
            }
            catch (Exception ex) when (LogFailure(ex))
            {
                throw;
            }
            catch
            {
                CloseConnection();
                throw;
            }
        }

        private async Task<DbDataReader> OpenReaderAsync(string sql, CancellationToken cancellationToken)
        {
            try
            {
                await PrepareCommandAsync(sql, cancellationToken).ConfigureAwait(false);
                return await Command!.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (LogFailure(ex))
            {
                throw;
            }
            catch
            {
                CloseConnection();
                throw;
            }
        }
    }
}
