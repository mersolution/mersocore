using System;
using System.Linq;
using System.Text.RegularExpressions;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.ORM
{
    /// <summary>
    /// Identifier quoting per provider: [Order] (SQL Server), `Order` (MySQL / MariaDB),
    /// "order" (PostgreSQL — lower case, the same name an unquoted CREATE TABLE produced), "Order" (SQLite).
    /// Only plain names (Name, t.Name, dbo.Users, t.*, "Name AS n") are quoted; expressions such as
    /// COUNT(*) or DATE(CreatedAt) are left exactly as written.
    /// </summary>
    internal static class SqlDialect
    {
        private static readonly Regex SimpleName = new Regex(@"^[A-Za-z_][A-Za-z0-9_$]*$", RegexOptions.Compiled);
        private static readonly Regex ExplicitAlias = new Regex(@"^(\S+)\s+[Aa][Ss]\s+([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);
        private static readonly Regex TableAlias = new Regex(@"^(\S+)\s+(?:[Aa][Ss]\s+)?([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);

        /// <summary>
        /// Quote a column reference (Name, t.Name, t.*, "Name AS n"); anything else is returned unchanged
        /// </summary>
        internal static string Column(DbProviderType provider, string identifier)
        {
            if (!ModelBase.QuoteIdentifiers || string.IsNullOrWhiteSpace(identifier))
                return identifier;

            var name = identifier.Trim();
            if (TryQuotePath(provider, name, out var quoted))
                return quoted;

            var alias = ExplicitAlias.Match(name);
            if (alias.Success && TryQuotePath(provider, alias.Groups[1].Value, out var left))
                return left + " AS " + Part(provider, alias.Groups[2].Value);

            return identifier;
        }

        /// <summary>
        /// Quote a table reference (Users, dbo.Users, "orders o", "orders AS o")
        /// </summary>
        internal static string Table(DbProviderType provider, string table)
        {
            if (!ModelBase.QuoteIdentifiers || string.IsNullOrWhiteSpace(table))
                return table;

            var name = table.Trim();
            if (TryQuotePath(provider, name, out var quoted))
                return quoted;

            var alias = TableAlias.Match(name);
            if (alias.Success && TryQuotePath(provider, alias.Groups[1].Value, out var left))
                return left + " " + Part(provider, alias.Groups[2].Value);

            return table;
        }

        /// <summary>
        /// Quote a comma separated column list ("a, b"); left unchanged when it contains expressions
        /// </summary>
        internal static string ColumnList(DbProviderType provider, string columns)
        {
            if (!ModelBase.QuoteIdentifiers || string.IsNullOrWhiteSpace(columns) || columns.IndexOf('(') >= 0)
                return columns;

            return string.Join(", ", columns.Split(',').Select(c => Column(provider, c.Trim())));
        }

        /// <summary>
        /// Name as stored in the catalog (information_schema): PostgreSQL folds plain names to lower case
        /// </summary>
        internal static string CatalogName(DbProviderType provider, string name)
        {
            return provider == DbProviderType.PostgreSQL && SimpleName.IsMatch(name)
                ? name.ToLowerInvariant()
                : name;
        }

        private static bool TryQuotePath(DbProviderType provider, string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? quoted)
        {
            quoted = null;
            var parts = name.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                var isStar = parts[i] == "*" && i == parts.Length - 1 && i > 0;
                if (!isStar && !SimpleName.IsMatch(parts[i]))
                    return false;
            }

            quoted = string.Join(".", parts.Select(p => p == "*" ? p : Part(provider, p)));
            return true;
        }

        private static string Part(DbProviderType provider, string part)
        {
            switch (provider)
            {
                case DbProviderType.SqlServer:
                    return "[" + part + "]";
                case DbProviderType.MySQL:
                case DbProviderType.MariaDB:
                    return "`" + part + "`";
                case DbProviderType.PostgreSQL:
                    return "\"" + part.ToLowerInvariant() + "\"";
                default:
                    return "\"" + part + "\"";
            }
        }
    }
}
