# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 2.x     | Yes — security fixes are released as 2.x patch versions |
| 1.x     | No — please upgrade to 2.x |

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Send an e-mail to **hello@mersolution.com** with:

- the affected version(s) and target framework (`netstandard2.0` / `net8.0`),
- the database provider involved (SQL Server, MySQL, MariaDB, PostgreSQL, SQLite),
- steps or a small code sample to reproduce,
- the impact you expect (for example SQL injection, data exposure, broken authentication of the JWT helper).

You will get an answer within 5 working days. A fix is prepared privately and released as soon as it is ready;
the reporter is credited in the CHANGELOG unless they prefer otherwise.

## Scope notes

- Values passed to `Where`, `WhereRaw(sql, bindings)`, `SelectRaw`, `OrderByRaw`, `HavingRaw` and `RawQuery` parameters are
  sent as database parameters. The SQL text of the `*Raw` methods and of `RawQuery` itself is executed as written —
  never build it from user input.
- Column and table names given as strings are quoted per provider but are not meant for user input either.
- `[Encrypted]` columns are only as safe as `EncryptedStringConverter.Key`: load it from a secret store, never
  commit it. Changing the key makes existing values unreadable.
