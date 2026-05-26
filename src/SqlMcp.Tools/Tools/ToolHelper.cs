using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Tools;

internal static class ToolHelper
{
    public static async Task<T> RunAsync<T>(
        Func<Task<T>> action,
        Func<ErrorInfo, T> asError,
        Dictionary<string, string>? context = null)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return asError(new ErrorInfo(ex.Message, context));
        }
    }
}
