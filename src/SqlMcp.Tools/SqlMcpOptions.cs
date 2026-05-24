namespace SqlMcp.Tools;

public sealed record SqlMcpOptions(
    string ConnectionUri,
    bool UseSsl,
    SqlPermissionOptions Permissions);

public sealed record SqlPermissionOptions(
    bool AllowWrite,
    bool AllowDelete,
    bool AllowDdl,
    bool AllowDropDatabase);
