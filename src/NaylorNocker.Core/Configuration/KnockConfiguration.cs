using System.Net;
using System.Text;
using System.Text.Json;
using NaylorNocker.Core.Protocol;
namespace NaylorNocker.Core.Configuration;
public static class KnockConfiguration
{
    public static ValidatedKnockOptions Load(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath) || !Path.IsPathFullyQualified(absolutePath))
            throw new ConfigurationException("ConfigPath", "absolute-path-required");
        string text;
        try { text = File.ReadAllText(absolutePath, new UTF8Encoding(false, true)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException)
        { throw new ConfigurationException("File", "unreadable-or-invalid-encoding"); }
        try { return Parse(text); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or EncoderFallbackException)
        { throw new ConfigurationException("File", "malformed-json-or-encoding"); }
    }

    private static ValidatedKnockOptions Parse(string text)
    {
        using var document = JsonDocument.Parse(text, new() { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ConfigurationException("Schema", "object-required");
        Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal);
        string[] allowed = ["BindAddress", "ListenPort", "ExpectedMessage", "LogLevel"];
        foreach (var property in document.RootElement.EnumerateObject())
            if (!allowed.Contains(property.Name) || !fields.TryAdd(property.Name, property.Value))
                throw new ConfigurationException("Schema", "unknown-or-duplicate-field");
        if (!fields.TryGetValue("ListenPort", out var portElement) ||
            portElement.ValueKind != JsonValueKind.Number || !portElement.TryGetInt32(out var port) || port is < 1 or > 65535)
            throw new ConfigurationException("ListenPort", "integer-1-65535-required");
        var message = ReadString(fields, "ExpectedMessage", null);
        if (string.IsNullOrEmpty(message)) throw new ConfigurationException("ExpectedMessage", "required");
        var bytes = new UTF8Encoding(false, true).GetBytes(message);
        if (bytes.Length > 256) throw new ConfigurationException("ExpectedMessage", "maximum-256-utf8-bytes");
        var bindText = ReadString(fields, "BindAddress", "0.0.0.0");
        if (!IPAddress.TryParse(bindText, out var bind))
            throw new ConfigurationException("BindAddress", "numeric-address-required");
        if (bind.IsIPv4MappedToIPv6) bind = bind.MapToIPv4();
        if (!bind.Equals(IPAddress.Any) && !bind.Equals(IPAddress.IPv6Any))
            try { bind = SourceAddress.Normalize(bind); }
            catch (ArgumentException) { throw new ConfigurationException("BindAddress", "invalid-unicast-address-or-scope"); }
        var level = ReadString(fields, "LogLevel", "Information")!;
        string[] levels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];
        if (!levels.Contains(level)) throw new ConfigurationException("LogLevel", "unknown-level");
        return new(bind, port, bytes, level);
    }

    private static string? ReadString(Dictionary<string, JsonElement> fields, string name, string? fallback)
    {
        if (!fields.TryGetValue(name, out var element)) return fallback;
        if (element.ValueKind != JsonValueKind.String) throw new ConfigurationException(name, "string-required");
        return element.GetString();
    }
}
