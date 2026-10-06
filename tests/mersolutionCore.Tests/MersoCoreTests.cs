using System;
using System.Collections.Generic;
using Xunit;

// The library keeps its configuration in static state (default connection, cache, observers)
[assembly: CollectionBehavior(DisableTestParallelization = true)]

public class MersoCoreTests
{
    [Fact]
    public void Library() => AssertPassed(Suite.RunLibrary());

    [Fact]
    public void SqlDialects() => AssertPassed(Suite.RunDialects());

    /// <summary>
    /// SQLite always; the other databases when their MERSO_TEST_* connection string is set (see Suite)
    /// </summary>
    public static IEnumerable<object[]> Databases()
    {
        yield return new object[] { "SQLite" };

        if (IsSet(Suite.SqlServerEnv)) yield return new object[] { "SqlServer" };
        if (IsSet(Suite.MySqlEnv)) yield return new object[] { "MySQL" };
        if (IsSet(Suite.MariaDbEnv)) yield return new object[] { "MariaDB" };
        if (IsSet(Suite.PostgresEnv)) yield return new object[] { "PostgreSQL" };
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void Database(string provider) => AssertPassed(Suite.RunDatabase(provider));

    private static bool IsSet(string variable) => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));

    private static void AssertPassed(List<string> failures)
    {
        Assert.True(failures.Count == 0, "Failed checks:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }
}
