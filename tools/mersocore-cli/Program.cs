using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using mersolutionCore.Command.Abstractions;
using mersolutionCore.Config;

namespace mersolutionCore.Cli
{
    internal static class Program
    {
        private const string Usage = @"mersocore — command line tool for mersolutionCore

Usage:
  mersocore scaffold --provider <provider> --connection <connection string> [options]

Generates one model class per table (properties, keys, timestamps, soft delete, relations from foreign keys).

Providers: sqlserver | mysql | mariadb | postgresql | sqlite
  (for SQLite --connection may be a file path)

Options:
  --output <dir>          Folder for the .cs files (default: Models)
  --namespace <name>      Namespace of the classes (default: Models)
  --tables <a,b,...>      Only these tables (default: all)
  --strip-prefix <text>   Remove a table name prefix from the class names (tbl_users → User)
  --date-only             DATE / TIME columns as DateOnly / TimeOnly (default DateTime / TimeSpan)
  --no-nullable           No nullable reference annotations (C# 7.3 / .NET Framework projects)
  --force                 Overwrite existing files

Examples:
  mersocore scaffold --provider postgresql --connection ""Host=localhost;Database=shop;Username=app;Password=..."" --namespace Shop.Models
  mersocore scaffold --provider sqlite --connection app.db --output Data/Models --strip-prefix tbl_";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help")
                {
                    Console.WriteLine(Usage);
                    return args.Length == 0 ? 1 : 0;
                }

                if (args[0] == "--version")
                {
                    Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
                    return 0;
                }

                if (!args[0].Equals("scaffold", StringComparison.OrdinalIgnoreCase))
                    return Fail($"Unknown command '{args[0]}'. Run 'mersocore --help'.");

                return Scaffold(Options(args.Skip(1).ToArray()));
            }
            catch (ArgumentException ex)
            {
                return Fail(ex.Message);
            }
            catch (Exception ex)
            {
                return Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static int Scaffold(Dictionary<string, string?> options)
        {
            var provider = Provider(Required(options, "provider"));
            var connection = Required(options, "connection");
            if (provider == DbProviderType.SQLite && connection.IndexOf('=') < 0)
                connection = "Data Source=" + connection;

            DbConfig.Configure(connection, provider);
            Func<DbCommandBase> connect = DbConfig.CreateConnection;

            var tables = SchemaReader.Read(connect, provider);
            if (options.TryGetValue("tables", out var only) && !string.IsNullOrWhiteSpace(only))
            {
                var wanted = new HashSet<string>(only!.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0), StringComparer.OrdinalIgnoreCase);
                tables = tables.Where(t => wanted.Contains(t.Name) || wanted.Contains(t.Schema + "." + t.Name)).ToList();
            }
            if (tables.Count == 0)
                return Fail("No tables found.");

            var scaffold = new ScaffoldOptions
            {
                Provider = provider,
                Namespace = Value(options, "namespace") ?? "Models",
                StripPrefix = Value(options, "strip-prefix") ?? string.Empty,
                DateOnly = options.ContainsKey("date-only"),
                Nullable = !options.ContainsKey("no-nullable")
            };

            var output = Value(options, "output") ?? "Models";
            Directory.CreateDirectory(output);
            var force = options.ContainsKey("force");

            int written = 0, skipped = 0;
            foreach (var file in ModelWriter.Write(tables, scaffold).OrderBy(f => f.Key))
            {
                var path = Path.Combine(output, file.Key);
                if (File.Exists(path) && !force)
                {
                    Console.WriteLine($"  skip   {path} (exists, use --force)");
                    skipped++;
                    continue;
                }
                File.WriteAllText(path, file.Value, new System.Text.UTF8Encoding(false));
                Console.WriteLine($"  write  {path}");
                written++;
            }

            Console.WriteLine($"{tables.Count} table(s): {written} file(s) written, {skipped} skipped.");
            return 0;
        }

        private static DbProviderType Provider(string name)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "sqlserver":
                case "mssql":
                    return DbProviderType.SqlServer;
                case "mysql":
                    return DbProviderType.MySQL;
                case "mariadb":
                    return DbProviderType.MariaDB;
                case "postgresql":
                case "postgres":
                case "pgsql":
                    return DbProviderType.PostgreSQL;
                case "sqlite":
                    return DbProviderType.SQLite;
                default:
                    throw new ArgumentException($"Unknown provider '{name}'. Use sqlserver, mysql, mariadb, postgresql or sqlite.");
            }
        }

        // --name value / --flag
        private static Dictionary<string, string?> Options(string[] args)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Unexpected argument '{args[i]}'.");

                var name = args[i].Substring(2);
                string? value = null;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    value = args[++i];
                result[name] = value;
            }
            return result;
        }

        private static string Required(Dictionary<string, string?> options, string name)
        {
            var value = Value(options, name);
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"--{name} is required. Run 'mersocore --help'.") : value!;
        }

        private static string? Value(Dictionary<string, string?> options, string name)
        {
            return options.TryGetValue(name, out var value) ? value : null;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine("error: " + message);
            return 1;
        }
    }
}
