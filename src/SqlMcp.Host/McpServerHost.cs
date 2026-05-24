using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlMcp.Tools;

namespace SqlMcp.Host;

public static class McpServerHost
{
    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var options = ParseOptions(args);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();

        builder.Services.Compose(options);

        var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static SqlMcpOptions ParseOptions(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? dbUri = null;
        var ssl = false;
        var allowWrite = false;
        var allowDelete = false;
        var allowDdl = false;
        var allowDropDatabase = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--db":
                    if (dbUri is not null)
                        throw new ArgumentException("The '--db' option may only be specified once.", nameof(args));

                    if (index + 1 >= args.Length)
                        throw new ArgumentException("Missing value for '--db'. Expected '--db <connection-uri>'.", nameof(args));

                    dbUri = args[++index];
                    if (string.IsNullOrWhiteSpace(dbUri))
                        throw new ArgumentException("The '--db' value must not be empty or whitespace.", nameof(args));

                    break;

                case "--ssl":
                    ssl = true;
                    break;

                case "--allow-write":
                    allowWrite = true;
                    break;

                case "--allow-delete":
                    allowDelete = true;
                    break;

                case "--allow-ddl":
                    allowDdl = true;
                    break;

                case "--allow-drop-database":
                    allowDropDatabase = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.", nameof(args));
            }
        }

        if (string.IsNullOrWhiteSpace(dbUri))
        {
            Console.Error.WriteLine(string.Join('\n',
                "sqlmcp: database connection URI is required.",
                "",
                "Usage:",
                "  sqlmcp --db <connection-uri> [options]",
                "",
                "Supported databases:",
                "  PostgreSQL: postgres://user:pass@host:5432/db (or postgresql://)",
                "  MySQL:      mysql://user:pass@host:3306/db",
                "  SQLite:     sqlite:./path/to/file.db (or file:./path or *.db/*.sqlite/*.sqlite3)",
                "  SQL Server: mssql://user:pass@host:1433/db",
                "  Oracle:     oracle://user:pass@host:1521/service",
                "",
                "Options:",
                "  --ssl                   Enable SSL/TLS",
                "  --allow-write           Enable INSERT and UPDATE",
                "  --allow-delete          Enable DELETE",
                "  --allow-ddl             Enable ALTER, CREATE, DROP, TRUNCATE",
                "  --allow-drop-database   Enable DROP DATABASE"));

            Environment.Exit(1);
        }

        return new SqlMcpOptions(
            ConnectionUri: dbUri!,
            UseSsl: ssl,
            Permissions: new SqlPermissionOptions(
                AllowWrite: allowWrite,
                AllowDelete: allowDelete,
                AllowDdl: allowDdl,
                AllowDropDatabase: allowDropDatabase));
    }
}
