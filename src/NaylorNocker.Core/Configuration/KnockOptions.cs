using System.Net;
namespace NaylorNocker.Core.Configuration;
public sealed record KnockOptions(string BindAddress, int ListenPort, string ExpectedMessage, string LogLevel);
public sealed record ValidatedKnockOptions(IPAddress BindAddress, int ListenPort, byte[] ExpectedMessageBytes, string LogLevel);
public sealed class ConfigurationException(string field, string category)
    : Exception($"Invalid configuration ({field}: {category}).")
{
    public string Field { get; } = field;
    public string Category { get; } = category;
}
