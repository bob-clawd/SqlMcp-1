using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using SqlMcp.Tools.Drivers;
using SqlMcp.Tools.Security;

namespace SqlMcp.Tools;

public static class ServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection WithSqlMcp(SqlMcpOptions options) => services
            .AddInfrastructure(options)
            .AddTools();

        private IServiceCollection AddInfrastructure(SqlMcpOptions options) => services
            .AddSingleton(options)
            .AddSingleton(options.Permissions)
            .AddSingleton<ISqlStatementClassifier, SqlStatementClassifier>()
            .AddSingleton<IDatabaseDriver>(_ => DatabaseDriverFactory.Create(options.ConnectionUri, options.UseSsl));

        private IServiceCollection AddTools()
        {
            foreach (var type in GetTools())
                services.AddSingleton(type);

            return services;
        }
    }

    public static IEnumerable<Type> GetTools() => Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(IsTool)
        .Distinct();

    private static bool IsTool(Type type) =>
        type is { IsClass: true, IsAbstract: false } &&
        type.GetCustomAttribute<McpServerToolTypeAttribute>(false) is not null;
}
