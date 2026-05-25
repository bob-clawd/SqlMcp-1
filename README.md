![plot](assets/icon.png)

# SqlMcp

> Give AI agents accurate SQL database schema and safe data access. No more schema guessing.

Agents working on a codebase only see the code — not the live database.
When they need schema or data, they guess, leading to wrong column names, wrong types, and missed foreign keys.

SqlMcp connects any MCP-compatible AI agent directly to your database.
Use a read-only database user for read-only access — SqlMcp does not enforce statement-level permissions.

## Supported Databases

| Database | Connection URI |
|---|---|
| **PostgreSQL** | `postgres://user:pass@host:5432/db` (or `postgresql://`) |
| **MySQL** | `mysql://user:pass@host:3306/db` |
| **SQLite** | `sqlite:./path/to/file.db` (or `file:./path` or just `*.db`/`*.sqlite`/`*.sqlite3`) |
| **SQL Server** | `mssql://user:pass@host:1433/db` |
| **Oracle** | `oracle://user:pass@host:1521/sid_or_service` |

The driver is auto-detected from the URI scheme.

## Get It as a .NET Tool

[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://www.nuget.org/packages/SqlMcp/)

```bash
dotnet tool install -g SqlMcp
```

## Quick Start

```bash
# PostgreSQL
sqlmcp --db 'postgres://user:pass@localhost:5432/db'

# MySQL
sqlmcp --db 'mysql://user:pass@localhost:3306/db'

# SQLite
sqlmcp --db 'sqlite:./path/to/db.sqlite'

# SQL Server
sqlmcp --db 'mssql://user:pass@localhost:1433/db'

# Oracle
sqlmcp --db 'oracle://user:pass@localhost:1521/sid_or_service'
```

## Configuration

| Flag | Default | Description |
| :--- | :--- | :--- |
| `--db <uri>` | required | Database connection URI |
| `--ssl` | false | Enable SSL/TLS for connection |

## Available Tools

| Tool | Description |
| :--- | :--- |
| `query` | Read-only SQL — SELECT, SHOW, DESCRIBE, EXPLAIN. |
| `execute` | Write SQL — INSERT, UPDATE, DELETE, ALTER, CREATE, DROP, TRUNCATE. Requires `confirm=true`. |
| `analyze_query` | EXPLAIN query plan. `execute=true` for actual timings (SELECT only). |
| `list_tables` | All tables and views in the database. |
| `describe_table` | Full schema: columns, indexes, foreign keys. |

## Security

The database connection user is the sole security boundary — use a read-only user for read-only access.

**Multi-statement queries** (e.g. `SELECT 1; DROP TABLE x`) are always blocked regardless of user privileges.

## Development

```bash
dotnet build SqlMcp.slnx
```
