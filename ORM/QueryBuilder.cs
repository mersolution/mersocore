using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Fluent Query Builder for ORM models
    /// </summary>
    /// <typeparam name="T">Model type</typeparam>
    public class QueryBuilder<T> where T : Model<T>, new()
    {
        private readonly ModelMetadata _metadata;
        private readonly List<string> _selectColumns = new List<string>();
        private readonly List<WhereClause> _whereClauses = new List<WhereClause>();
        private readonly List<string> _orderByClauses = new List<string>();
        private readonly List<string> _joinClauses = new List<string>();
        private string _groupBy;
        private string _having;
        private int? _limit;
        private int? _offset;
        private bool _distinct;
        private bool _withTrashed;

        public QueryBuilder()
        {
            _metadata = Model<T>.GetMetadata<T>();
        }

        #region Select Methods

        /// <summary>
        /// Select specific columns
        /// </summary>
        public QueryBuilder<T> Select(params string[] columns)
        {
            _selectColumns.AddRange(columns);
            return this;
        }

        /// <summary>
        /// Select distinct
        /// </summary>
        public QueryBuilder<T> Distinct()
        {
            _distinct = true;
            return this;
        }

        #endregion

        #region Where Methods

        /// <summary>
        /// Add WHERE clause
        /// </summary>
        public QueryBuilder<T> Where(string column, string op, object value)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = op, Value = value, Logic = "AND" });
            return this;
        }

        /// <summary>
        /// Add WHERE clause (equals)
        /// </summary>
        public QueryBuilder<T> Where(string column, object value)
        {
            return Where(column, "=", value);
        }

        /// <summary>
        /// Add OR WHERE clause
        /// </summary>
        public QueryBuilder<T> OrWhere(string column, string op, object value)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = op, Value = value, Logic = "OR" });
            return this;
        }

        /// <summary>
        /// Add WHERE IN clause
        /// </summary>
        public QueryBuilder<T> WhereIn(string column, IEnumerable<object> values)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "IN", Value = values, Logic = "AND", IsIn = true });
            return this;
        }

        /// <summary>
        /// Add WHERE NOT IN clause
        /// </summary>
        public QueryBuilder<T> WhereNotIn(string column, IEnumerable<object> values)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "NOT IN", Value = values, Logic = "AND", IsIn = true });
            return this;
        }

        /// <summary>
        /// Add WHERE BETWEEN clause
        /// </summary>
        public QueryBuilder<T> WhereBetween(string column, object min, object max)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "BETWEEN", Value = new[] { min, max }, Logic = "AND", IsBetween = true });
            return this;
        }

        /// <summary>
        /// Add WHERE NULL clause
        /// </summary>
        public QueryBuilder<T> WhereNull(string column)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "IS NULL", Value = null, Logic = "AND", IsNull = true });
            return this;
        }

        /// <summary>
        /// Add WHERE NOT NULL clause
        /// </summary>
        public QueryBuilder<T> WhereNotNull(string column)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "IS NOT NULL", Value = null, Logic = "AND", IsNull = true });
            return this;
        }

        /// <summary>
        /// Add WHERE LIKE clause
        /// </summary>
        public QueryBuilder<T> WhereLike(string column, string pattern)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "LIKE", Value = $"%{pattern}%", Logic = "AND" });
            return this;
        }

        /// <summary>
        /// Add WHERE date clause
        /// </summary>
        public QueryBuilder<T> WhereDate(string column, DateTime date)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "DATE", Value = date.Date, Logic = "AND", IsDate = true });
            return this;
        }

        /// <summary>
        /// Add raw WHERE clause
        /// </summary>
        public QueryBuilder<T> WhereRaw(string sql)
        {
            _whereClauses.Add(new WhereClause { RawSql = sql, Logic = "AND", IsRaw = true });
            return this;
        }

        /// <summary>
        /// Include soft deleted records
        /// </summary>
        public QueryBuilder<T> WithTrashed()
        {
            _withTrashed = true;
            return this;
        }

        #endregion

        #region Join Methods

        /// <summary>
        /// Add INNER JOIN
        /// </summary>
        public QueryBuilder<T> Join(string table, string condition)
        {
            _joinClauses.Add($"INNER JOIN {table} ON {condition}");
            return this;
        }

        /// <summary>
        /// Add LEFT JOIN
        /// </summary>
        public QueryBuilder<T> LeftJoin(string table, string condition)
        {
            _joinClauses.Add($"LEFT JOIN {table} ON {condition}");
            return this;
        }

        /// <summary>
        /// Add RIGHT JOIN
        /// </summary>
        public QueryBuilder<T> RightJoin(string table, string condition)
        {
            _joinClauses.Add($"RIGHT JOIN {table} ON {condition}");
            return this;
        }

        #endregion

        #region Order & Group Methods

        /// <summary>
        /// Add ORDER BY clause
        /// </summary>
        public QueryBuilder<T> OrderBy(string column, string direction = "ASC")
        {
            _orderByClauses.Add($"{column} {direction.ToUpper()}");
            return this;
        }

        /// <summary>
        /// Add ORDER BY DESC
        /// </summary>
        public QueryBuilder<T> OrderByDesc(string column)
        {
            return OrderBy(column, "DESC");
        }

        /// <summary>
        /// Add GROUP BY clause
        /// </summary>
        public QueryBuilder<T> GroupBy(string column)
        {
            _groupBy = column;
            return this;
        }

        /// <summary>
        /// Add HAVING clause
        /// </summary>
        public QueryBuilder<T> Having(string column, string op, object value)
        {
            _having = $"{column} {op} '{value}'";
            return this;
        }

        /// <summary>
        /// Set LIMIT
        /// </summary>
        public QueryBuilder<T> Limit(int count)
        {
            _limit = count;
            return this;
        }

        /// <summary>
        /// Set OFFSET
        /// </summary>
        public QueryBuilder<T> Offset(int count)
        {
            _offset = count;
            return this;
        }

        /// <summary>
        /// Pagination helper
        /// </summary>
        public QueryBuilder<T> Page(int pageNumber, int pageSize = 10)
        {
            _limit = pageSize;
            _offset = (pageNumber - 1) * pageSize;
            return this;
        }

        /// <summary>
        /// Alias for Limit
        /// </summary>
        public QueryBuilder<T> Take(int count)
        {
            return Limit(count);
        }

        /// <summary>
        /// Alias for Offset
        /// </summary>
        public QueryBuilder<T> Skip(int count)
        {
            return Offset(count);
        }

        /// <summary>
        /// Order by latest (created_at DESC)
        /// </summary>
        public QueryBuilder<T> Latest(string column = "CreatedAt")
        {
            return OrderByDesc(column);
        }

        /// <summary>
        /// Order by oldest (created_at ASC)
        /// </summary>
        public QueryBuilder<T> Oldest(string column = "CreatedAt")
        {
            return OrderBy(column, "ASC");
        }

        /// <summary>
        /// Random order
        /// </summary>
        public QueryBuilder<T> InRandomOrder()
        {
            _orderByClauses.Clear();
            _orderByClauses.Add("NEWID()");
            return this;
        }

        #endregion

        #region Execute Methods

        /// <summary>
        /// Get all results
        /// </summary>
        public List<T> Get()
        {
            var (sql, parameters) = BuildSelectQuery();

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                var dt = db.RunDataTable(sql);
                return MapToModels(dt);
            }
        }

        /// <summary>
        /// Get first result
        /// </summary>
        public T First()
        {
            _limit = 1;
            var results = Get();
            return results.FirstOrDefault();
        }

        /// <summary>
        /// Get first result or throw exception
        /// </summary>
        public T FirstOrFail()
        {
            var result = First();
            if (result == null)
                throw new InvalidOperationException("No record found");
            return result;
        }

        /// <summary>
        /// Get single column value
        /// </summary>
        public object Value(string column)
        {
            _selectColumns.Clear();
            _selectColumns.Add(column);
            _limit = 1;

            var (sql, parameters) = BuildSelectQuery();

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                return db.RunToStringScaler(sql);
            }
        }

        /// <summary>
        /// Get single column as list
        /// </summary>
        public List<object> Pluck(string column)
        {
            _selectColumns.Clear();
            _selectColumns.Add(column);

            var (sql, parameters) = BuildSelectQuery();

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                var dt = db.RunDataTable(sql);
                return dt.AsEnumerable().Select(r => r[column]).ToList();
            }
        }

        /// <summary>
        /// Check if any records exist
        /// </summary>
        public bool Exists()
        {
            return Count() > 0;
        }

        /// <summary>
        /// Check if no records exist
        /// </summary>
        public bool DoesntExist()
        {
            return Count() == 0;
        }

        /// <summary>
        /// Count records
        /// </summary>
        public int Count(string column = "*")
        {
            _selectColumns.Clear();
            _selectColumns.Add($"COUNT({column})");

            var (sql, parameters) = BuildSelectQuery();

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                return db.RunToInt32Scaler(sql);
            }
        }

        /// <summary>
        /// Sum column values
        /// </summary>
        public decimal Sum(string column)
        {
            _selectColumns.Clear();
            _selectColumns.Add($"SUM({column})");

            var (sql, parameters) = BuildSelectQuery();

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                return db.RunToDecimalScaler(sql);
            }
        }

        /// <summary>
        /// Average column values
        /// </summary>
        public decimal Avg(string column)
        {
            _selectColumns.Clear();
            _selectColumns.Add($"AVG({column})");

            var (sql, parameters) = BuildSelectQuery();

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                return db.RunToDecimalScaler(sql);
            }
        }

        /// <summary>
        /// Max column value
        /// </summary>
        public object Max(string column)
        {
            _selectColumns.Clear();
            _selectColumns.Add($"MAX({column})");

            var (sql, parameters) = BuildSelectQuery();

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                return db.RunToStringScaler(sql);
            }
        }

        /// <summary>
        /// Min column value
        /// </summary>
        public object Min(string column)
        {
            _selectColumns.Clear();
            _selectColumns.Add($"MIN({column})");

            var (sql, parameters) = BuildSelectQuery();

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                return db.RunToStringScaler(sql);
            }
        }

        /// <summary>
        /// Update records
        /// </summary>
        public int Update(Dictionary<string, object> data)
        {
            var (whereSql, parameters) = BuildWhereClause();

            var setClauses = new List<string>();
            foreach (var kvp in data)
            {
                var paramName = $"@set_{kvp.Key}";
                setClauses.Add($"{kvp.Key} = {paramName}");
                parameters[paramName] = kvp.Value;
            }

            var sql = $"UPDATE {_metadata.TableName} SET {string.Join(", ", setClauses)}";
            if (!string.IsNullOrEmpty(whereSql))
                sql += $" WHERE {whereSql}";

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                db.RunExecute(sql);
                return 1; // Affected rows not available in base implementation
            }
        }

        /// <summary>
        /// Delete records
        /// </summary>
        public int Delete()
        {
            var (whereSql, parameters) = BuildWhereClause();

            string sql;
            if (_metadata.HasSoftDelete && !_withTrashed)
            {
                sql = $"UPDATE {_metadata.TableName} SET {_metadata.SoftDeleteColumn} = @deleted_at";
                parameters["@deleted_at"] = DateTime.UtcNow;
            }
            else
            {
                sql = $"DELETE FROM {_metadata.TableName}";
            }

            if (!string.IsNullOrEmpty(whereSql))
                sql += $" WHERE {whereSql}";

            using (var db = ModelBase.ConnectionFactory())
            {
                foreach (var param in parameters)
                {
                    db.ParametersAdd(param.Key, param.Value);
                }

                db.RunExecute(sql);
                return 1;
            }
        }

        /// <summary>
        /// Increment column value
        /// </summary>
        public int Increment(string column, int amount = 1)
        {
            return Update(new Dictionary<string, object> { { column, new RawValue($"{column} + {amount}") } });
        }

        /// <summary>
        /// Decrement column value
        /// </summary>
        public int Decrement(string column, int amount = 1)
        {
            return Update(new Dictionary<string, object> { { column, new RawValue($"{column} - {amount}") } });
        }

        /// <summary>
        /// Get the generated SQL
        /// </summary>
        public string ToSql()
        {
            var (sql, _) = BuildSelectQuery();
            return sql;
        }

        /// <summary>
        /// Get paginated results
        /// </summary>
        public PaginatedResult<T> Paginate(int pageNumber = 1, int pageSize = 10)
        {
            var totalCount = Count();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            _limit = pageSize;
            _offset = (pageNumber - 1) * pageSize;

            // Need to reset select columns after Count()
            _selectColumns.Clear();

            var items = Get();

            return new PaginatedResult<T>
            {
                Items = items,
                CurrentPage = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPreviousPage = pageNumber > 1,
                HasNextPage = pageNumber < totalPages
            };
        }

        /// <summary>
        /// Process results in chunks
        /// </summary>
        public void Chunk(int chunkSize, Action<List<T>> callback)
        {
            int page = 1;
            List<T> results;

            do
            {
                _limit = chunkSize;
                _offset = (page - 1) * chunkSize;
                _selectColumns.Clear();

                results = Get();

                if (results.Count > 0)
                {
                    callback(results);
                }

                page++;
            } while (results.Count == chunkSize);
        }

        /// <summary>
        /// Process results in chunks with ability to stop
        /// </summary>
        public void ChunkById(int chunkSize, Action<List<T>> callback, string column = "Id")
        {
            object lastId = 0;
            List<T> results;

            do
            {
                _whereClauses.RemoveAll(w => w.Column == column && w.Operator == ">");
                Where(column, ">", lastId);
                OrderBy(column);
                _limit = chunkSize;
                _selectColumns.Clear();

                results = Get();

                if (results.Count > 0)
                {
                    callback(results);
                    var lastItem = results.Last();
                    var prop = _metadata.Properties.FirstOrDefault(p => p.ColumnName == column);
                    if (prop != null)
                    {
                        lastId = prop.PropertyInfo.GetValue(lastItem);
                    }
                }
            } while (results.Count == chunkSize);
        }

        #endregion

        #region Private Methods

        private (string sql, Dictionary<string, object> parameters) BuildSelectQuery()
        {
            var parameters = new Dictionary<string, object>();
            var sb = new StringBuilder();

            // SELECT
            sb.Append("SELECT ");
            if (_distinct) sb.Append("DISTINCT ");

            if (_selectColumns.Count > 0)
                sb.Append(string.Join(", ", _selectColumns));
            else
                sb.Append("*");

            // FROM
            sb.Append($" FROM {_metadata.TableName}");

            // JOINS
            foreach (var join in _joinClauses)
            {
                sb.Append($" {join}");
            }

            // WHERE
            var (whereSql, whereParams) = BuildWhereClause();
            if (!string.IsNullOrEmpty(whereSql))
            {
                sb.Append($" WHERE {whereSql}");
                foreach (var p in whereParams)
                    parameters[p.Key] = p.Value;
            }

            // GROUP BY
            if (!string.IsNullOrEmpty(_groupBy))
                sb.Append($" GROUP BY {_groupBy}");

            // HAVING
            if (!string.IsNullOrEmpty(_having))
                sb.Append($" HAVING {_having}");

            // ORDER BY
            if (_orderByClauses.Count > 0)
                sb.Append($" ORDER BY {string.Join(", ", _orderByClauses)}");

            // LIMIT & OFFSET
            if (_limit.HasValue)
            {
                if (_offset.HasValue && _orderByClauses.Count > 0)
                {
                    sb.Append($" OFFSET {_offset.Value} ROWS FETCH NEXT {_limit.Value} ROWS ONLY");
                }
                else
                {
                    // Simple TOP for SQL Server without ORDER BY
                    var selectIndex = sb.ToString().IndexOf("SELECT ", StringComparison.OrdinalIgnoreCase);
                    if (selectIndex >= 0)
                    {
                        var insertPos = selectIndex + 7;
                        if (_distinct) insertPos += 9;
                        sb.Insert(insertPos, $"TOP {_limit.Value} ");
                    }
                }
            }

            return (sb.ToString(), parameters);
        }

        private (string sql, Dictionary<string, object> parameters) BuildWhereClause()
        {
            var parameters = new Dictionary<string, object>();
            var clauses = new List<string>();
            int paramIndex = 0;

            // Add soft delete filter
            if (_metadata.HasSoftDelete && !_withTrashed)
            {
                clauses.Add($"{_metadata.SoftDeleteColumn} IS NULL");
            }

            foreach (var where in _whereClauses)
            {
                string clause;

                if (where.IsRaw)
                {
                    clause = where.RawSql;
                }
                else if (where.IsNull)
                {
                    clause = $"{where.Column} {where.Operator}";
                }
                else if (where.IsIn)
                {
                    var values = (IEnumerable<object>)where.Value;
                    var paramNames = new List<string>();
                    foreach (var val in values)
                    {
                        var paramName = $"@p{paramIndex++}";
                        paramNames.Add(paramName);
                        parameters[paramName] = val;
                    }
                    clause = $"{where.Column} {where.Operator} ({string.Join(", ", paramNames)})";
                }
                else if (where.IsBetween)
                {
                    var vals = (object[])where.Value;
                    var p1 = $"@p{paramIndex++}";
                    var p2 = $"@p{paramIndex++}";
                    parameters[p1] = vals[0];
                    parameters[p2] = vals[1];
                    clause = $"{where.Column} BETWEEN {p1} AND {p2}";
                }
                else if (where.IsDate)
                {
                    var paramName = $"@p{paramIndex++}";
                    parameters[paramName] = where.Value;
                    clause = $"CAST({where.Column} AS DATE) = {paramName}";
                }
                else
                {
                    var paramName = $"@p{paramIndex++}";
                    parameters[paramName] = where.Value;
                    clause = $"{where.Column} {where.Operator} {paramName}";
                }

                if (clauses.Count > 0 || (_metadata.HasSoftDelete && !_withTrashed))
                    clauses.Add($"{where.Logic} {clause}");
                else
                    clauses.Add(clause);
            }

            return (string.Join(" ", clauses), parameters);
        }

        private List<T> MapToModels(DataTable dt)
        {
            var list = new List<T>();
            foreach (DataRow row in dt.Rows)
            {
                var model = new T();
                foreach (var prop in _metadata.Properties)
                {
                    if (dt.Columns.Contains(prop.ColumnName))
                    {
                        var value = row[prop.ColumnName];
                        if (value != DBNull.Value)
                        {
                            try
                            {
                                var targetType = Nullable.GetUnderlyingType(prop.PropertyInfo.PropertyType) ?? prop.PropertyInfo.PropertyType;
                                var convertedValue = Convert.ChangeType(value, targetType);
                                prop.PropertyInfo.SetValue(model, convertedValue);
                            }
                            catch { }
                        }
                    }
                }
                list.Add(model);
            }
            return list;
        }

        #endregion
    }

    internal class WhereClause
    {
        public string Column { get; set; }
        public string Operator { get; set; }
        public object Value { get; set; }
        public string Logic { get; set; }
        public string RawSql { get; set; }
        public bool IsRaw { get; set; }
        public bool IsNull { get; set; }
        public bool IsIn { get; set; }
        public bool IsBetween { get; set; }
        public bool IsDate { get; set; }
    }

    /// <summary>
    /// Represents a raw SQL value (not parameterized)
    /// </summary>
    public class RawValue
    {
        public string Value { get; }
        public RawValue(string value) => Value = value;
        public override string ToString() => Value;
    }

    /// <summary>
    /// Paginated result container
    /// </summary>
    public class PaginatedResult<T>
    {
        public List<T> Items { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }

        public int FirstItem => (CurrentPage - 1) * PageSize + 1;
        public int LastItem => Math.Min(CurrentPage * PageSize, TotalCount);
    }
}
