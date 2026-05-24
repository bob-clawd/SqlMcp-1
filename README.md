![plot](assets/icon.png)

# SqlMcp

> Give AI agents accurate SQL database schema and safe data access. No more schema guessing.

Agents working on a codebase only see the code — not the live database.
When they need schema or data, they guess, leading to wrong column names, wrong types, and missed foreign keys.

SqlMcp connects any MCP-compatible AI agent directly to your database.
It is **read-only by default** and requires explicit opt-in for write operations.

## Supported Databases

| Database | Connection URI |
|---|---|
| **PostgreSQL** | `postgres://user:pass@host:5432/db` (or `postgresql://`) |
| **MySQL** | `mysql://user:pass@host:3306/db` |
| **SQLite** | `sqlite:./path/to/file.db` (or `file:./path` or just `*.db`/`*.sqlite`/`*.sqlite3`) |
| **SQL Server** | `mssql://user:pass@host:1433/db` |
| **Oracle** | `oracle://user:pass@host:1521/service` |

The driver is auto-detected from the URI scheme.

## Get It as a .NET Tool

[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://www.nuget.org/packages/SqlMcp/)

```bash
dotnet tool install -g SqlMcp
```

## Quick Start

```bash
# PostgreSQL
sqlmcp --db 'postgres://user:password@localhost:5432/mydb'

# MySQL
sqlmcp --db 'mysql://user:password@localhost:3306/mydb'

# SQLite
sqlmcp --db 'sqlite:./mydb.sqlite'

# SQL Server
sqlmcp --db 'mssql://user:password@localhost:1433/mydb'

# Oracle
sqlmcp --db 'oracle://user:password@localhost:1521/XEPDB1'
```



## Configuration

| Flag | Default | Description |
| :--- | :--- | :--- |---|
| `--db <uri>` | required | Database connection URI |
| `--ssl` | false | Enable SSL/TLS for connection |
| `--allow-write` | false | Enable INSERT and UPDATE |
| `--allow-delete` | false | Enable DELETE |
| `--allow-ddl` | false | Enable ALTER, CREATE, DROP, TRUNCATE |
| `--allow-drop-database` | false | Enable DROP DATABASE |

## Available Tools

| Tool | Description | Permission |
| :--- | :--- | :--- |---|
| `execute_query` | Execute a SQL statement | Depends on statement type |
| `analyze_query` | Show raw execution plan for a query | Plan-only by default |
| `list_tables` | List all tables and views | Read-only (default) |
| `describe_table` | Full schema for one table: columns, indexes, foreign keys | Read-only (default) |

## Security Model

- **Default**: only `SELECT`, `SHOW`, `DESCRIBE`, `EXPLAIN` are allowed.
- **Write operations** (`INSERT`, `UPDATE`): require `--allow-write`.
- **Delete**: requires `--allow-delete`.
- **DDL** (`ALTER`, `CREATE`, `DROP`, `TRUNCATE`): requires `--allow-ddl`.
- **DROP DATABASE**: requires `--allow-drop-database`.
- **Multi-statement queries** (e.g. `SELECT 1; DROP TABLE x`): always blocked.

## Development

```bash
dotnet build SqlMcp.slnx
dotnet test SqlMcp.slnx
```
