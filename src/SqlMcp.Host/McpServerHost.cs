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

        string? dbUri = Environment.GetEnvironmentVariable("DB_URL");
        var ssl = string.Equals(Environment.GetEnvironmentVariable("SSL"), "true", StringComparison.OrdinalIgnoreCase);

        var allowWrite = string.Equals(Environment.GetEnvironmentVariable("ALLOW_WRITE"), "true", StringComparison.OrdinalIgnoreCase);
        var allowDelete = string.Equals(Environment.GetEnvironmentVariable("ALLOW_DELETE"), "true", StringComparison.OrdinalIgnoreCase);
        var allowDdl = string.Equals(Environment.GetEnvironmentVariable("ALLOW_DDL"), "true", StringComparison.OrdinalIgnoreCase);
        var allowDropDatabase = string.Equals(Environment.GetEnvironmentVariable("ALLOW_DROP_DATABASE"), "true", StringComparison.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--db":
                    if (dbUri is not null)
                        throw new ArgumentException("The '--db' option may only be specified once (or set DB_URL).", nameof(args));

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
                "  MySQL:      mysql://user:pass@host:3306/db",
                "  PostgreSQL: postgres://user:pass@host:5432/db (or postgresql://)",
                "  SQLite:     sqlite:./path/to/file.db (or file:./path or *.db/*.sqlite/*.sqlite3)",
                "",
                "Environment variables:",
                "  DB_URL, SSL=true, ALLOW_WRITE=true, ALLOW_DELETE=true, ALLOW_DDL=true, ALLOW_DROP_DATABASE=true",
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
