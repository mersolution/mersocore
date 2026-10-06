# Contributing to mersolutionCore

Thanks for helping! Bug reports, documentation fixes and pull requests are welcome.

## Build

Requirements: the .NET 10 SDK (it builds both targets, `netstandard2.0` and `net8.0`).

```bash
dotnet build mersolutionCore.csproj -c Release
```

The build must stay at **0 warnings** on both targets (nullable reference types are enabled).

The `mersocore` command line tool lives in `tools/mersocore-cli` and references the library project:

```bash
dotnet run --project tools/mersocore-cli -- scaffold --provider sqlite --connection app.db --output /tmp/models
```

Its `<Version>` moves together with the library's: the release workflow checks that both match the tag.

## Test

```bash
# Library checks, SQL of every dialect, SQLite
dotnet test tests/mersolutionCore.Tests

# The same against the netstandard2.0 build of the library
dotnet test tests/mersolutionCore.Tests -p:LibTfm=ns
```

The other databases run when their admin connection string is set; the tests create and drop a database named
`mersocore_test`. Start throwaway servers with Docker (same passwords as the CI):

```bash
docker compose -f tests/docker-compose.yml up -d

export MERSO_TEST_SQLSERVER="Server=127.0.0.1,1433;User ID=sa;Password=Merso_test_1!;TrustServerCertificate=true"
export MERSO_TEST_MYSQL="Server=127.0.0.1;Port=3306;User ID=root;Password=merso_test"
export MERSO_TEST_MARIADB="Server=127.0.0.1;Port=3307;User ID=root;Password=merso_test"
export MERSO_TEST_POSTGRES="Host=127.0.0.1;Port=5432;Username=postgres;Password=merso_test"
dotnet test tests/mersolutionCore.Tests

docker compose -f tests/docker-compose.yml down
```

`MERSO_TEST_TRACE=1` prints every check before it runs (useful when one hangs).

The tests run under the **tr-TR** culture on purpose (decimal comma, dotless i): the library must not depend on the
current culture. Add a check to `tests/mersolutionCore.Tests/Suite.cs` for every fix or feature — a database check
when SQL is involved, so it runs on all five databases.

## Pull requests

- One topic per pull request; describe the problem and how you tested it.
- Keep the public API compatible within a major version; note behaviour changes in `CHANGELOG.md`.
- Generated SQL must use parameters for values and `SqlDialect` for identifiers.
- Sync and async methods share one code path (`useAsync` flag, see `DbRun`) — please keep it that way.
- Update `DOCUMENTATION.md` / `README.md` when you add or change a feature.

## Code style

C# `latest`, 4 spaces, XML doc comments on public members; follow the style of the file you change.
