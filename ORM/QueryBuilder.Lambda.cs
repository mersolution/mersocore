using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Type-safe conditions and columns: <c>User.Query().Where(u =&gt; u.Age &gt;= 18 &amp;&amp; u.Name.StartsWith("A")).OrderBy(u =&gt; u.Name)</c>.
    /// The expression becomes the same parameterised SQL as Where(column, value); values (also captured
    /// variables) are always parameters.
    /// </summary>
    public partial class QueryBuilder<T>
    {
        /// <summary>
        /// AND condition from a lambda. Supported: == != &lt; &gt; &lt;= &gt;=, &amp;&amp; || !, null checks, bool properties,
        /// string StartsWith / EndsWith / Contains / Equals, string.IsNullOrEmpty, list.Contains(u.Id) (IN), enums.
        /// </summary>
        public QueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
        {
            _whereClauses.Add(Translate(predicate, "AND"));
            return this;
        }

        /// <summary>
        /// OR condition from a lambda
        /// </summary>
        public QueryBuilder<T> OrWhere(Expression<Func<T, bool>> predicate)
        {
            _whereClauses.Add(Translate(predicate, "OR"));
            return this;
        }

        /// <summary>
        /// AND NOT (condition) from a lambda
        /// </summary>
        public QueryBuilder<T> WhereNot(Expression<Func<T, bool>> predicate)
        {
            var clause = Translate(predicate, "AND");
            _whereClauses.Add(new WhereClause { Group = new List<WhereClause> { clause }, Negate = true, Logic = "AND" });
            return this;
        }

        /// <summary>
        /// ORDER BY a property: <c>OrderBy(u =&gt; u.Name)</c>
        /// </summary>
        public QueryBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> key)
        {
            return OrderBy(ColumnOf(key?.Body, "OrderBy"), "ASC");
        }

        /// <summary>
        /// ORDER BY a property descending
        /// </summary>
        public QueryBuilder<T> OrderByDesc<TKey>(Expression<Func<T, TKey>> key)
        {
            return OrderBy(ColumnOf(key?.Body, "OrderByDesc"), "DESC");
        }

        /// <summary>
        /// SELECT these properties: <c>Select(u =&gt; u.Id, u =&gt; u.Name)</c>
        /// </summary>
        public QueryBuilder<T> Select(params Expression<Func<T, object?>>[] columns)
        {
            return Select((columns ?? new Expression<Func<T, object?>>[0]).Select(c => ColumnOf(c?.Body, "Select")).ToArray());
        }

        /// <summary>
        /// Values of one property, typed: <c>List&lt;string&gt; emails = User.Query().Pluck(u =&gt; u.Email)</c>
        /// </summary>
        public List<TValue> Pluck<TValue>(Expression<Func<T, TValue>> column)
        {
            return Pluck(ColumnOf(column?.Body, "Pluck")).Select(v => (TValue)ModelMapper.ConvertTo(v, typeof(TValue))!).ToList();
        }

        /// <summary>
        /// Values of one property, typed (async)
        /// </summary>
        public async Task<List<TValue>> PluckAsync<TValue>(Expression<Func<T, TValue>> column, CancellationToken cancellationToken = default)
        {
            var values = await PluckAsync(ColumnOf(column?.Body, "Pluck"), cancellationToken).ConfigureAwait(false);
            return values.Select(v => (TValue)ModelMapper.ConvertTo(v, typeof(TValue))!).ToList();
        }

        #region Translation

        private WhereClause Translate(Expression<Func<T, bool>> predicate, string logic)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            var clause = Node(predicate.Body, predicate.Parameters[0]);
            clause.Logic = logic;
            return clause;
        }

        private WhereClause Node(Expression e, ParameterExpression p)
        {
            switch (e.NodeType)
            {
                case ExpressionType.AndAlso:
                case ExpressionType.OrElse:
                {
                    var binary = (BinaryExpression)e;
                    var left = Node(binary.Left, p);
                    var right = Node(binary.Right, p);
                    left.Logic = "AND";
                    right.Logic = e.NodeType == ExpressionType.AndAlso ? "AND" : "OR";
                    return new WhereClause { Group = new List<WhereClause> { left, right } };
                }

                case ExpressionType.Not:
                {
                    var operand = ((UnaryExpression)e).Operand;
                    if (TryColumn(operand, p, out var boolColumn, out _))
                        return CreateComparison(boolColumn, "=", false, "AND");
                    return new WhereClause { Group = new List<WhereClause> { Node(operand, p) }, Negate = true };
                }

                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                    return Comparison((BinaryExpression)e, p);

                case ExpressionType.Call:
                    return Method((MethodCallExpression)e, p);

                case ExpressionType.Constant:
                    return new WhereClause { RawSql = Equals(((ConstantExpression)e).Value, true) ? "1 = 1" : "1 = 0", IsRaw = true };
            }

            // A bool property on its own: u.IsActive
            if (TryColumn(e, p, out var column, out _))
                return CreateComparison(column, "=", true, "AND");

            // Anything without the parameter is a value: true / false
            if (!UsesParameter(e, p) && e.Type == typeof(bool))
                return new WhereClause { RawSql = Equals(Evaluate(e), true) ? "1 = 1" : "1 = 0", IsRaw = true };

            throw Unsupported(e);
        }

        private WhereClause Comparison(BinaryExpression binary, ParameterExpression p)
        {
            var op = OperatorOf(binary.NodeType);

            if (TryColumn(binary.Left, p, out var leftColumn, out var leftType))
            {
                if (TryColumn(binary.Right, p, out var rightColumn, out _))
                    return ColumnClause(leftColumn, op, rightColumn, "AND");
                return CreateComparison(leftColumn, op, ColumnValue(Evaluate(binary.Right, p), leftType), "AND");
            }

            if (TryColumn(binary.Right, p, out var column, out var type))
                return CreateComparison(column, Flip(op), ColumnValue(Evaluate(binary.Left, p), type), "AND");

            throw Unsupported(binary);
        }

        private WhereClause Method(MethodCallExpression call, ParameterExpression p)
        {
            var method = call.Method;

            // string.IsNullOrEmpty(u.Name)
            if (method.DeclaringType == typeof(string) && method.Name == nameof(string.IsNullOrEmpty) && call.Arguments.Count == 1
                && TryColumn(call.Arguments[0], p, out var emptyColumn, out _))
            {
                var isNull = CreateComparison(emptyColumn, "=", null, "AND");
                var isEmpty = CreateComparison(emptyColumn, "=", string.Empty, "OR");
                return new WhereClause { Group = new List<WhereClause> { isNull, isEmpty } };
            }

            // u.Name.StartsWith("A") / EndsWith / Contains / Equals
            if (method.DeclaringType == typeof(string) && call.Object != null && TryColumn(call.Object, p, out var textColumn, out _))
            {
                if (call.Arguments.Count != 1)
                    throw new NotSupportedException($"Where(x => ...): {method.Name} with a comparison option cannot be translated; the database collation decides about case.");

                var text = Convert.ToString(Evaluate(call.Arguments[0], p), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                switch (method.Name)
                {
                    case nameof(string.StartsWith):
                        return Like(textColumn, EscapeLike(text) + "%");
                    case nameof(string.EndsWith):
                        return Like(textColumn, "%" + EscapeLike(text));
                    case nameof(string.Contains):
                        return Like(textColumn, "%" + EscapeLike(text) + "%");
                    case nameof(string.Equals):
                        return CreateComparison(textColumn, "=", text, "AND");
                }
            }

            // list.Contains(u.Id) / Enumerable.Contains(list, u.Id) → IN
            if (method.Name == "Contains")
            {
                Expression? source = null, item = null;
                if (method.IsStatic && call.Arguments.Count == 2)
                {
                    source = call.Arguments[0];
                    item = call.Arguments[1];
                }
                else if (!method.IsStatic && call.Object != null && call.Arguments.Count == 1)
                {
                    source = call.Object;
                    item = call.Arguments[0];
                }

                if (source != null && item != null && TryColumn(item, p, out var inColumn, out var inType) && !UsesParameter(source, p))
                {
                    var values = (Evaluate(source) as IEnumerable)?.Cast<object?>().Select(v => ColumnValue(v, inType)) ?? Enumerable.Empty<object?>();
                    return InClause(inColumn, values, "IN", "AND");
                }
            }

            // A method of values only (DateTime.Today.AddDays(-7) > ... is handled by Evaluate): a bool value
            if (!UsesParameter(call, p) && call.Type == typeof(bool))
                return new WhereClause { RawSql = Equals(Evaluate(call), true) ? "1 = 1" : "1 = 0", IsRaw = true };

            throw Unsupported(call);
        }

        private static WhereClause Like(string column, string pattern)
        {
            return new WhereClause { Column = column, Operator = "LIKE", Value = pattern, Logic = "AND", LikeEscape = true };
        }

        // % _ [ are wildcards in LIKE; '!' is the escape character of the generated clause
        private static string EscapeLike(string value)
        {
            return value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");
        }

        // u.Name, u.Age (also inside a conversion, or u.BirthDate.Value) → column name
        private bool TryColumn(Expression e, ParameterExpression p, out string column, out Type propertyType)
        {
            column = string.Empty;
            propertyType = typeof(object);

            e = StripConvert(e);
            if (e is MemberExpression nullable && nullable.Member.Name == "Value" && nullable.Expression != null
                && Nullable.GetUnderlyingType(nullable.Expression.Type) != null)
                e = StripConvert(nullable.Expression);

            if (e is MemberExpression member && member.Expression == p && member.Member is PropertyInfo property)
            {
                var prop = ModelBase.FindProperty(_metadata, property.Name);
                if (prop == null)
                    throw new NotSupportedException($"Where(x => ...): {typeof(T).Name}.{property.Name} is not a column.");
                column = prop.ColumnName;
                propertyType = property.PropertyType;
                return true;
            }
            return false;
        }

        private string ColumnOf(Expression? body, string method)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            var e = StripConvert(body);
            if (e is MemberExpression member && member.Expression is ParameterExpression && member.Member is PropertyInfo property)
            {
                var prop = ModelBase.FindProperty(_metadata, property.Name);
                return prop?.ColumnName ?? throw new NotSupportedException($"{method}(x => ...): {typeof(T).Name}.{property.Name} is not a column.");
            }
            throw new NotSupportedException($"{method}(x => ...) needs a property, e.g. x => x.Name (got {body}).");
        }

        private static Expression StripConvert(Expression e)
        {
            while (e.NodeType == ExpressionType.Convert || e.NodeType == ExpressionType.ConvertChecked)
                e = ((UnaryExpression)e).Operand;
            return e;
        }

        // Enum columns compare with enum values (the compiler turns them into numbers)
        private static object? ColumnValue(object? value, Type propertyType)
        {
            var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (value != null && type.IsEnum && !(value is Enum))
                return Enum.ToObject(type, value);
            return value;
        }

        private static object? Evaluate(Expression e, ParameterExpression p)
        {
            if (UsesParameter(e, p))
                throw Unsupported(e);
            return Evaluate(e);
        }

        // Value of an expression without the lambda parameter (constants, captured variables, method calls)
        private static object? Evaluate(Expression e)
        {
            switch (e)
            {
                case ConstantExpression constant:
                    return constant.Value;
                case MemberExpression member when member.Expression is ConstantExpression owner:
                    switch (member.Member)
                    {
                        case FieldInfo field:
                            return field.GetValue(owner.Value);
                        case PropertyInfo property:
                            return property.GetValue(owner.Value);
                    }
                    break;
            }

            return Expression.Lambda<Func<object?>>(Expression.Convert(e, typeof(object))).Compile()();
        }

        private static bool UsesParameter(Expression e, ParameterExpression p)
        {
            var finder = new ParameterFinder(p);
            finder.Visit(e);
            return finder.Found;
        }

        private static string OperatorOf(ExpressionType type)
        {
            switch (type)
            {
                case ExpressionType.Equal: return "=";
                case ExpressionType.NotEqual: return "!=";
                case ExpressionType.LessThan: return "<";
                case ExpressionType.LessThanOrEqual: return "<=";
                case ExpressionType.GreaterThan: return ">";
                default: return ">=";
            }
        }

        // value op column → column (flipped op) value
        private static string Flip(string op)
        {
            switch (op)
            {
                case "<": return ">";
                case "<=": return ">=";
                case ">": return "<";
                case ">=": return "<=";
                default: return op;
            }
        }

        private static NotSupportedException Unsupported(Expression e)
        {
            return new NotSupportedException(
                $"Where(x => ...): '{e}' cannot be translated to SQL. Compare properties with values, use StartsWith / EndsWith / Contains, list.Contains(x.Id), or Where(column, value) / WhereRaw.");
        }

        private sealed class ParameterFinder : ExpressionVisitor
        {
            private readonly ParameterExpression _parameter;

            public ParameterFinder(ParameterExpression parameter)
            {
                _parameter = parameter;
            }

            public bool Found { get; private set; }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _parameter)
                    Found = true;
                return node;
            }
        }

        #endregion
    }
}
