using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// A query that can be embedded in another one (WHERE EXISTS / IN subquery, UNION part)
    /// </summary>
    internal interface ISubQuery
    {
        /// <summary>Full SELECT (own ORDER BY / LIMIT kept); <paramref name="forIn"/>: default column = primary key</summary>
        string BuildSubquery(DbProviderType provider, bool forIn, ref int paramIndex, Dictionary<string, object?> parameters);

        /// <summary>SELECT without ORDER BY / LIMIT (UNION part)</summary>
        string BuildUnionPart(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters);

        /// <summary>Alias of the table while the query is a correlated subquery (null otherwise)</summary>
        string? Alias { get; set; }

        /// <summary>Table of the query's model</summary>
        string TableName { get; }

        /// <summary>WHERE conditions only (with soft delete; global scopes when asked), no "WHERE"</summary>
        string BuildConditions(DbProviderType provider, bool withGlobalScopes, ref int paramIndex, Dictionary<string, object?> parameters);

        /// <summary>" ORDER BY ..." or an empty string</summary>
        string BuildOrderBy(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters);

        /// <summary>AND column op value</summary>
        void AddComparison(string column, string op, object? value);
    }

    /// <summary>
    /// Fluent Query Builder for ORM models. SQL (paging, random order, date functions, identifier quoting)
    /// is generated for the provider of the model's connection: SQL Server, MySQL, MariaDB, PostgreSQL or SQLite.
    /// Global scopes (<see cref="GlobalScopeAttribute"/>) and the soft delete filter are applied automatically.
    /// Raw SQL pieces (WhereRaw, SelectRaw, OrderByRaw, HavingRaw) take values as <c>?</c> placeholders.
    /// </summary>
    /// <typeparam name="T">Model type</typeparam>
    public partial class QueryBuilder<T> : ISubQuery where T : Model<T>, new()
    {
        private static readonly HashSet<string> AllowedOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "=", "!=", "<>", "<", ">", "<=", ">=", "LIKE", "NOT LIKE", "ILIKE", "NOT ILIKE"
        };

        private static readonly HashSet<string> ColumnOperators = new HashSet<string>(StringComparer.Ordinal)
        {
            "=", "!=", "<>", "<", ">", "<=", ">="
        };

        private readonly ModelMetadata _metadata;
        private readonly List<SelectItem> _selectColumns = new List<SelectItem>();
        private readonly List<WhereClause> _whereClauses = new List<WhereClause>();
        private readonly List<OrderItem> _orders = new List<OrderItem>();
        private readonly List<JoinItem> _joins = new List<JoinItem>();
        private readonly List<UnionItem> _unions = new List<UnionItem>();
        private string? _groupBy;
        private HavingClause? _having;
        private int? _limit;
        private int? _offset;
        private bool _distinct;
        private bool _withTrashed;
        private bool _onlyTrashed;
        private bool _withoutGlobalScopes;
        private string? _connectionName;
        private RowLock _lock;
        private string? _alias;

        private enum RowLock { None, Update, Share }

        public QueryBuilder()
        {
            _metadata = Model<T>.GetMetadata<T>();
        }

        #region Connection

        /// <summary>
        /// Run this query on a named connection; loaded models are saved back to it
        /// </summary>
        public QueryBuilder<T> On(string connectionName)
        {
            if (string.IsNullOrWhiteSpace(connectionName))
                throw new ArgumentNullException(nameof(connectionName));

            _connectionName = connectionName;
            return this;
        }

        internal QueryBuilder<T> UseConnection(string? connectionName)
        {
            _connectionName = connectionName;
            return this;
        }

        /// <summary>
        /// Explicit On(name) connection (null when the query uses the model's / default connection)
        /// </summary>
        internal string? ExplicitConnection => _connectionName;

        private string? EffectiveConnection => _connectionName ?? _metadata.ConnectionName;

        private DbCommandBase NewCommand() => ModelBase.CreateCommand(EffectiveConnection);

        // Reads may go to a read replica; locking reads and OnPrimary() queries never do
        private DbCommandBase NewReadCommand() =>
            _primary || _lock != RowLock.None ? NewCommand() : ModelBase.CreateReadCommand(EffectiveConnection);

        private bool _primary;

        /// <summary>
        /// Run this query on the primary even when read replicas are registered (read your own writes)
        /// </summary>
        public QueryBuilder<T> OnPrimary()
        {
            _primary = true;
            return this;
        }

        private DbProviderType Provider() => ModelBase.CurrentProvider(EffectiveConnection);

        #endregion

        #region Select Methods

        /// <summary>
        /// Select specific columns
        /// </summary>
        public QueryBuilder<T> Select(params string[] columns)
        {
            foreach (var column in columns)
                _selectColumns.Add(new SelectItem { Text = column });
            return this;
        }

        /// <summary>
        /// Select a raw SQL expression, e.g. <c>SelectRaw("COUNT(*) AS Total")</c> or
        /// <c>SelectRaw("Price * ? AS Gross", 1.2m)</c>. Columns without a model property are read with
        /// <c>model.GetAttribute("Total")</c>. Raw SQL — never concatenate user input, pass it as a binding.
        /// </summary>
        public QueryBuilder<T> SelectRaw(string expression, params object?[] bindings)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentNullException(nameof(expression));

            _selectColumns.Add(new SelectItem { Text = expression, Raw = true, Bindings = bindings });
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

        /// <summary>
        /// Lock the selected rows until the transaction ends (SELECT ... FOR UPDATE; SQL Server: UPDLOCK).
        /// Use it inside <c>MersoTransaction.Run</c>; SQLite has no row locks (its write transaction locks the file).
        /// </summary>
        public QueryBuilder<T> LockForUpdate()
        {
            _lock = RowLock.Update;
            return this;
        }

        /// <summary>
        /// Shared lock: others can read the selected rows but not change them until the transaction ends
        /// (FOR SHARE / LOCK IN SHARE MODE; SQL Server: HOLDLOCK)
        /// </summary>
        public QueryBuilder<T> SharedLock()
        {
            _lock = RowLock.Share;
            return this;
        }

        /// <summary>
        /// Apply <paramref name="then"/> only when <paramref name="condition"/> is true (else <paramref name="otherwise"/>):
        /// <c>User.Query().When(onlyActive, q =&gt; q.Where("IsActive", true))</c>
        /// </summary>
        public QueryBuilder<T> When(bool condition, Action<QueryBuilder<T>> then, Action<QueryBuilder<T>>? otherwise = null)
        {
            if (then == null) throw new ArgumentNullException(nameof(then));

            if (condition)
                then(this);
            else
                otherwise?.Invoke(this);
            return this;
        }

        /// <summary>
        /// Apply <paramref name="then"/> with the value when it is given (not null, not an empty string, not false):
        /// <c>User.Query().When(search, (q, s) =&gt; q.WhereLike("Name", $"%{s}%"))</c>
        /// </summary>
        public QueryBuilder<T> When<TValue>(TValue value, Action<QueryBuilder<T>, TValue> then, Action<QueryBuilder<T>>? otherwise = null)
        {
            if (then == null) throw new ArgumentNullException(nameof(then));

            var given = value != null && !(value is string s && s.Length == 0) && !(value is bool b && !b);
            if (given)
                then(this, value);
            else
                otherwise?.Invoke(this);
            return this;
        }

        /// <summary>
        /// Opposite of <see cref="When(bool, Action{QueryBuilder{T}}, Action{QueryBuilder{T}})"/>
        /// </summary>
        public QueryBuilder<T> Unless(bool condition, Action<QueryBuilder<T>> then, Action<QueryBuilder<T>>? otherwise = null)
        {
            return When(!condition, then, otherwise);
        }

        /// <summary>
        /// Apply a reusable query part (a "scope"): <c>User.Query().Apply(UserScopes.Active)</c>.
        /// Extension methods work as well: <c>public static QueryBuilder&lt;User&gt; Active(this QueryBuilder&lt;User&gt; q) =&gt; q.Where("IsActive", true);</c>
        /// </summary>
        public QueryBuilder<T> Apply(Action<QueryBuilder<T>> scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            scope(this);
            return this;
        }

        #endregion

        #region Where Methods

        /// <summary>
        /// Add WHERE clause (operators: =, !=, &lt;&gt;, &lt;, &gt;, &lt;=, &gt;=, LIKE, NOT LIKE, ILIKE).
        /// A null value with = / != / IS / IS NOT becomes IS NULL / IS NOT NULL.
        /// </summary>
        public QueryBuilder<T> Where(string column, string op, object? value)
        {
            _whereClauses.Add(CreateComparison(column, op, value, "AND"));
            return this;
        }

        /// <summary>
        /// Add WHERE clause (equals)
        /// </summary>
        public QueryBuilder<T> Where(string column, object? value)
        {
            return Where(column, "=", value);
        }

        /// <summary>
        /// Parenthesised group: <c>Where(q => q.Where("A", 1).OrWhere("B", 2)).Where("C", 3)</c>
        /// → (A = 1 OR B = 2) AND C = 3
        /// </summary>
        public QueryBuilder<T> Where(Action<QueryBuilder<T>> group)
        {
            return AddGroup(group, "AND", false);
        }

        /// <summary>
        /// OR (parenthesised group)
        /// </summary>
        public QueryBuilder<T> OrWhere(Action<QueryBuilder<T>> group)
        {
            return AddGroup(group, "OR", false);
        }

        /// <summary>
        /// AND NOT (parenthesised group)
        /// </summary>
        public QueryBuilder<T> WhereNot(Action<QueryBuilder<T>> group)
        {
            return AddGroup(group, "AND", true);
        }

        /// <summary>
        /// Add OR WHERE clause
        /// </summary>
        public QueryBuilder<T> OrWhere(string column, string op, object? value)
        {
            _whereClauses.Add(CreateComparison(column, op, value, "OR"));
            return this;
        }

        /// <summary>
        /// Add OR WHERE clause (equals)
        /// </summary>
        public QueryBuilder<T> OrWhere(string column, object? value)
        {
            return OrWhere(column, "=", value);
        }

        /// <summary>
        /// WHERE primary key = id. For a composite key pass the values in key order:
        /// <c>WhereKey(orderId, productId)</c> (or an object[] / a dictionary of column → value).
        /// </summary>
        public QueryBuilder<T> WhereKey(object id)
        {
            var keys = _metadata.KeyProperties;
            if (keys.Count == 0)
                throw new InvalidOperationException($"Model {typeof(T).Name} has no primary key. Add [PrimaryKey] or an Id property.");

            var values = _metadata.GetKeyValues(id);
            for (int i = 0; i < keys.Count; i++)
                Where(keys[i].ColumnName, "=", values[i]);
            return this;
        }

        /// <summary>
        /// WHERE composite primary key = values (in key order)
        /// </summary>
        public QueryBuilder<T> WhereKey(params object[] keyValues)
        {
            return WhereKey((object)keyValues);
        }

        /// <summary>
        /// WHERE primary key IN ids (composite key: each id is an object[] in key order)
        /// </summary>
        internal QueryBuilder<T> WhereKeyIn(IList<object> ids)
        {
            var keys = _metadata.KeyProperties;
            if (keys.Count == 1)
                return WhereIn(keys[0].ColumnName, ids);

            return Where(any =>
            {
                foreach (var id in ids)
                    any.OrWhere(one => one.WhereKey(id));
            });
        }

        /// <summary>
        /// Add WHERE IN clause
        /// </summary>
        public QueryBuilder<T> WhereIn(string column, IEnumerable<object?>? values)
        {
            _whereClauses.Add(InClause(column, values, "IN", "AND"));
            return this;
        }

        /// <summary>
        /// Add WHERE NOT IN clause
        /// </summary>
        public QueryBuilder<T> WhereNotIn(string column, IEnumerable<object?>? values)
        {
            _whereClauses.Add(InClause(column, values, "NOT IN", "AND"));
            return this;
        }

        /// <summary>
        /// Add OR WHERE IN clause
        /// </summary>
        public QueryBuilder<T> OrWhereIn(string column, IEnumerable<object?>? values)
        {
            _whereClauses.Add(InClause(column, values, "IN", "OR"));
            return this;
        }

        /// <summary>
        /// Add OR WHERE NOT IN clause
        /// </summary>
        public QueryBuilder<T> OrWhereNotIn(string column, IEnumerable<object?>? values)
        {
            _whereClauses.Add(InClause(column, values, "NOT IN", "OR"));
            return this;
        }

        /// <summary>
        /// WHERE column IN (subquery). The subquery selects one column (<c>Select("UserId")</c>; default: its primary key).
        /// </summary>
        public QueryBuilder<T> WhereIn<TSub>(string column, QueryBuilder<TSub> subquery) where TSub : Model<TSub>, new()
        {
            _whereClauses.Add(SubInClause(column, subquery, "IN", "AND"));
            return this;
        }

        /// <summary>
        /// WHERE column NOT IN (subquery)
        /// </summary>
        public QueryBuilder<T> WhereNotIn<TSub>(string column, QueryBuilder<TSub> subquery) where TSub : Model<TSub>, new()
        {
            _whereClauses.Add(SubInClause(column, subquery, "NOT IN", "AND"));
            return this;
        }

        /// <summary>
        /// OR column IN (subquery)
        /// </summary>
        public QueryBuilder<T> OrWhereIn<TSub>(string column, QueryBuilder<TSub> subquery) where TSub : Model<TSub>, new()
        {
            _whereClauses.Add(SubInClause(column, subquery, "IN", "OR"));
            return this;
        }

        /// <summary>
        /// WHERE EXISTS (subquery). Correlate it with WhereColumn:
        /// <c>User.Query().WhereExists(Order.Query().WhereColumn("mc_orders.UserId", "mc_users.Id"))</c>
        /// </summary>
        public QueryBuilder<T> WhereExists<TSub>(QueryBuilder<TSub> subquery) where TSub : Model<TSub>, new()
        {
            _whereClauses.Add(ExistsClause(subquery, false, "AND"));
            return this;
        }

        /// <summary>
        /// WHERE NOT EXISTS (subquery)
        /// </summary>
        public QueryBuilder<T> WhereNotExists<TSub>(QueryBuilder<TSub> subquery) where TSub : Model<TSub>, new()
        {
            _whereClauses.Add(ExistsClause(subquery, true, "AND"));
            return this;
        }

        /// <summary>
        /// OR EXISTS (subquery)
        /// </summary>
        public QueryBuilder<T> OrWhereExists<TSub>(QueryBuilder<TSub> subquery) where TSub : Model<TSub>, new()
        {
            _whereClauses.Add(ExistsClause(subquery, false, "OR"));
            return this;
        }

        /// <summary>
        /// OR NOT EXISTS (subquery)
        /// </summary>
        public QueryBuilder<T> OrWhereNotExists<TSub>(QueryBuilder<TSub> subquery) where TSub : Model<TSub>, new()
        {
            _whereClauses.Add(ExistsClause(subquery, true, "OR"));
            return this;
        }

        #region Relation queries (WhereHas / Has / WithCount)

        /// <summary>
        /// Rows that have at least one related row: <c>User.Query().WhereHas("Orders")</c>. The relation is a
        /// [HasMany] / [HasOne] / [BelongsTo] / [MorphMany] / [MorphOne] property; soft-deleted related rows and
        /// the related model's global scopes are respected.
        /// </summary>
        public QueryBuilder<T> WhereHas(string relation) => AddHas(relation, null, false, "AND");

        /// <summary>
        /// Rows that have a related row matching the conditions:
        /// <c>User.Query().WhereHas&lt;Order&gt;("Orders", q =&gt; q.Where("Total", "&gt;", 100))</c>
        /// </summary>
        public QueryBuilder<T> WhereHas<TRelated>(string relation, Action<QueryBuilder<TRelated>> constraint) where TRelated : Model<TRelated>, new()
            => AddHas(relation, Constraint(relation, constraint), false, "AND");

        /// <summary>
        /// OR rows that have at least one related row
        /// </summary>
        public QueryBuilder<T> OrWhereHas(string relation) => AddHas(relation, null, false, "OR");

        /// <summary>
        /// OR rows that have a related row matching the conditions
        /// </summary>
        public QueryBuilder<T> OrWhereHas<TRelated>(string relation, Action<QueryBuilder<TRelated>> constraint) where TRelated : Model<TRelated>, new()
            => AddHas(relation, Constraint(relation, constraint), false, "OR");

        /// <summary>
        /// Rows without related rows: <c>User.Query().WhereDoesntHave("Orders")</c>
        /// </summary>
        public QueryBuilder<T> WhereDoesntHave(string relation) => AddHas(relation, null, true, "AND");

        /// <summary>
        /// Rows without a related row matching the conditions
        /// </summary>
        public QueryBuilder<T> WhereDoesntHave<TRelated>(string relation, Action<QueryBuilder<TRelated>> constraint) where TRelated : Model<TRelated>, new()
            => AddHas(relation, Constraint(relation, constraint), true, "AND");

        /// <summary>
        /// OR rows without related rows
        /// </summary>
        public QueryBuilder<T> OrWhereDoesntHave(string relation) => AddHas(relation, null, true, "OR");

        /// <summary>
        /// OR rows without a related row matching the conditions
        /// </summary>
        public QueryBuilder<T> OrWhereDoesntHave<TRelated>(string relation, Action<QueryBuilder<TRelated>> constraint) where TRelated : Model<TRelated>, new()
            => AddHas(relation, Constraint(relation, constraint), true, "OR");

        /// <summary>
        /// Rows by the number of related rows: <c>Has("Orders")</c> (at least one), <c>Has("Orders", "&gt;=", 3)</c>
        /// </summary>
        public QueryBuilder<T> Has(string relation, string op = ">=", int count = 1) => AddHasCount(relation, op, count, "AND");

        /// <summary>
        /// OR rows by the number of related rows
        /// </summary>
        public QueryBuilder<T> OrHas(string relation, string op = ">=", int count = 1) => AddHasCount(relation, op, count, "OR");

        /// <summary>
        /// Rows without related rows (same as <see cref="WhereDoesntHave(string)"/>)
        /// </summary>
        public QueryBuilder<T> DoesntHave(string relation) => AddHas(relation, null, true, "AND");

        /// <summary>
        /// OR rows without related rows
        /// </summary>
        public QueryBuilder<T> OrDoesntHave(string relation) => AddHas(relation, null, true, "OR");

        /// <summary>
        /// WhereHas with one condition: <c>User.Query().WhereRelation("Orders", "Status", "paid")</c>
        /// </summary>
        public QueryBuilder<T> WhereRelation(string relation, string column, object? value) => WhereRelation(relation, column, "=", value);

        /// <summary>
        /// WhereHas with one condition: <c>User.Query().WhereRelation("Orders", "Total", "&gt;", 100)</c>
        /// </summary>
        public QueryBuilder<T> WhereRelation(string relation, string column, string op, object? value)
        {
            var related = RelationInfo.Get(typeof(T), relation).NewRelatedQuery();
            related.AddComparison(column, op, value);
            return AddHas(relation, related, false, "AND");
        }

        /// <summary>
        /// OR WhereHas with one condition
        /// </summary>
        public QueryBuilder<T> OrWhereRelation(string relation, string column, string op, object? value)
        {
            var related = RelationInfo.Get(typeof(T), relation).NewRelatedQuery();
            related.AddComparison(column, op, value);
            return AddHas(relation, related, false, "OR");
        }

        /// <summary>
        /// Adds a column with the number of related rows: <c>User.Query().WithCount("Orders")</c> → "OrdersCount"
        /// ("Orders as PaidCount" names it). Read it with <c>GetAttribute&lt;int&gt;("OrdersCount")</c> or a [Computed] property.
        /// </summary>
        public QueryBuilder<T> WithCount(params string[] relations)
        {
            foreach (var item in relations ?? new string[0])
            {
                SplitAlias(item, out var relation, out var alias);
                AddAggregate(relation, "COUNT", null, null, alias ?? relation + "Count");
            }
            return this;
        }

        /// <summary>
        /// Number of related rows matching the conditions:
        /// <c>WithCount&lt;Order&gt;("Orders", q =&gt; q.Where("Status", "paid"), "PaidOrders")</c>
        /// </summary>
        public QueryBuilder<T> WithCount<TRelated>(string relation, Action<QueryBuilder<TRelated>> constraint, string? alias = null) where TRelated : Model<TRelated>, new()
            => AddAggregate(relation, "COUNT", null, Constraint(relation, constraint), alias ?? relation + "Count");

        /// <summary>
        /// Sum of a related column: <c>WithSum("Orders", "Total")</c> → "OrdersSumTotal" (null without related rows)
        /// </summary>
        public QueryBuilder<T> WithSum(string relation, string column, string? alias = null)
            => AddAggregate(relation, "SUM", column, null, alias ?? relation + "Sum" + AliasPart(column));

        /// <summary>
        /// Average of a related column: "OrdersAvgTotal"
        /// </summary>
        public QueryBuilder<T> WithAvg(string relation, string column, string? alias = null)
            => AddAggregate(relation, "AVG", column, null, alias ?? relation + "Avg" + AliasPart(column));

        /// <summary>
        /// Smallest related value: "OrdersMinTotal"
        /// </summary>
        public QueryBuilder<T> WithMin(string relation, string column, string? alias = null)
            => AddAggregate(relation, "MIN", column, null, alias ?? relation + "Min" + AliasPart(column));

        /// <summary>
        /// Largest related value: "OrdersMaxTotal"
        /// </summary>
        public QueryBuilder<T> WithMax(string relation, string column, string? alias = null)
            => AddAggregate(relation, "MAX", column, null, alias ?? relation + "Max" + AliasPart(column));

        /// <summary>
        /// 1 when a related row exists, else 0: "OrdersExists"
        /// </summary>
        public QueryBuilder<T> WithExists(string relation, string? alias = null)
            => AddAggregate(relation, "EXISTS", null, null, alias ?? relation + "Exists");

        /// <summary>
        /// Eager load a relation with conditions / order (applied to the last relation of a nested path):
        /// <c>User.Query().With&lt;Order&gt;("Orders", q =&gt; q.Where("Status", "paid").OrderByDesc("Id")).Get()</c>
        /// </summary>
        public EagerLoader<T> With<TRelated>(string relation, Action<QueryBuilder<TRelated>> constraint) where TRelated : Model<TRelated>, new()
        {
            return new EagerLoader<T>(this).With(relation, constraint);
        }

        private ISubQuery Constraint<TRelated>(string relation, Action<QueryBuilder<TRelated>> constraint) where TRelated : Model<TRelated>, new()
        {
            var info = RelationInfo.Get(typeof(T), relation);
            if (info.RelatedType != typeof(TRelated))
                throw new ArgumentException($"{typeof(T).Name}.{info.Property.Name} relates to {info.RelatedType?.Name ?? "several types"}, not {typeof(TRelated).Name}.", nameof(relation));

            var query = new QueryBuilder<TRelated>();
            constraint?.Invoke(query);
            return query;
        }

        private QueryBuilder<T> AddHas(string relation, ISubQuery? related, bool negate, string logic)
        {
            var info = RelationInfo.Get(typeof(T), relation);
            _whereClauses.Add(ExistsClause(new RelationSubQuery(info, related ?? info.NewRelatedQuery(), this), negate, logic));
            return this;
        }

        private QueryBuilder<T> AddHasCount(string relation, string op, int count, string logic)
        {
            op = (op ?? ">=").Trim();
            if (!ColumnOperators.Contains(op))
                throw new ArgumentException($"Operator '{op}' is not allowed.", nameof(op));

            // At least one / none: EXISTS is cheaper than counting
            if ((op == ">=" && count == 1) || (op == ">" && count == 0))
                return AddHas(relation, null, false, logic);
            if ((op == "<" && count == 1) || ((op == "=" || op == "<=") && count == 0))
                return AddHas(relation, null, true, logic);

            var info = RelationInfo.Get(typeof(T), relation);
            _whereClauses.Add(new WhereClause { CountQuery = new RelationSubQuery(info, info.NewRelatedQuery(), this), Operator = op, Value = count, Logic = logic });
            return this;
        }

        private QueryBuilder<T> AddAggregate(string relation, string function, string? column, ISubQuery? related, string alias)
        {
            var info = RelationInfo.Get(typeof(T), relation);
            _selectColumns.Add(new SelectItem
            {
                Text = alias,
                Function = function,
                AggregateColumn = column,
                Aggregate = new RelationSubQuery(info, related ?? info.NewRelatedQuery(), this)
            });
            return this;
        }

        // "Orders as PaidCount" → Orders + PaidCount
        private static void SplitAlias(string item, out string relation, out string? alias)
        {
            var text = (item ?? string.Empty).Trim();
            var at = text.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            relation = at < 0 ? text : text.Substring(0, at).Trim();
            alias = at < 0 ? null : text.Substring(at + 4).Trim();
        }

        // "total_amount" → "Total_amount" (letters, digits, underscore)
        private static string AliasPart(string column)
        {
            var clean = new string((column ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            return clean.Length == 0 ? clean : char.ToUpperInvariant(clean[0]) + clean.Substring(1);
        }

        #endregion

        /// <summary>
        /// Compare two columns: <c>WhereColumn("UpdatedAt", "&gt;", "CreatedAt")</c>
        /// </summary>
        public QueryBuilder<T> WhereColumn(string first, string op, string second)
        {
            _whereClauses.Add(ColumnClause(first, op, second, "AND"));
            return this;
        }

        /// <summary>
        /// Two columns are equal (also correlates a subquery with the outer table)
        /// </summary>
        public QueryBuilder<T> WhereColumn(string first, string second)
        {
            return WhereColumn(first, "=", second);
        }

        /// <summary>
        /// OR compare two columns
        /// </summary>
        public QueryBuilder<T> OrWhereColumn(string first, string op, string second)
        {
            _whereClauses.Add(ColumnClause(first, op, second, "OR"));
            return this;
        }

        /// <summary>
        /// Add WHERE BETWEEN clause
        /// </summary>
        public QueryBuilder<T> WhereBetween(string column, object? min, object? max)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "BETWEEN", Value = new[] { min, max }, Logic = "AND", IsBetween = true });
            return this;
        }

        /// <summary>
        /// Add WHERE NOT BETWEEN clause
        /// </summary>
        public QueryBuilder<T> WhereNotBetween(string column, object? min, object? max)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "NOT BETWEEN", Value = new[] { min, max }, Logic = "AND", IsBetween = true });
            return this;
        }

        /// <summary>
        /// Add OR WHERE BETWEEN clause
        /// </summary>
        public QueryBuilder<T> OrWhereBetween(string column, object? min, object? max)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "BETWEEN", Value = new[] { min, max }, Logic = "OR", IsBetween = true });
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
        /// Add OR WHERE NULL clause
        /// </summary>
        public QueryBuilder<T> OrWhereNull(string column)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "IS NULL", Value = null, Logic = "OR", IsNull = true });
            return this;
        }

        /// <summary>
        /// Add OR WHERE NOT NULL clause
        /// </summary>
        public QueryBuilder<T> OrWhereNotNull(string column)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "IS NOT NULL", Value = null, Logic = "OR", IsNull = true });
            return this;
        }

        /// <summary>
        /// Add WHERE LIKE clause (matches anywhere: %pattern%)
        /// </summary>
        public QueryBuilder<T> WhereLike(string column, string pattern)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "LIKE", Value = $"%{pattern}%", Logic = "AND" });
            return this;
        }

        /// <summary>
        /// Add OR WHERE LIKE clause (matches anywhere: %pattern%)
        /// </summary>
        public QueryBuilder<T> OrWhereLike(string column, string pattern)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "LIKE", Value = $"%{pattern}%", Logic = "OR" });
            return this;
        }

        /// <summary>
        /// Add WHERE date clause (compares the date part only)
        /// </summary>
        public QueryBuilder<T> WhereDate(string column, DateTime date)
        {
            _whereClauses.Add(new WhereClause { Column = column, Operator = "DATE", Value = date.Date, Logic = "AND", IsDate = true });
            return this;
        }

        /// <summary>
        /// Add raw WHERE clause (raw SQL — never concatenate user input)
        /// </summary>
        public QueryBuilder<T> WhereRaw(string sql)
        {
            _whereClauses.Add(new WhereClause { RawSql = sql, Logic = "AND", IsRaw = true });
            return this;
        }

        /// <summary>
        /// Add raw WHERE clause with <c>?</c> placeholders: <c>WhereRaw("Age &gt; ? AND City = ?", 18, city)</c>
        /// </summary>
        public QueryBuilder<T> WhereRaw(string sql, params object?[] bindings)
        {
            _whereClauses.Add(new WhereClause { RawSql = sql, Bindings = bindings, Logic = "AND", IsRaw = true });
            return this;
        }

        /// <summary>
        /// Add raw OR WHERE clause with <c>?</c> placeholders
        /// </summary>
        public QueryBuilder<T> OrWhereRaw(string sql, params object?[] bindings)
        {
            _whereClauses.Add(new WhereClause { RawSql = sql, Bindings = bindings, Logic = "OR", IsRaw = true });
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

        /// <summary>
        /// Only soft deleted records
        /// </summary>
        public QueryBuilder<T> OnlyTrashed()
        {
            _onlyTrashed = true;
            return this;
        }

        /// <summary>
        /// Skip the model's [GlobalScope] filters for this query
        /// </summary>
        public QueryBuilder<T> WithoutGlobalScopes()
        {
            _withoutGlobalScopes = true;
            return this;
        }

        private QueryBuilder<T> AddGroup(Action<QueryBuilder<T>> group, string logic, bool negate)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));

            var inner = new QueryBuilder<T>();
            group(inner);
            _whereClauses.Add(new WhereClause { Group = inner._whereClauses, Logic = logic, Negate = negate });
            return this;
        }

        private static WhereClause InClause(string column, IEnumerable<object?>? values, string op, string logic)
        {
            var list = values?.Select(v => DbValue(column, v)).ToList() ?? new List<object?>();
            return new WhereClause { Column = column, Operator = op, Value = list, Logic = logic, IsIn = true };
        }

        // [Converter] columns are compared with the stored form (enum name, JSON text ...)
        private static object? DbValue(string column, object? value)
        {
            if (value == null || value == DBNull.Value)
                return value;

            var prop = ModelBase.FindProperty(ModelBase.GetMetadata<T>(), BareColumn(column));
            return prop?.Converter == null ? value : prop.ToDatabase(value);
        }

        private static WhereClause SubInClause(string column, ISubQuery subquery, string op, string logic)
        {
            if (subquery == null) throw new ArgumentNullException(nameof(subquery));
            return new WhereClause { Column = column, Operator = op, SubQuery = subquery, Logic = logic };
        }

        private static WhereClause ExistsClause(ISubQuery subquery, bool negate, string logic)
        {
            if (subquery == null) throw new ArgumentNullException(nameof(subquery));
            return new WhereClause { SubQuery = subquery, IsExists = true, Negate = negate, Logic = logic };
        }

        private static WhereClause ColumnClause(string first, string op, string second, string logic)
        {
            op = (op ?? "=").Trim();
            if (!ColumnOperators.Contains(op))
                throw new ArgumentException($"Unsupported column operator '{op}'.", nameof(op));

            return new WhereClause { Column = first, Operator = op, OtherColumn = second, Logic = logic, IsColumn = true };
        }

        private static WhereClause CreateComparison(string column, string op, object? value, string logic)
        {
            op = (op ?? "=").Trim();

            if (value == null || value == DBNull.Value)
            {
                if (op == "=" || op.Equals("IS", StringComparison.OrdinalIgnoreCase))
                    return new WhereClause { Column = column, Operator = "IS NULL", Logic = logic, IsNull = true };
                if (op == "!=" || op == "<>" || op.Equals("IS NOT", StringComparison.OrdinalIgnoreCase))
                    return new WhereClause { Column = column, Operator = "IS NOT NULL", Logic = logic, IsNull = true };
            }

            if (!AllowedOperators.Contains(op))
                throw new ArgumentException($"Unsupported operator '{op}'. Use WhereRaw for custom SQL.", nameof(op));

            return new WhereClause { Column = column, Operator = op.ToUpperInvariant(), Value = DbValue(column, value), Logic = logic };
        }

        #endregion

        #region Join / Union Methods

        /// <summary>
        /// Add INNER JOIN (table may carry an alias: "orders o"; the condition is raw SQL)
        /// </summary>
        public QueryBuilder<T> Join(string table, string condition)
        {
            _joins.Add(new JoinItem("INNER", table, condition));
            return this;
        }

        /// <summary>
        /// Add LEFT JOIN
        /// </summary>
        public QueryBuilder<T> LeftJoin(string table, string condition)
        {
            _joins.Add(new JoinItem("LEFT", table, condition));
            return this;
        }

        /// <summary>
        /// Add RIGHT JOIN (not supported by SQLite before 3.39)
        /// </summary>
        public QueryBuilder<T> RightJoin(string table, string condition)
        {
            _joins.Add(new JoinItem("RIGHT", table, condition));
            return this;
        }

        /// <summary>
        /// UNION (duplicates removed) with another query of the same model. ORDER BY / Limit / Paginate of this
        /// query apply to the combined rows; the other query's own order and limit are ignored.
        /// </summary>
        public QueryBuilder<T> Union(QueryBuilder<T> other)
        {
            _unions.Add(new UnionItem(other ?? throw new ArgumentNullException(nameof(other)), false));
            return this;
        }

        /// <summary>
        /// UNION ALL (keeps duplicates) with another query of the same model
        /// </summary>
        public QueryBuilder<T> UnionAll(QueryBuilder<T> other)
        {
            _unions.Add(new UnionItem(other ?? throw new ArgumentNullException(nameof(other)), true));
            return this;
        }

        #endregion

        #region Order & Group Methods

        /// <summary>
        /// Add ORDER BY clause (direction: ASC or DESC)
        /// </summary>
        public QueryBuilder<T> OrderBy(string column, string direction = "ASC")
        {
            var dir = (direction ?? "ASC").Trim().ToUpperInvariant();
            if (dir != "ASC" && dir != "DESC")
                throw new ArgumentException("Direction must be ASC or DESC.", nameof(direction));

            _orders.RemoveAll(o => o.Random);
            _orders.Add(new OrderItem { Column = column, Direction = dir });
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
        /// Add a raw ORDER BY expression: <c>OrderByRaw("CASE WHEN Status = ? THEN 0 ELSE 1 END, Name", "urgent")</c>
        /// </summary>
        public QueryBuilder<T> OrderByRaw(string sql, params object?[] bindings)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentNullException(nameof(sql));

            _orders.RemoveAll(o => o.Random);
            _orders.Add(new OrderItem { RawSql = sql, Bindings = bindings });
            return this;
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
        /// Add HAVING clause (value is sent as a parameter)
        /// </summary>
        public QueryBuilder<T> Having(string column, string op, object? value)
        {
            op = (op ?? "=").Trim();
            if (!AllowedOperators.Contains(op))
                throw new ArgumentException($"Unsupported operator '{op}'.", nameof(op));

            _having = new HavingClause { Column = column, Operator = op.ToUpperInvariant(), Value = value };
            return this;
        }

        /// <summary>
        /// Raw HAVING expression with <c>?</c> placeholders: <c>HavingRaw("SUM(Total) &gt; ?", 1000)</c>
        /// </summary>
        public QueryBuilder<T> HavingRaw(string sql, params object?[] bindings)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentNullException(nameof(sql));

            _having = new HavingClause { RawSql = sql, Bindings = bindings };
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
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

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
        /// Random order (NEWID() / RAND() / RANDOM() depending on the provider)
        /// </summary>
        public QueryBuilder<T> InRandomOrder()
        {
            _orders.Clear();
            _orders.Add(new OrderItem { Random = true });
            return this;
        }

        #endregion

        #region Execute Methods

        /// <summary>
        /// Get all results
        /// </summary>
        public List<T> Get()
        {
            return DbRun.Sync(GetCoreAsync(_limit, _offset, false, default));
        }

        /// <summary>
        /// Get all results (async)
        /// </summary>
        public Task<List<T>> GetAsync(CancellationToken cancellationToken = default)
        {
            return GetCoreAsync(_limit, _offset, true, cancellationToken);
        }

        internal Task<List<T>> GetCoreAsync(bool useAsync, CancellationToken ct)
        {
            return GetCoreAsync(_limit, _offset, useAsync, ct);
        }

        private async Task<List<T>> GetCoreAsync(int? limit, int? offset, bool useAsync, CancellationToken ct)
        {
            using (var db = NewReadCommand())
            {
                var query = BuildSelect(db.ProviderType, null, limit, offset, aggregate: false);
                DbRun.Bind(db, query.Parameters);
                return await DbRun.Models(db, query.Sql, _metadata, NewLoadedModel, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Get first result
        /// </summary>
        public T? First()
        {
            return DbRun.Sync(GetFirstCoreAsync(false, default));
        }

        /// <summary>
        /// Get first result (async)
        /// </summary>
        public Task<T?> FirstAsync(CancellationToken cancellationToken = default)
        {
            return GetFirstCoreAsync(true, cancellationToken);
        }

        internal async Task<T?> GetFirstCoreAsync(bool useAsync, CancellationToken ct)
        {
            return (await GetCoreAsync(1, _offset, useAsync, ct).ConfigureAwait(false)).FirstOrDefault();
        }

        /// <summary>
        /// Get first result or throw exception
        /// </summary>
        public T FirstOrFail()
        {
            return First() ?? throw new InvalidOperationException("No record found");
        }

        /// <summary>
        /// Get first result or throw exception (async)
        /// </summary>
        public async Task<T> FirstOrFailAsync(CancellationToken cancellationToken = default)
        {
            return await FirstAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("No record found");
        }

        /// <summary>
        /// Get single column value of the first row (as string)
        /// </summary>
        public object Value(string column)
        {
            return DbRun.Sync(ValueCore(column, false, default));
        }

        /// <summary>
        /// Get single column value of the first row (as string, async)
        /// </summary>
        public async Task<object> ValueAsync(string column, CancellationToken cancellationToken = default)
        {
            return await ValueCore(column, true, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> ValueCore(string column, bool useAsync, CancellationToken ct)
        {
            using (var db = NewReadCommand())
            {
                var query = BuildSelect(db.ProviderType, new[] { SqlDialect.Column(db.ProviderType, column) }, 1, _offset, aggregate: false);
                DbRun.Bind(db, query.Parameters);
                return await DbRun.String(db, query.Sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Get single column as list
        /// </summary>
        public List<object?> Pluck(string column)
        {
            return DbRun.Sync(PluckCore(column, false, default));
        }

        /// <summary>
        /// Get single column as list (async)
        /// </summary>
        public Task<List<object?>> PluckAsync(string column, CancellationToken cancellationToken = default)
        {
            return PluckCore(column, true, cancellationToken);
        }

        private async Task<List<object?>> PluckCore(string column, bool useAsync, CancellationToken ct)
        {
            using (var db = NewReadCommand())
            {
                var query = BuildSelect(db.ProviderType, new[] { SqlDialect.Column(db.ProviderType, column) }, _limit, _offset, aggregate: false);
                DbRun.Bind(db, query.Parameters);
                return await DbRun.Column(db, query.Sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Check if any records exist
        /// </summary>
        public bool Exists()
        {
            return DbRun.Sync(ExistsCoreAsync(false, default));
        }

        /// <summary>
        /// Check if any records exist (async)
        /// </summary>
        public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        {
            return ExistsCoreAsync(true, cancellationToken);
        }

        internal async Task<bool> ExistsCoreAsync(bool useAsync, CancellationToken ct)
        {
            return await CountCore("*", useAsync, ct).ConfigureAwait(false) > 0;
        }

        /// <summary>
        /// Check if no records exist
        /// </summary>
        public bool DoesntExist()
        {
            return !Exists();
        }

        /// <summary>
        /// Count records (ignores ORDER BY / LIMIT; grouped or distinct queries count their rows)
        /// </summary>
        public int Count(string column = "*")
        {
            return DbRun.Sync(CountCore(column, false, default));
        }

        /// <summary>
        /// Count records (async)
        /// </summary>
        public Task<int> CountAsync(string column = "*", CancellationToken cancellationToken = default)
        {
            return CountCore(column, true, cancellationToken);
        }

        private async Task<int> CountCore(string column, bool useAsync, CancellationToken ct)
        {
            using (var db = NewReadCommand())
            {
                var p = db.ProviderType;
                BuiltQuery query;
                if ((!string.IsNullOrEmpty(_groupBy) || _distinct) && _unions.Count == 0)
                {
                    var inner = BuildSelect(p, null, null, null, aggregate: true);
                    query = new BuiltQuery($"SELECT COUNT(*) FROM ({inner.Sql}) mc_count", inner.Parameters);
                }
                else
                {
                    query = BuildSelect(p, new[] { $"COUNT({SqlDialect.Column(p, column)})" }, null, null, aggregate: true);
                }

                DbRun.Bind(db, query.Parameters);
                return await DbRun.Int32(db, query.Sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sum column values
        /// </summary>
        public decimal Sum(string column)
        {
            return DbRun.Sync(DecimalAggregate("SUM", column, false, default));
        }

        /// <summary>
        /// Sum column values (async)
        /// </summary>
        public Task<decimal> SumAsync(string column, CancellationToken cancellationToken = default)
        {
            return DecimalAggregate("SUM", column, true, cancellationToken);
        }

        /// <summary>
        /// Average column values
        /// </summary>
        public decimal Avg(string column)
        {
            return DbRun.Sync(DecimalAggregate("AVG", column, false, default));
        }

        /// <summary>
        /// Average column values (async)
        /// </summary>
        public Task<decimal> AvgAsync(string column, CancellationToken cancellationToken = default)
        {
            return DecimalAggregate("AVG", column, true, cancellationToken);
        }

        /// <summary>
        /// Max column value (as string)
        /// </summary>
        public object Max(string column)
        {
            return DbRun.Sync(StringAggregate("MAX", column, false, default));
        }

        /// <summary>
        /// Max column value (as string, async)
        /// </summary>
        public async Task<object> MaxAsync(string column, CancellationToken cancellationToken = default)
        {
            return await StringAggregate("MAX", column, true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Min column value (as string)
        /// </summary>
        public object Min(string column)
        {
            return DbRun.Sync(StringAggregate("MIN", column, false, default));
        }

        /// <summary>
        /// Min column value (as string, async)
        /// </summary>
        public async Task<object> MinAsync(string column, CancellationToken cancellationToken = default)
        {
            return await StringAggregate("MIN", column, true, cancellationToken).ConfigureAwait(false);
        }

        private async Task<decimal> DecimalAggregate(string function, string column, bool useAsync, CancellationToken ct)
        {
            using (var db = NewReadCommand())
            {
                var query = AggregateQuery(db.ProviderType, function, column);
                DbRun.Bind(db, query.Parameters);
                return await DbRun.Decimal(db, query.Sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        private async Task<string> StringAggregate(string function, string column, bool useAsync, CancellationToken ct)
        {
            using (var db = NewReadCommand())
            {
                var query = AggregateQuery(db.ProviderType, function, column);
                DbRun.Bind(db, query.Parameters);
                return await DbRun.String(db, query.Sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Aggregate returned as the provider's value (Model.Min / Model.Max)
        /// </summary>
        internal async Task<object?> AggregateValueCore(string function, string column, bool useAsync, CancellationToken ct)
        {
            using (var db = NewReadCommand())
            {
                var query = AggregateQuery(db.ProviderType, function, column);
                DbRun.Bind(db, query.Parameters);
                return await DbRun.Scalar(db, query.Sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        private BuiltQuery AggregateQuery(DbProviderType provider, string function, string column)
        {
            return BuildSelect(provider, new[] { $"{function}({SqlDialect.Column(provider, column)})" }, null, null, aggregate: true);
        }

        /// <summary>
        /// Update matching records. Values of type <see cref="RawValue"/> are written as SQL expressions.
        /// Returns the number of affected rows.
        /// </summary>
        public int Update(Dictionary<string, object?> data)
        {
            return DbRun.Sync(UpdateCore(data, false, default));
        }

        /// <summary>
        /// Update matching records (async)
        /// </summary>
        public Task<int> UpdateAsync(Dictionary<string, object?> data, CancellationToken cancellationToken = default)
        {
            return UpdateCore(data, true, cancellationToken);
        }

        private async Task<int> UpdateCore(Dictionary<string, object?> data, bool useAsync, CancellationToken ct)
        {
            if (data == null || data.Count == 0)
                return 0;

            using (var db = NewCommand())
            {
                var p = db.ProviderType;
                int paramIndex = 0;
                var parameters = new Dictionary<string, object?>();
                var where = BuildWhere(p, ref paramIndex, parameters);

                var setClauses = new List<string>();
                foreach (var kvp in data)
                {
                    var column = SqlDialect.Column(p, kvp.Key);
                    if (kvp.Value is RawValue raw)
                    {
                        setClauses.Add($"{column} = {raw.Value}");
                        continue;
                    }

                    var paramName = $"@set{paramIndex++}";
                    setClauses.Add($"{column} = {paramName}");
                    parameters[paramName] = kvp.Value;
                }

                var sql = $"UPDATE {SqlDialect.Table(p, _metadata.TableName)} SET {string.Join(", ", setClauses)}";
                if (!string.IsNullOrEmpty(where))
                    sql += $" WHERE {where}";

                DbRun.Bind(db, parameters);
                return await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delete matching records (soft delete when configured; after OnlyTrashed() / WithTrashed() rows are removed).
        /// Returns the number of affected rows.
        /// </summary>
        public int Delete()
        {
            return DbRun.Sync(DeleteCore(false, default));
        }

        /// <summary>
        /// Delete matching records (async)
        /// </summary>
        public Task<int> DeleteAsync(CancellationToken cancellationToken = default)
        {
            return DeleteCore(true, cancellationToken);
        }

        private async Task<int> DeleteCore(bool useAsync, CancellationToken ct)
        {
            using (var db = NewCommand())
            {
                var p = db.ProviderType;
                int paramIndex = 0;
                var parameters = new Dictionary<string, object?>();
                var where = BuildWhere(p, ref paramIndex, parameters);
                var table = SqlDialect.Table(p, _metadata.TableName);

                string sql;
                if (_metadata.HasSoftDelete && !_withTrashed && !_onlyTrashed)
                {
                    sql = $"UPDATE {table} SET {SqlDialect.Column(p, _metadata.SoftDeleteColumn)} = @deleted_at";
                    parameters["@deleted_at"] = DateTime.UtcNow;
                }
                else
                {
                    sql = $"DELETE FROM {table}";
                }

                if (!string.IsNullOrEmpty(where))
                    sql += $" WHERE {where}";

                DbRun.Bind(db, parameters);
                return await DbRun.NonQuery(db, sql, useAsync, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Increment column value. Returns the number of affected rows.
        /// </summary>
        public int Increment(string column, int amount = 1)
        {
            return Update(IncrementData(column, amount));
        }

        /// <summary>
        /// Increment column value (async)
        /// </summary>
        public Task<int> IncrementAsync(string column, int amount = 1, CancellationToken cancellationToken = default)
        {
            return UpdateAsync(IncrementData(column, amount), cancellationToken);
        }

        /// <summary>
        /// Decrement column value. Returns the number of affected rows.
        /// </summary>
        public int Decrement(string column, int amount = 1)
        {
            return Update(IncrementData(column, -amount));
        }

        /// <summary>
        /// Decrement column value (async)
        /// </summary>
        public Task<int> DecrementAsync(string column, int amount = 1, CancellationToken cancellationToken = default)
        {
            return UpdateAsync(IncrementData(column, -amount), cancellationToken);
        }

        private Dictionary<string, object?> IncrementData(string column, int amount)
        {
            var quoted = SqlDialect.Column(Provider(), column);
            var expression = amount < 0
                ? $"{quoted} - {(-(long)amount).ToString(CultureInfo.InvariantCulture)}"
                : $"{quoted} + {amount.ToString(CultureInfo.InvariantCulture)}";
            return new Dictionary<string, object?> { { column, new RawValue(expression) } };
        }

        /// <summary>
        /// Get the generated SQL for the provider of this query's connection
        /// </summary>
        public string ToSql()
        {
            return BuildSelect(Provider(), null, _limit, _offset, aggregate: false).Sql;
        }

        /// <summary>
        /// Stable cache key: connection, SQL and parameter values (queries that differ only in values get different keys)
        /// </summary>
        internal string ToCacheKey()
        {
            var query = BuildSelect(Provider(), null, _limit, _offset, aggregate: false);
            var sb = new StringBuilder(EffectiveConnection ?? string.Empty).Append('|').Append(query.Sql);
            foreach (var p in query.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sb.Append('|').Append(p.Key).Append('=');
                sb.Append(p.Value?.GetType().FullName).Append(':');
                sb.Append(p.Value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : p.Value?.ToString());
            }

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Get paginated results
        /// </summary>
        public PaginatedResult<T> Paginate(int pageNumber = 1, int pageSize = 10)
        {
            return DbRun.Sync(PaginateCore(pageNumber, pageSize, false, default));
        }

        /// <summary>
        /// Get paginated results (async)
        /// </summary>
        public Task<PaginatedResult<T>> PaginateAsync(int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            return PaginateCore(pageNumber, pageSize, true, cancellationToken);
        }

        private async Task<PaginatedResult<T>> PaginateCore(int pageNumber, int pageSize, bool useAsync, CancellationToken ct)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var totalCount = await CountCore("*", useAsync, ct).ConfigureAwait(false);
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            var items = await GetCoreAsync(pageSize, (pageNumber - 1) * pageSize, useAsync, ct).ConfigureAwait(false);

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
        /// A page without counting all rows (one row more is read to know whether a next page exists)
        /// </summary>
        public SimplePaginatedResult<T> SimplePaginate(int pageNumber = 1, int pageSize = 10)
        {
            return DbRun.Sync(SimplePaginateCore(pageNumber, pageSize, false, default));
        }

        /// <summary>
        /// A page without counting all rows (async)
        /// </summary>
        public Task<SimplePaginatedResult<T>> SimplePaginateAsync(int pageNumber = 1, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            return SimplePaginateCore(pageNumber, pageSize, true, cancellationToken);
        }

        private async Task<SimplePaginatedResult<T>> SimplePaginateCore(int pageNumber, int pageSize, bool useAsync, CancellationToken ct)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var items = await GetCoreAsync(pageSize + 1, (pageNumber - 1) * pageSize, useAsync, ct).ConfigureAwait(false);
            var hasNext = items.Count > pageSize;
            if (hasNext)
                items.RemoveAt(items.Count - 1);

            return new SimplePaginatedResult<T>
            {
                Items = items,
                CurrentPage = pageNumber,
                PageSize = pageSize,
                HasPreviousPage = pageNumber > 1,
                HasNextPage = hasNext
            };
        }

        /// <summary>
        /// Keyset ("cursor") paging: fast on large tables because it continues after the last row instead of
        /// skipping rows. Pass <c>NextCursor</c> / <c>PreviousCursor</c> of a result to move on. Ordered by the
        /// OrderBy columns plus the primary key (default: the primary key); those columns must not be NULL.
        /// </summary>
        public CursorPaginatedResult<T> CursorPaginate(int pageSize = 10, string? cursor = null)
        {
            return DbRun.Sync(CursorPaginateCore(pageSize, cursor, false, default));
        }

        /// <summary>
        /// Keyset ("cursor") paging (async)
        /// </summary>
        public Task<CursorPaginatedResult<T>> CursorPaginateAsync(int pageSize = 10, string? cursor = null, CancellationToken cancellationToken = default)
        {
            return CursorPaginateCore(pageSize, cursor, true, cancellationToken);
        }

        private async Task<CursorPaginatedResult<T>> CursorPaginateCore(int pageSize, string? cursor, bool useAsync, CancellationToken ct)
        {
            if (pageSize < 1) pageSize = 10;

            var keys = CursorColumns();
            var decoded = string.IsNullOrEmpty(cursor) ? null : PageCursor.Decode(cursor!, keys.Select(k => ColumnType(k.Column)).ToList());
            var backwards = decoded?.Previous == true;

            var savedOrders = new List<OrderItem>(_orders);
            WhereClause? keyset = null;
            try
            {
                _orders.Clear();
                foreach (var key in keys)
                    _orders.Add(new OrderItem { Column = key.Column, Direction = key.Descending ^ backwards ? "DESC" : "ASC" });

                if (decoded != null)
                {
                    keyset = KeysetClause(keys, decoded.Values, backwards);
                    _whereClauses.Add(keyset);
                }

                var rows = await GetCoreAsync(pageSize + 1, null, useAsync, ct).ConfigureAwait(false);
                var hasMore = rows.Count > pageSize;
                if (hasMore)
                    rows.RemoveAt(rows.Count - 1);
                if (backwards)
                    rows.Reverse();

                var hasNext = backwards ? decoded != null : hasMore;
                var hasPrevious = backwards ? hasMore : decoded != null;
                return new CursorPaginatedResult<T>
                {
                    Items = rows,
                    PageSize = pageSize,
                    NextCursor = hasNext && rows.Count > 0 ? PageCursor.Encode(false, keys.Select(k => CursorValue(rows[rows.Count - 1], k.Column))) : null,
                    PreviousCursor = hasPrevious && rows.Count > 0 ? PageCursor.Encode(true, keys.Select(k => CursorValue(rows[0], k.Column))) : null
                };
            }
            finally
            {
                if (keyset != null)
                    _whereClauses.Remove(keyset);
                _orders.Clear();
                _orders.AddRange(savedOrders);
            }
        }

        // Order columns + primary key as tie-breaker
        private List<(string Column, bool Descending)> CursorColumns()
        {
            var columns = new List<(string Column, bool Descending)>();
            foreach (var order in _orders)
            {
                if (order.Random || order.RawSql != null)
                    throw new InvalidOperationException("CursorPaginate needs column orders (no OrderByRaw / InRandomOrder).");
                columns.Add((order.Column!, string.Equals(order.Direction, "DESC", StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var key in _metadata.KeyProperties)
            {
                if (!columns.Any(c => BareColumn(c.Column).Equals(key.ColumnName, StringComparison.OrdinalIgnoreCase)))
                    columns.Add((key.ColumnName, false));
            }

            if (columns.Count == 0)
                throw new InvalidOperationException($"CursorPaginate on {typeof(T).Name}: add OrderBy columns (the model has no primary key).");
            return columns;
        }

        // (c1 > v1) OR (c1 = v1 AND c2 > v2) OR ... ("<" for descending columns; flipped when paging backwards)
        private WhereClause KeysetClause(List<(string Column, bool Descending)> keys, object?[] values, bool backwards)
        {
            var alternatives = new List<WhereClause>();
            for (int i = 0; i < keys.Count; i++)
            {
                var all = new List<WhereClause>();
                for (int j = 0; j < i; j++)
                    all.Add(CreateComparison(keys[j].Column, "=", values[j], "AND"));
                all.Add(CreateComparison(keys[i].Column, keys[i].Descending ^ backwards ? "<" : ">", values[i], "AND"));
                alternatives.Add(new WhereClause { Group = all, Logic = i == 0 ? "AND" : "OR" });
            }
            return new WhereClause { Group = alternatives, Logic = "AND" };
        }

        private static string BareColumn(string column)
        {
            var dot = column.LastIndexOf('.');
            return dot < 0 ? column : column.Substring(dot + 1);
        }

        private Type? ColumnType(string column)
        {
            return ModelBase.FindProperty(_metadata, BareColumn(column))?.PropertyInfo.PropertyType;
        }

        private object? CursorValue(T model, string column)
        {
            var prop = ModelBase.FindProperty(_metadata, BareColumn(column));
            return prop != null ? prop.GetValue(model) : model.GetAttribute(BareColumn(column));
        }

        /// <summary>
        /// Models one by one while you iterate: rows stream from the open data reader and are never all in
        /// memory together (no eager loading). Inside a transaction run no other query before the loop ends.
        /// </summary>
        public IEnumerable<T> Cursor()
        {
            using (var db = NewReadCommand())
            {
                var query = BuildSelect(db.ProviderType, null, _limit, _offset, aggregate: false);
                DbRun.Bind(db, query.Parameters);

                ModelMapper.ReaderMap? map = null;
                foreach (var record in db.StreamReader(query.Sql))
                {
                    map = map ?? new ModelMapper.ReaderMap(record, _metadata);
                    var model = NewLoadedModel();
                    map.Fill(record, model);
                    yield return model;
                }
            }
        }

        /// <summary>
        /// Async counterpart of <see cref="Cursor"/>: <c>await foreach (var user in User.Query().CursorAsync())</c>
        /// </summary>
        public async IAsyncEnumerable<T> CursorAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using (var db = NewReadCommand())
            {
                var query = BuildSelect(db.ProviderType, null, _limit, _offset, aggregate: false);
                DbRun.Bind(db, query.Parameters);

                ModelMapper.ReaderMap? map = null;
                await foreach (var record in db.StreamReaderAsync(query.Sql, cancellationToken).ConfigureAwait(false))
                {
                    map = map ?? new ModelMapper.ReaderMap(record, _metadata);
                    var model = NewLoadedModel();
                    map.Fill(record, model);
                    yield return model;
                }
            }
        }

        /// <summary>
        /// Process results in chunks (ordered by primary key when no order is given, so pages are stable)
        /// </summary>
        public void Chunk(int chunkSize, Action<List<T>> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            DbRun.Sync(ChunkCore(chunkSize, Wrap(callback), false, default));
        }

        /// <summary>
        /// Process results in chunks (async)
        /// </summary>
        public Task ChunkAsync(int chunkSize, Func<List<T>, Task> callback, CancellationToken cancellationToken = default)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return ChunkCore(chunkSize, callback, true, cancellationToken);
        }

        private static Func<List<T>, Task> Wrap(Action<List<T>> callback)
        {
            return list =>
            {
                callback(list);
                return Task.CompletedTask;
            };
        }

        private async Task<bool> ChunkCore(int chunkSize, Func<List<T>, Task> callback, bool useAsync, CancellationToken ct)
        {
            if (chunkSize < 1) throw new ArgumentOutOfRangeException(nameof(chunkSize));

            if (_orders.Count == 0)
            {
                foreach (var key in _metadata.KeyProperties)
                    OrderBy(key.ColumnName);
            }

            int page = 0;
            List<T> results;

            do
            {
                results = await GetCoreAsync(chunkSize, page * chunkSize, useAsync, ct).ConfigureAwait(false);

                if (results.Count > 0)
                {
                    await callback(results).ConfigureAwait(false);
                }

                page++;
            } while (results.Count == chunkSize);

            return true;
        }

        /// <summary>
        /// Process results in chunks by increasing id (safe when the callback changes rows)
        /// </summary>
        public void ChunkById(int chunkSize, Action<List<T>> callback, string column = "Id")
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            DbRun.Sync(ChunkByIdCore(chunkSize, Wrap(callback), column, false, default));
        }

        /// <summary>
        /// Process results in chunks by increasing id (async)
        /// </summary>
        public Task ChunkByIdAsync(int chunkSize, Func<List<T>, Task> callback, string column = "Id", CancellationToken cancellationToken = default)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return ChunkByIdCore(chunkSize, callback, column, true, cancellationToken);
        }

        private async Task<bool> ChunkByIdCore(int chunkSize, Func<List<T>, Task> callback, string column, bool useAsync, CancellationToken ct)
        {
            if (chunkSize < 1) throw new ArgumentOutOfRangeException(nameof(chunkSize));

            var prop = ModelBase.FindProperty(_metadata, column);
            var savedOrder = new List<OrderItem>(_orders);
            _orders.Clear();
            _orders.Add(new OrderItem { Column = column, Direction = "ASC" });

            WhereClause? cursor = null;
            object? lastId = null;
            List<T> results;

            try
            {
                do
                {
                    if (cursor != null)
                        _whereClauses.Remove(cursor);

                    if (lastId != null)
                    {
                        cursor = CreateComparison(column, ">", lastId, "AND");
                        _whereClauses.Add(cursor);
                    }

                    results = await GetCoreAsync(chunkSize, null, useAsync, ct).ConfigureAwait(false);

                    if (results.Count > 0)
                    {
                        await callback(results).ConfigureAwait(false);
                        if (prop == null)
                            break;

                        // The cursor must move forward, otherwise the same page would come back forever
                        var next = prop.GetValue(results.Last());
                        if (next == null || (lastId != null && ModelMapper.KeyOf(next) == ModelMapper.KeyOf(lastId)))
                            throw new InvalidOperationException($"ChunkById: column '{column}' did not advance; it must be filled by the query and unique.");
                        lastId = next;
                    }
                } while (results.Count == chunkSize);
            }
            finally
            {
                if (cursor != null)
                    _whereClauses.Remove(cursor);
                _orders.Clear();
                _orders.AddRange(savedOrder);
            }

            return true;
        }

        #endregion

        #region SQL building

        private BuiltQuery BuildSelect(DbProviderType provider, IList<string>? rawColumns, int? limit, int? offset, bool aggregate)
        {
            int paramIndex = 0;
            var parameters = new Dictionary<string, object?>();
            var sql = BuildSelectSql(provider, rawColumns, limit, offset, aggregate, ref paramIndex, parameters);
            return new BuiltQuery(sql, parameters);
        }

        private string BuildSelectSql(DbProviderType provider, IList<string>? rawColumns, int? limit, int? offset, bool aggregate,
            ref int paramIndex, Dictionary<string, object?> parameters)
        {
            bool useTop = !aggregate && provider == DbProviderType.SqlServer && limit.HasValue && !offset.HasValue;
            var top = useTop ? $"TOP {limit.GetValueOrDefault()} " : string.Empty;
            var sb = new StringBuilder();

            if (_unions.Count > 0)
            {
                // Parts without ORDER BY / LIMIT; order, limit and the outer columns apply to the combined rows
                var parts = new StringBuilder(BuildUnionPartSql(provider, ref paramIndex, parameters));
                foreach (var union in _unions)
                {
                    parts.Append(union.All ? " UNION ALL " : " UNION ");
                    parts.Append(union.Query.BuildUnionPart(provider, ref paramIndex, parameters));
                }

                sb.Append("SELECT ").Append(top);
                sb.Append(rawColumns != null && rawColumns.Count > 0 ? string.Join(", ", rawColumns) : "*");
                sb.Append($" FROM ({parts}) mc_union");
            }
            else
            {
                sb.Append("SELECT ");
                if (_distinct) sb.Append("DISTINCT ");
                sb.Append(top);
                sb.Append(rawColumns != null && rawColumns.Count > 0
                    ? string.Join(", ", rawColumns)
                    : SelectList(provider, ref paramIndex, parameters));
                AppendFromWhereGroup(sb, provider, ref paramIndex, parameters);
            }

            if (aggregate)
                return sb.ToString();

            AppendOrderAndLimit(sb, provider, limit, offset, ref paramIndex, parameters);

            if (_lock != RowLock.None && _unions.Count == 0)
            {
                switch (provider)
                {
                    case DbProviderType.MySQL:
                    case DbProviderType.MariaDB:
                        sb.Append(_lock == RowLock.Update ? " FOR UPDATE" : " LOCK IN SHARE MODE");
                        break;
                    case DbProviderType.PostgreSQL:
                        sb.Append(_lock == RowLock.Update ? " FOR UPDATE" : " FOR SHARE");
                        break;
                }
            }
            return sb.ToString();
        }

        // SELECT [DISTINCT] columns FROM ... WHERE ... GROUP BY ... HAVING ... (no order / limit)
        private string BuildUnionPartSql(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            var sb = new StringBuilder("SELECT ");
            if (_distinct) sb.Append("DISTINCT ");
            sb.Append(SelectList(provider, ref paramIndex, parameters));
            AppendFromWhereGroup(sb, provider, ref paramIndex, parameters);
            return sb.ToString();
        }

        string ISubQuery.BuildUnionPart(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            return BuildUnionPartSql(provider, ref paramIndex, parameters);
        }

        string ISubQuery.BuildSubquery(DbProviderType provider, bool forIn, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            IList<string>? columns = null;
            if (forIn && _selectColumns.Count == 0)
            {
                var keys = _metadata.KeyProperties;
                if (keys.Count != 1)
                    throw new InvalidOperationException($"WhereIn subquery on {typeof(T).Name}: call Select(\"Column\") to choose the column.");
                columns = new[] { SqlDialect.Column(provider, keys[0].ColumnName) };
            }
            else if (!forIn && _selectColumns.Count == 0)
            {
                columns = new[] { "1" };
            }

            return BuildSelectSql(provider, columns, _limit, _offset, aggregate: false, ref paramIndex, parameters);
        }

        private string SelectList(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            if (_selectColumns.Count == 0)
                return "*";

            var items = new List<string>(_selectColumns.Count + 1);

            // Only WithCount / WithSum ... columns: all model columns plus them
            if (_selectColumns.All(c => c.Aggregate != null))
                items.Add("*");

            foreach (var item in _selectColumns)
            {
                if (item.Aggregate != null)
                    items.Add($"({AggregateSql(item, provider, ref paramIndex, parameters)}) AS {SqlDialect.Column(provider, item.Text)}");
                else
                    items.Add(item.Raw
                        ? BindRaw(item.Text, item.Bindings, ref paramIndex, parameters)
                        : SqlDialect.Column(provider, item.Text));
            }
            return string.Join(", ", items);
        }

        private static string AggregateSql(SelectItem item, DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            var sub = item.Aggregate!;
            switch (item.Function)
            {
                case "EXISTS":
                    return $"CASE WHEN EXISTS ({sub.Build(provider, "1", ref paramIndex, parameters)}) THEN 1 ELSE 0 END";
                case "COUNT":
                    return sub.Build(provider, "COUNT(*)", ref paramIndex, parameters);
                default:
                    return sub.Build(provider, $"{item.Function}({{alias}}.{SqlDialect.Column(provider, item.AggregateColumn!)})", ref paramIndex, parameters);
            }
        }

        private void AppendFromWhereGroup(StringBuilder sb, DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            sb.Append($" FROM {SqlDialect.Table(provider, _metadata.TableName)}");

            // SQL Server locks with table hints instead of FOR UPDATE
            if (_lock != RowLock.None && provider == DbProviderType.SqlServer)
                sb.Append(_lock == RowLock.Update ? " WITH (ROWLOCK, UPDLOCK, HOLDLOCK)" : " WITH (ROWLOCK, HOLDLOCK)");

            foreach (var join in _joins)
            {
                sb.Append($" {join.Type} JOIN {SqlDialect.Table(provider, join.Table)} ON {join.Condition}");
            }

            var where = BuildWhere(provider, ref paramIndex, parameters);
            if (!string.IsNullOrEmpty(where))
                sb.Append($" WHERE {where}");

            if (!string.IsNullOrEmpty(_groupBy))
                sb.Append($" GROUP BY {SqlDialect.ColumnList(provider, _groupBy!)}");

            if (_having != null)
            {
                if (_having.RawSql != null)
                {
                    sb.Append($" HAVING {BindRaw(_having.RawSql, _having.Bindings, ref paramIndex, parameters)}");
                }
                else
                {
                    var name = $"@p{paramIndex++}";
                    parameters[name] = _having.Value;
                    sb.Append($" HAVING {SqlDialect.Column(provider, _having.Column!)} {_having.Operator} {name}");
                }
            }
        }

        private List<string> OrderList(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            var orders = new List<string>(_orders.Count);
            foreach (var o in _orders)
            {
                if (o.Random)
                    orders.Add(RandomFunction(provider));
                else if (o.RawSql != null)
                    orders.Add(BindRaw(o.RawSql, o.Bindings, ref paramIndex, parameters));
                else
                    orders.Add($"{SqlDialect.Column(provider, o.Column!)} {o.Direction}");
            }
            return orders;
        }

        private void AppendOrderAndLimit(StringBuilder sb, DbProviderType provider, int? limit, int? offset,
            ref int paramIndex, Dictionary<string, object?> parameters)
        {
            var orders = OrderList(provider, ref paramIndex, parameters);

            switch (provider)
            {
                case DbProviderType.SqlServer:
                    if (offset.HasValue)
                    {
                        // OFFSET/FETCH needs ORDER BY in SQL Server
                        sb.Append(orders.Count > 0 ? $" ORDER BY {string.Join(", ", orders)}" : " ORDER BY (SELECT NULL)");
                        sb.Append($" OFFSET {offset.Value} ROWS");
                        if (limit.HasValue)
                            sb.Append($" FETCH NEXT {limit.Value} ROWS ONLY");
                    }
                    else if (orders.Count > 0)
                    {
                        sb.Append($" ORDER BY {string.Join(", ", orders)}");
                    }
                    break;

                default:
                    if (orders.Count > 0)
                        sb.Append($" ORDER BY {string.Join(", ", orders)}");

                    if (limit.HasValue)
                        sb.Append($" LIMIT {limit.Value}");
                    else if (offset.HasValue && provider != DbProviderType.PostgreSQL)
                        sb.Append(provider == DbProviderType.SQLite ? " LIMIT -1" : " LIMIT 18446744073709551615");

                    if (offset.HasValue)
                        sb.Append($" OFFSET {offset.Value}");
                    break;
            }
        }

        string? ISubQuery.Alias
        {
            get => _alias;
            set => _alias = value;
        }

        string ISubQuery.TableName => _metadata.TableName;

        string ISubQuery.BuildConditions(DbProviderType provider, bool withGlobalScopes, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            return BuildWhere(provider, ref paramIndex, parameters, withGlobalScopes);
        }

        string ISubQuery.BuildOrderBy(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            var orders = OrderList(provider, ref paramIndex, parameters);
            return orders.Count > 0 ? " ORDER BY " + string.Join(", ", orders) : string.Empty;
        }

        void ISubQuery.AddComparison(string column, string op, object? value)
        {
            _whereClauses.Add(CreateComparison(column, op, value, "AND"));
        }

        private string BuildWhere(DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters, bool globalScopes = true)
        {
            var userClauses = BuildClauses(_whereClauses, provider, ref paramIndex, parameters);

            // Filters every query of the model gets: soft delete, then [GlobalScope] attributes
            var filters = new List<string>();
            if (_metadata.HasSoftDelete)
            {
                var deleted = SqlDialect.Column(provider, _metadata.SoftDeleteColumn);
                if (_onlyTrashed)
                    filters.Add($"{deleted} IS NOT NULL");
                else if (!_withTrashed)
                    filters.Add($"{deleted} IS NULL");
            }

            if (!_withoutGlobalScopes && globalScopes)
            {
                foreach (var scope in GlobalScopeManager.GetScopes<T>())
                {
                    var scopeClause = CreateComparison(scope.Column, scope.Operator, scope.Value, "AND");
                    filters.Add(BuildClause(scopeClause, provider, ref paramIndex, parameters));
                }
            }

            if (filters.Count == 0)
                return userClauses;
            if (userClauses.Length > 0)
                return $"{string.Join(" AND ", filters)} AND ({userClauses})";
            return string.Join(" AND ", filters);
        }

        private static string BuildClauses(List<WhereClause> clauses, DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            var sb = new StringBuilder();
            foreach (var where in clauses)
            {
                var clause = BuildClause(where, provider, ref paramIndex, parameters);
                if (string.IsNullOrEmpty(clause))
                    continue;

                if (sb.Length > 0)
                    sb.Append($" {where.Logic} ");
                sb.Append(clause);
            }
            return sb.ToString();
        }

        private static string BuildClause(WhereClause where, DbProviderType provider, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            if (where.Group != null)
            {
                var inner = BuildClauses(where.Group, provider, ref paramIndex, parameters);
                if (inner.Length == 0)
                    return string.Empty;
                return where.Negate ? $"NOT ({inner})" : $"({inner})";
            }

            if (where.IsRaw)
                return $"({BindRaw(where.RawSql!, where.Bindings, ref paramIndex, parameters)})";

            if (where.IsExists)
            {
                var sub = where.SubQuery!.BuildSubquery(provider, false, ref paramIndex, parameters);
                return $"{(where.Negate ? "NOT EXISTS" : "EXISTS")} ({sub})";
            }

            if (where.CountQuery != null)
            {
                var counted = where.CountQuery.Build(provider, "COUNT(*)", ref paramIndex, parameters);
                var countParam = $"@p{paramIndex++}";
                parameters[countParam] = where.Value;
                return $"({counted}) {where.Operator} {countParam}";
            }

            var column = SqlDialect.Column(provider, where.Column!);

            if (where.SubQuery != null)
                return $"{column} {where.Operator} ({where.SubQuery.BuildSubquery(provider, true, ref paramIndex, parameters)})";

            if (where.IsColumn)
                return $"{column} {where.Operator} {SqlDialect.Column(provider, where.OtherColumn!)}";

            if (where.IsNull)
                return $"{column} {where.Operator}";

            if (where.IsIn)
            {
                var values = ((IEnumerable)where.Value!).Cast<object?>().ToList();
                if (values.Count == 0)
                {
                    // Empty IN () is a syntax error: IN matches nothing, NOT IN matches everything
                    return where.Operator == "IN" ? "1 = 0" : "1 = 1";
                }

                var paramNames = new List<string>();
                foreach (var val in values)
                {
                    var paramName = $"@p{paramIndex++}";
                    paramNames.Add(paramName);
                    parameters[paramName] = val;
                }
                return $"{column} {where.Operator} ({string.Join(", ", paramNames)})";
            }

            if (where.IsBetween)
            {
                var vals = (object?[])where.Value!;
                var p1 = $"@p{paramIndex++}";
                var p2 = $"@p{paramIndex++}";
                parameters[p1] = vals[0];
                parameters[p2] = vals[1];
                return $"{column} {where.Operator} {p1} AND {p2}";
            }

            if (where.IsDate)
            {
                var paramName = $"@p{paramIndex++}";
                parameters[paramName] = where.Value;
                return DateComparison(provider, column, paramName);
            }

            var name = $"@p{paramIndex++}";
            parameters[name] = where.Value;
            return where.LikeEscape
                ? $"{column} {where.Operator} {name} ESCAPE '!'"
                : $"{column} {where.Operator} {name}";
        }

        /// <summary>
        /// Replace each <c>?</c> outside quotes with a parameter (no bindings: the SQL is returned unchanged)
        /// </summary>
        internal static string BindRaw(string sql, object?[]? bindings, ref int paramIndex, Dictionary<string, object?> parameters)
        {
            if (bindings == null || bindings.Length == 0 || string.IsNullOrEmpty(sql))
                return sql;

            var sb = new StringBuilder(sql.Length + bindings.Length * 4);
            int used = 0;
            char quote = '\0';

            for (int i = 0; i < sql.Length; i++)
            {
                var c = sql[i];
                if (quote != '\0')
                {
                    sb.Append(c);
                    if (c == quote)
                        quote = '\0';
                    continue;
                }

                if (c == '\'' || c == '"' || c == '`')
                {
                    quote = c;
                    sb.Append(c);
                    continue;
                }

                if (c == '?')
                {
                    if (used >= bindings.Length)
                        throw new ArgumentException($"Raw SQL has more '?' placeholders than the {bindings.Length} values given: {sql}");

                    var name = $"@p{paramIndex++}";
                    parameters[name] = bindings[used++];
                    sb.Append(name);
                    continue;
                }

                sb.Append(c);
            }

            if (used != bindings.Length)
                throw new ArgumentException($"Raw SQL has {used} '?' placeholders but {bindings.Length} values were given: {sql}");

            return sb.ToString();
        }

        private static string DateComparison(DbProviderType provider, string column, string paramName)
        {
            switch (provider)
            {
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    return $"DATE({column}) = DATE({paramName})";
                case DbProviderType.SQLite:
                    return $"date({column}) = date({paramName})";
                default:
                    return $"CAST({column} AS DATE) = CAST({paramName} AS DATE)";
            }
        }

        private static string RandomFunction(DbProviderType provider)
        {
            switch (provider)
            {
                case DbProviderType.SqlServer:
                    return "NEWID()";
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    return "RAND()";
                default:
                    return "RANDOM()";
            }
        }

        // A model about to be filled from a row of this query (remembers the connection for Save())
        private T NewLoadedModel()
        {
            return new T { LoadedConnection = _connectionName };
        }

        private sealed class BuiltQuery
        {
            public BuiltQuery(string sql, Dictionary<string, object?> parameters)
            {
                Sql = sql;
                Parameters = parameters;
            }

            public string Sql { get; }
            public Dictionary<string, object?> Parameters { get; }
        }

        private sealed class SelectItem
        {
            public string Text { get; set; } = string.Empty;
            public bool Raw { get; set; }
            public object?[]? Bindings { get; set; }

            /// <summary>WithCount / WithSum ...: subquery over a relation, <see cref="Text"/> is the alias</summary>
            public RelationSubQuery? Aggregate { get; set; }
            public string? Function { get; set; }
            public string? AggregateColumn { get; set; }
        }

        private sealed class HavingClause
        {
            public string? Column { get; set; }
            public string? Operator { get; set; }
            public object? Value { get; set; }
            public string? RawSql { get; set; }
            public object?[]? Bindings { get; set; }
        }

        private sealed class OrderItem
        {
            public string? Column { get; set; }
            public string? Direction { get; set; }
            public bool Random { get; set; }
            public string? RawSql { get; set; }
            public object?[]? Bindings { get; set; }
        }

        private sealed class JoinItem
        {
            public JoinItem(string type, string table, string condition)
            {
                Type = type;
                Table = table;
                Condition = condition;
            }

            public string Type { get; }
            public string Table { get; }
            public string Condition { get; }
        }

        private sealed class UnionItem
        {
            public UnionItem(ISubQuery query, bool all)
            {
                Query = query;
                All = all;
            }

            public ISubQuery Query { get; }
            public bool All { get; }
        }

        #endregion
    }

    internal class WhereClause
    {
        public string? Column { get; set; }
        public string? Operator { get; set; }
        public object? Value { get; set; }
        public string Logic { get; set; } = "AND";
        public string? RawSql { get; set; }
        public object?[]? Bindings { get; set; }
        public bool IsRaw { get; set; }
        public bool IsNull { get; set; }
        public bool IsIn { get; set; }
        public bool IsBetween { get; set; }
        public bool IsDate { get; set; }

        /// <summary>Parenthesised group of clauses</summary>
        public List<WhereClause>? Group { get; set; }

        /// <summary>NOT (group) / NOT EXISTS</summary>
        public bool Negate { get; set; }

        /// <summary>IN (subquery) or EXISTS (subquery)</summary>
        public ISubQuery? SubQuery { get; set; }
        public bool IsExists { get; set; }

        /// <summary>Has(relation, op, count): (SELECT COUNT(*) ...) op value</summary>
        public RelationSubQuery? CountQuery { get; set; }

        /// <summary>LIKE whose pattern escapes % _ [ ! with '!' (lambda StartsWith / EndsWith / Contains)</summary>
        public bool LikeEscape { get; set; }

        /// <summary>Column compared with another column</summary>
        public bool IsColumn { get; set; }
        public string? OtherColumn { get; set; }
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
        public List<T>? Items { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }

        public int FirstItem => TotalCount == 0 ? 0 : (CurrentPage - 1) * PageSize + 1;
        public int LastItem => Math.Min(CurrentPage * PageSize, TotalCount);
    }

    /// <summary>
    /// Page of <c>SimplePaginate</c>: no total count, only whether a next page exists
    /// </summary>
    public class SimplePaginatedResult<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }

    /// <summary>
    /// Page of <c>CursorPaginate</c>: pass <see cref="NextCursor"/> / <see cref="PreviousCursor"/> back to move on
    /// (null when there is no such page). The cursors are opaque, URL-safe strings.
    /// </summary>
    public class CursorPaginatedResult<T>
    {
        public List<T> Items { get; set; } = new List<T>();
        public int PageSize { get; set; }
        public string? NextCursor { get; set; }
        public string? PreviousCursor { get; set; }
        public bool HasNextPage => NextCursor != null;
        public bool HasPreviousPage => PreviousCursor != null;
    }

    /// <summary>
    /// Cursor text of keyset paging: base64url(JSON { p: previous?, v: [values as invariant strings] })
    /// </summary>
    internal sealed class PageCursor
    {
        public bool Previous { get; private set; }
        public object?[] Values { get; private set; } = new object?[0];

        internal static string Encode(bool previous, IEnumerable<object?> values)
        {
            var payload = new Dictionary<string, object?>
            {
                ["p"] = previous,
                ["v"] = values.Select(Format).ToArray()
            };
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        internal static PageCursor Decode(string cursor, List<Type?> types)
        {
            try
            {
                var text = cursor.Replace('-', '+').Replace('_', '/');
                text = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
                using (var json = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(text)))
                {
                    var root = json.RootElement;
                    var items = root.GetProperty("v").EnumerateArray().ToList();
                    if (items.Count != types.Count)
                        throw new FormatException();

                    var values = new object?[items.Count];
                    for (int i = 0; i < items.Count; i++)
                        values[i] = items[i].ValueKind == System.Text.Json.JsonValueKind.Null ? null : Parse(items[i].GetString()!, types[i]);

                    return new PageCursor { Previous = root.GetProperty("p").GetBoolean(), Values = values };
                }
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                throw new ArgumentException("The cursor is not valid for this query.", nameof(cursor), ex);
            }
        }

        private static string? Format(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case DateTime dt:
                    return dt.ToString("o", CultureInfo.InvariantCulture);
                case DateTimeOffset dto:
                    return dto.ToString("o", CultureInfo.InvariantCulture);
                case IFormattable f:
                    return f.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString();
            }
        }

        private static object? Parse(string text, Type? type)
        {
            if (type == null)
                return text;

            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (target == typeof(DateTime))
                return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (target == typeof(DateTimeOffset))
                return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return ModelMapper.ConvertTo(text, target);
        }
    }
}
