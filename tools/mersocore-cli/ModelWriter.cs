using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Cli
{
    internal sealed class ScaffoldOptions
    {
        public string Namespace = "Models";
        public string StripPrefix = string.Empty;
        public bool DateOnly;
        public bool Nullable = true;
        public DbProviderType Provider;
    }

    /// <summary>
    /// Model class source code for each table: properties, [Table] / [Column] / [PrimaryKey], timestamps, soft
    /// delete, and [BelongsTo] / [HasMany] navigation properties from the foreign keys
    /// </summary>
    internal static class ModelWriter
    {
        private sealed class Model
        {
            public TableInfo Table = null!;
            public string ClassName = string.Empty;
            public Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.Ordinal); // column → property
            public HashSet<string> Members = new HashSet<string>(StringComparer.Ordinal);
        }

        public static Dictionary<string, string> Write(List<TableInfo> tables, ScaffoldOptions options)
        {
            var models = new Dictionary<string, Model>();
            var classNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in tables)
            {
                var name = Singular(Pascal(StripPrefix(table.Name, options.StripPrefix)));
                if (name.Length == 0) name = "Table";
                var unique = name;
                for (int n = 2; !classNames.Add(unique); n++)
                    unique = name + n.ToString(CultureInfo.InvariantCulture);

                var model = new Model { Table = table, ClassName = unique };
                model.Members.Add(unique);
                foreach (var column in table.Columns)
                {
                    var property = MemberName(PropertyName(column.Name), model);
                    model.Properties[column.Name] = property;
                }
                models[table.Key] = model;
            }

            var navigations = Navigations(models);
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in models.Values)
                files[model.ClassName + ".cs"] = Source(model, navigations.TryGetValue(model.Table.Key, out var nav) ? nav : new List<string>(), options);
            return files;
        }

        // [BelongsTo] on the child, [HasMany] on the parent, for every foreign key whose parent was scaffolded
        private static Dictionary<string, List<string>> Navigations(Dictionary<string, Model> models)
        {
            var result = new Dictionary<string, List<string>>();
            List<string> For(string key) => result.TryGetValue(key, out var list) ? list : (result[key] = new List<string>());

            foreach (var child in models.Values)
            {
                foreach (var fk in child.Table.ForeignKeys)
                {
                    var parent = models.Values.FirstOrDefault(m => m.Table.Name.Equals(fk.RefTable, StringComparison.OrdinalIgnoreCase)
                                                                   && (fk.RefSchema.Length == 0 || m.Table.Schema.Equals(fk.RefSchema, StringComparison.OrdinalIgnoreCase)));
                    if (parent == null)
                        continue;

                    var fkProps = fk.Columns.Select(c => child.Properties.TryGetValue(c, out var p) ? p : Pascal(c)).ToList();
                    var refProps = fk.RefColumns.Select(c => parent.Properties.TryGetValue(c, out var p) ? p : Pascal(c)).ToList();
                    var fkList = string.Join(", ", fkProps);
                    var parentKeys = parent.Table.Columns.Where(c => c.KeyOrder > 0).OrderBy(c => c.KeyOrder).Select(c => parent.Properties[c.Name]).ToList();
                    var pointsToKey = parentKeys.SequenceEqual(refProps);

                    // child.User → parent
                    var single = fkProps.Count == 1 && fkProps[0].Length > 2 && fkProps[0].EndsWith("Id", StringComparison.Ordinal)
                        ? fkProps[0].Substring(0, fkProps[0].Length - 2)
                        : parent.ClassName;
                    var belongsName = MemberName(single, child, "Ref");
                    var ownerKey = pointsToKey ? string.Empty : $", OwnerKey = \"{string.Join(", ", refProps)}\"";
                    For(child.Table.Key).Add(
                        $"    [BelongsTo(typeof({parent.ClassName}), \"{fkList}\"{ownerKey})]\n" +
                        $"    public {parent.ClassName}? {belongsName} {{ get; set; }}");

                    // parent.Orders → children (self relations too: Category.Categories / Children)
                    var many = Plural(child.ClassName);
                    var manyName = MemberName(many, parent, "By" + string.Concat(fkProps));
                    var localKey = pointsToKey ? string.Empty : $", LocalKey = \"{string.Join(", ", refProps)}\"";
                    For(parent.Table.Key).Add(
                        $"    [HasMany(typeof({child.ClassName}), \"{fkList}\"{localKey})]\n" +
                        $"    public List<{child.ClassName}> {manyName} {{ get; set; }} = new List<{child.ClassName}>();");
                }
            }
            return result;
        }

        private static string Source(Model model, List<string> navigations, ScaffoldOptions options)
        {
            var t = model.Table;
            var sb = new StringBuilder();
            sb.AppendLine("// Generated by mersocore scaffold — edit freely (re-running with --force overwrites).");
            if (options.Nullable)
                sb.AppendLine("#nullable enable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using mersolutionCore.ORM;");
            sb.AppendLine("using mersolutionCore.ORM.Entity;");
            sb.AppendLine();
            sb.AppendLine($"namespace {options.Namespace}");
            sb.AppendLine("{");

            var schema = ShowSchema(t.Schema, options.Provider) ? $", Schema = \"{Escape(t.Schema)}\"" : string.Empty;
            sb.AppendLine($"[Table(\"{Escape(t.Name)}\"{schema})]");
            sb.AppendLine($"public class {model.ClassName} : Model<{model.ClassName}>");
            sb.AppendLine("{");

            var keys = t.Columns.Count(c => c.KeyOrder > 0);
            var lines = new List<string>();
            foreach (var column in t.Columns)
            {
                var property = model.Properties[column.Name];
                var type = ClrType(column, options);
                var attributes = new List<string>();

                if (column.KeyOrder > 0)
                {
                    var args = new List<string>();
                    if (!column.Identity) args.Add("AutoIncrement = false");
                    if (keys > 1) args.Add("Order = " + column.KeyOrder.ToString(CultureInfo.InvariantCulture));
                    attributes.Add(args.Count == 0 ? "PrimaryKey" : $"PrimaryKey({string.Join(", ", args)})");
                }

                var columnArgs = new List<string>();
                if (!SameName(property, column.Name, options.Provider))
                    columnArgs.Add($"\"{Escape(column.Name)}\"");
                if (type.StartsWith("string", StringComparison.Ordinal) && column.Length > 0 && column.Length <= 4000 && IsSizedText(column))
                    columnArgs.Add("Length = " + column.Length.Value.ToString(CultureInfo.InvariantCulture));
                if (columnArgs.Count > 0)
                    attributes.Add($"Column({string.Join(", ", columnArgs)})");

                if (type.StartsWith("DateTime", StringComparison.Ordinal))
                {
                    var plain = Normalize(column.Name);
                    if (plain == "createdat") attributes.Add("CreatedAt");
                    else if (plain == "updatedat") attributes.Add("UpdatedAt");
                    else if (plain == "deletedat") attributes.Add("SoftDelete");
                }

                var nullable = column.Nullable || (column.KeyOrder == 0 && Normalize(column.Name) == "deletedat");
                var reference = type == "string" || type == "byte[]";
                var declared = type + (nullable && (!reference || options.Nullable) ? "?" : string.Empty);
                var init = !nullable && type == "string" ? " = string.Empty;" : !nullable && type == "byte[]" ? " = Array.Empty<byte>();" : string.Empty;

                var line = new StringBuilder();
                if (attributes.Count > 0)
                    line.AppendLine($"    [{string.Join(", ", attributes)}]");
                line.Append($"    public {declared} {property} {{ get; set; }}{init}");
                lines.Add(line.ToString());
            }

            sb.AppendLine(string.Join("\n\n", lines));
            if (navigations.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("    // Relations (fill with Query().With(\"Name\"))");
                var navigationText = string.Join("\n\n", navigations);
                sb.AppendLine(options.Nullable ? navigationText : navigationText.Replace("? ", " "));
            }
            sb.AppendLine("}");
            sb.AppendLine("}");

            // Members one level deeper inside the namespace block
            var text = sb.ToString().Replace("\r\n", "\n");
            var start = text.IndexOf("\n{\n", StringComparison.Ordinal) + 3;
            var body = text.Substring(start, text.Length - start - 2);
            body = string.Join("\n", body.Split('\n').Select(l => l.Length == 0 ? l : "    " + l));
            return text.Substring(0, start) + body + "}\n";
        }

        private static string ClrType(ColumnInfo column, ScaffoldOptions options)
        {
            var type = column.DataType;
            var full = column.FullType;

            switch (type)
            {
                case "bit":
                case "bool":
                case "boolean":
                    return "bool";
                case "tinyint":
                    // MySQL / MariaDB tinyint(1) is a bool
                    return full.StartsWith("tinyint(1)", StringComparison.Ordinal) ? "bool" : "byte";
                case "smallint":
                case "int2":
                case "year":
                    return "short";
                case "int":
                case "integer":
                case "int4":
                case "mediumint":
                case "serial":
                    return full.Contains("unsigned") ? "long" : "int";
                case "bigint":
                case "int8":
                case "bigserial":
                    return full.Contains("unsigned") ? "decimal" : "long";
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                    return "decimal";
                case "float":
                case "double":
                case "double precision":
                case "float8":
                    return "double";
                case "real":
                case "float4":
                    return options.Provider == DbProviderType.SQLite ? "double" : "float";
                case "date":
                    return options.DateOnly ? "DateOnly" : "DateTime";
                case "time":
                case "time without time zone":
                    return options.DateOnly ? "TimeOnly" : "TimeSpan";
                case "timestamp" when options.Provider == DbProviderType.SqlServer:
                    return "byte[]";   // SQL Server timestamp = rowversion
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "timestamp":
                case "timestamp without time zone":
                case "timestamp with time zone":
                case "timestamptz":
                    return "DateTime";
                case "datetimeoffset":
                    return "DateTimeOffset";
                case "uniqueidentifier":
                case "uuid":
                    return "Guid";
                case "binary":
                case "varbinary":
                case "image":
                case "blob":
                case "tinyblob":
                case "mediumblob":
                case "longblob":
                case "bytea":
                case "rowversion":
                    return "byte[]";
                default:
                    return "string";
            }
        }

        private static bool IsSizedText(ColumnInfo column)
        {
            var t = column.DataType;
            return t == "varchar" || t == "nvarchar" || t == "char" || t == "nchar" || t == "character varying" || t == "character";
        }

        // Dotted schema names only where they are not the default
        private static bool ShowSchema(string schema, DbProviderType provider)
        {
            if (string.IsNullOrEmpty(schema))
                return false;
            switch (provider)
            {
                case DbProviderType.SqlServer: return !schema.Equals("dbo", StringComparison.OrdinalIgnoreCase);
                case DbProviderType.PostgreSQL: return !schema.Equals("public", StringComparison.OrdinalIgnoreCase);
                default: return false;   // MySQL / MariaDB: the schema is the database itself
            }
        }

        // Case only differs: PostgreSQL (quoted lower case), SQLite and MySQL / MariaDB column names ignore case;
        // SQL Server keeps the exact name (a case-sensitive collation is possible)
        private static bool SameName(string property, string column, DbProviderType provider)
        {
            return provider == DbProviderType.SqlServer
                ? string.Equals(property, column, StringComparison.Ordinal)
                : string.Equals(property, column, StringComparison.OrdinalIgnoreCase);
        }

        private static string PropertyName(string column)
        {
            var plain = Normalize(column);
            switch (plain)
            {
                case "id": return "Id";
                case "createdat": return "CreatedAt";
                case "updatedat": return "UpdatedAt";
                case "deletedat": return "DeletedAt";
            }

            var pascal = Pascal(column);
            // userid (PostgreSQL folds to lower case) → UserId
            if (column.All(c => !char.IsUpper(c)) && column.IndexOf('_') < 0 && pascal.Length > 2 && pascal.EndsWith("id", StringComparison.Ordinal))
                pascal = pascal.Substring(0, pascal.Length - 2) + "Id";
            return pascal;
        }

        // A free member name: not the class name, no clash with columns / other navigations
        private static string MemberName(string wanted, Model model, string suffix = "Value")
        {
            var name = wanted;
            if (model.Members.Contains(name))
                name = wanted + suffix;
            for (int n = 2; model.Members.Contains(name); n++)
                name = wanted + suffix + n.ToString(CultureInfo.InvariantCulture);
            model.Members.Add(name);
            return name;
        }

        internal static string Pascal(string name)
        {
            var sb = new StringBuilder();
            var upper = true;
            foreach (var c in name)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    upper = true;
                    continue;
                }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            var result = sb.ToString();
            return result.Length > 0 && char.IsDigit(result[0]) ? "_" + result : result;
        }

        private static string StripPrefix(string name, string prefix)
        {
            return prefix.Length > 0 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length
                ? name.Substring(prefix.Length)
                : name;
        }

        internal static string Singular(string name)
        {
            if (name.EndsWith("ies", StringComparison.Ordinal) && name.Length > 4) return name.Substring(0, name.Length - 3) + "y";
            if (name.EndsWith("sses", StringComparison.Ordinal) || name.EndsWith("xes", StringComparison.Ordinal) || name.EndsWith("ches", StringComparison.Ordinal) || name.EndsWith("shes", StringComparison.Ordinal))
                return name.Substring(0, name.Length - 2);
            if (name.EndsWith("s", StringComparison.Ordinal) && !name.EndsWith("ss", StringComparison.Ordinal) && !name.EndsWith("us", StringComparison.Ordinal) && name.Length > 3)
                return name.Substring(0, name.Length - 1);
            return name;
        }

        internal static string Plural(string name)
        {
            if (name.EndsWith("y", StringComparison.Ordinal) && name.Length > 1 && "aeiou".IndexOf(name[name.Length - 2]) < 0)
                return name.Substring(0, name.Length - 1) + "ies";
            if (name.EndsWith("s", StringComparison.Ordinal) || name.EndsWith("x", StringComparison.Ordinal) || name.EndsWith("ch", StringComparison.Ordinal) || name.EndsWith("sh", StringComparison.Ordinal))
                return name + "es";
            return name + "s";
        }

        private static string Normalize(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
