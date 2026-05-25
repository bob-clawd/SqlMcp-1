namespace SqlMcp.Tools;

public sealed record SqlMcpOptions(
    string ConnectionUri,
    bool UseSsl);
