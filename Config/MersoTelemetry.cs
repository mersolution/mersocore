using System;
using System.Data.Common;
using System.Diagnostics;
using mersolutionCore.Command.Abstractions;

namespace mersolutionCore.Config
{
    /// <summary>
    /// OpenTelemetry tracing: every SQL command is an Activity of the source <see cref="SourceName"/> with the
    /// database semantic convention tags (db.system.name, db.operation.name, db.query.text, db.namespace,
    /// server.address, error.type). Turn it on in the OpenTelemetry setup:
    /// <c>builder.Services.AddOpenTelemetry().WithTracing(t =&gt; t.AddSource(MersoTelemetry.SourceName))</c>.
    /// Without a listener nothing is recorded (no cost).
    /// </summary>
    public static class MersoTelemetry
    {
        /// <summary>ActivitySource name: "mersolutionCore"</summary>
        public const string SourceName = "mersolutionCore";

        internal static readonly ActivitySource Source = new ActivitySource(SourceName, typeof(MersoTelemetry).Assembly.GetName().Version?.ToString());

        /// <summary>
        /// Record the SQL text (db.query.text). Values are parameters, never part of the text. Default true.
        /// </summary>
        public static bool IncludeQueryText { get; set; } = true;

        internal static Activity? Start(DbCommand command, DbProviderType provider)
        {
            if (!Source.HasListeners())
                return null;

            var sql = command.CommandText ?? string.Empty;
            var operation = Operation(sql);
            var activity = Source.StartActivity(operation, ActivityKind.Client);
            if (activity == null)
                return null;

            var system = SystemName(provider);
            activity.SetTag("db.system.name", system);
            activity.SetTag("db.system", system);
            activity.SetTag("db.operation.name", operation);
            if (IncludeQueryText)
                activity.SetTag("db.query.text", sql);

            var connection = command.Connection;
            if (connection != null)
            {
                if (!string.IsNullOrEmpty(connection.Database))
                    activity.SetTag("db.namespace", connection.Database);
                if (!string.IsNullOrEmpty(connection.DataSource))
                    activity.SetTag("server.address", connection.DataSource);
            }
            return activity;
        }

        internal static void Stop(Activity? activity, Exception? error)
        {
            if (activity == null)
                return;

            if (error != null)
            {
                activity.SetTag("error.type", error.GetType().FullName);
                activity.SetStatus(ActivityStatusCode.Error, error.Message);
            }
            activity.Dispose();
        }

        // First keyword: SELECT, INSERT, UPDATE, DELETE, MERGE, CREATE ... (comments and WITH skipped)
        private static string Operation(string sql)
        {
            var text = sql.TrimStart();
            while (text.StartsWith("--", StringComparison.Ordinal) || text.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = text.StartsWith("--", StringComparison.Ordinal) ? text.IndexOf('\n') : text.IndexOf("*/", StringComparison.Ordinal) + 1;
                if (end <= 0)
                    return "SQL";
                text = text.Substring(end + 1).TrimStart();
            }

            var length = 0;
            while (length < text.Length && char.IsLetter(text[length]))
                length++;
            return length == 0 ? "SQL" : text.Substring(0, length).ToUpperInvariant();
        }

        private static string SystemName(DbProviderType provider)
        {
            switch (provider)
            {
                case DbProviderType.SqlServer: return "microsoft.sql_server";
                case DbProviderType.MySQL: return "mysql";
                case DbProviderType.MariaDB: return "mariadb";
                case DbProviderType.PostgreSQL: return "postgresql";
                case DbProviderType.SQLite: return "sqlite";
                default: return "other_sql";
            }
        }
    }
}
