using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using SqlMcp.Tools;

namespace SqlMcp.Host;

public static class HostExtensions
{
    internal static string ServerVersion => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HostExtensions).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    internal static IServiceCollection Compose(this IServiceCollection services, SqlMcpOptions options) => services
        .WithSqlMcp(options)
        .AddHostedService<DatabaseProbeService>()
        .AddMcpRuntime();

    private static IServiceCollection AddMcpRuntime(this IServiceCollection services)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            WriteIndented = true
        };

        var builder = services.AddMcpServer(serverOptions =>
        {
            serverOptions.ServerInfo = new Implementation
            {
                Name = "SqlMcp",
                Version = ServerVersion
            };
        });

        builder.WithStdioServerTransport();
        builder.WithTools(ServiceExtensions.GetTools(), serializerOptions);

        return services;
    }
}
