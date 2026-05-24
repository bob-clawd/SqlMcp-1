using System.Net;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using DatabaseDriver = SqlMcp.Tools.Drivers.Postgres.DatabaseDriver;

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
            return new MySql.DatabaseDriver(cs);
        }

        if (connectionUri.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var cs = BuildPostgresConnectionString(connectionUri, ssl);
            return new DatabaseDriver(cs);
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

            return new Sqlite.DatabaseDriver(path);
        }

        if (connectionUri.StartsWith("mssql://", StringComparison.OrdinalIgnoreCase) ||
            connectionUri.StartsWith("sqlserver://", StringComparison.OrdinalIgnoreCase))
        {
            var cs = BuildMssqlConnectionString(connectionUri, ssl);
            return new Mssql.DatabaseDriver(cs);
        }

        if (connectionUri.StartsWith("oracle://", StringComparison.OrdinalIgnoreCase))
        {
            var cs = BuildOracleConnectionString(connectionUri, ssl);
            return new Oracle.DatabaseDriver(cs);
        }

        throw new ArgumentException(
            "Unsupported database URI. Supported schemes: mysql://, postgres:// (postgresql://), mssql:// (sqlserver://), oracle://, sqlite: (file:) or a *.db/*.sqlite/*.sqlite3 path.",
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

    private static string BuildMssqlConnectionString(string uriText, bool ssl)
    {
        var uri = new Uri(uriText);
        var db = uri.AbsolutePath.Trim('/');

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(0) ?? "");
        var pass = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(1) ?? "");

        var b = new SqlConnectionStringBuilder
        {
            DataSource = uri.Host + (uri.Port > 0 ? $",{uri.Port}" : ",1433"),
            UserID = string.IsNullOrEmpty(user) ? null : user,
            Password = string.IsNullOrEmpty(pass) ? null : pass,
            InitialCatalog = db,
            TrustServerCertificate = ssl,
            Encrypt = ssl,
        };

        if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass))
            b.IntegratedSecurity = true;

        return b.ConnectionString;
    }

    private static string BuildOracleConnectionString(string uriText, bool ssl)
    {
        var uri = new Uri(uriText);
        var serviceName = uri.AbsolutePath.Trim('/');

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(0) ?? "");
        var pass = WebUtility.UrlDecode(userInfo.ElementAtOrDefault(1) ?? "");

        var b = new OracleConnectionStringBuilder
        {
            DataSource = $"{uri.Host}:{(uri.Port > 0 ? uri.Port : 1521)}/{serviceName}",
            UserID = string.IsNullOrEmpty(user) ? null : user,
            Password = string.IsNullOrEmpty(pass) ? null : pass,
        };

        return b.ConnectionString;
    }
}