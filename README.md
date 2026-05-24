![plot](assets/icon.png)

# SqlMcp

> Give AI agents accurate SQL database schema and safe data access. No more schema guessing.

Agents working on a codebase only see the code — not the live database.
When they need schema or data, they guess, leading to wrong column names, wrong types, and missed foreign keys.

SqlMcp connects any MCP-compatible AI agent directly to your **MySQL, PostgreSQL, or SQLite** database.
It is **read-only by default** and requires explicit opt-in for write operations.

## Supported Databases

| Database | Connection URI |
|---|---|
| **MySQL** | `mysql://user:pass@host:3306/db` |
| **PostgreSQL** | `postgres://user:pass@host:5432/db` (or `postgresql://`) |
| **SQLite** | `sqlite:./path/to/file.db` (or `file:./path` or just `*.db`/`*.sqlite`/`*.sqlite3`) |

The driver is auto-detected from the URI scheme.

## Get It as a .NET Tool

[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://www.nuget.org/packages/SqlMcp/)

```bash
dotnet tool install -g SqlMcp
```

## Quick Start

```bash
# MySQL
sqlmcp --db 'mysql://user:password@localhost:3306/mydb'

# PostgreSQL
sqlmcp --db 'postgres://user:password@localhost:5432/mydb'

# SQLite
sqlmcp --db 'sqlite:./mydb.sqlite'
```

Or with env vars (recommended — keeps credentials out of process listings):

```bash
DB_URL='postgres://user:password@localhost:5432/mydb' sqlmcp
```

## Configuration

| Flag | Env Var | Default | Description |
|---|---|---|---|
| `--db <uri>` | `DB_URL` | required | Database connection URI |
| `--ssl` | `SSL=true` | false | Enable SSL/TLS for connection |
| `--allow-write` | `ALLOW_WRITE=true` | false | Enable INSERT and UPDATE |
| `--allow-delete` | `ALLOW_DELETE=true` | false | Enable DELETE |
| `--allow-ddl` | `ALLOW_DDL=true` | false | Enable ALTER, CREATE, DROP, TRUNCATE |
| `--allow-drop-database` | `ALLOW_DROP_DATABASE=true` | false | Enable DROP DATABASE |

CLI flags take precedence over environment variables.

## Available Tools

| Tool | Description | Permission |
|---|---|---|
| `list_tables` | List all tables and views | Read-only (default) |
| `describe_table` | Full schema for one table: columns, indexes, foreign keys | Read-only (default) |
| `get_schema` | Database schema dump | Read-only (default) |
| `get_sample_data` | Sample N rows from a table | Read-only (default) |
| `query` | Execute a SQL statement | Depends on statement type |
| `analyze_query` | Show execution plan and detect common issues | Plan-only by default |

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
