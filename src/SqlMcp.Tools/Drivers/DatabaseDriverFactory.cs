using System.Net;
using MySqlConnector;
using Npgsql;

namespace SqlMcp.Tools.Drivers;

public static class DatabaseDriverFactory
{
    public static IDatabaseDriver Create(string connectionUri, bool ssl)
    {
        if (string.IsNullOrWhiteSpace(connectionUri))
            throw new ArgumentException("Connection URI must not be empty.", nameof(connectionUri));

        if (connectionUri.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("mysql2://", StringComparison.OrdinalIgnoreCase))
        {
            var cs = BuildMySqlConnectionString(connectionUri, ssl);
            return new MySqlDatabaseDriver(cs);
        }

        if (connectionUri.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var cs = BuildPostgresConnectionString(connectionUri, ssl);
            return new PostgresDatabaseDriver(cs);
        }

        if (connectionUri.StartsWith("sqlite:", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            HasSqliteExtension(connectionUri))
        {
            var path = connectionUri;
            path = path.Replace("sqlite:", "", StringComparison.OrdinalIgnoreCase);
            path = path.Replace("file:", "", StringComparison.OrdinalIgnoreCase);
            path = path.Trim();

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("SQLite path must not be empty.", nameof(connectionUri));

            return new SqliteDatabaseDriver(path);
        }

        throw new ArgumentException(
            "Unsupported database URI. Supported schemes: mysql://, postgres:// (postgresql://), sqlite: (file:) or a *.db/*.sqlite/*.sqlite3 path.",
            nameof(connectionUri));
    }

    private static bool HasSqliteExtension(string value) =>
        value.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase);

    private static string BuildMySqlConnectionString(string uriText, bool ssl)
    {
        var uri = new Uri(uriText);
        var db = uri.AbsolutePath.Trim('/');

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(0) ?? "");
        var pass = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(1) ?? "");

        var b = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = uri.Port > 0 ? (uint)uri.Port : 3306,
            UserID = user,
            Password = pass,
            Database = db,
            AllowUserVariables = false,
        };

        if (ssl)
        {
            // "Required" keeps it simple; stricter modes need CA/certs.
            b.SslMode = MySqlSslMode.Required;
        }

        return b.ConnectionString;
    }

    private static string BuildPostgresConnectionString(string uriText, bool ssl)
    {
        var uri = new Uri(uriText);
        var db = uri.AbsolutePath.Trim('/');

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(0) ?? "");
        var pass = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(1) ?? "");

        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = user,
            Password = pass,
            Database = db,
        };

        if (ssl)
        {
            b.SslMode = SslMode.Require;
            b.TrustServerCertificate = false;
        }

        return b.ConnectionString;
    }
}
