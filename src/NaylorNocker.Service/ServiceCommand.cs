using NaylorNocker.Core.Configuration;
namespace NaylorNocker.Service;
public static class ServiceCommand
{
    public static async Task<int> RunAsync(string[] args, Func<ValidatedKnockOptions, Task<int>> runService, TextWriter output,
        Action<ConfigurationException>? reportStartupError = null)
    {
        var validate = args.Length == 3 && args[0] == "validate";
        var offset = validate ? 1 : 0;
        if (args.Length != offset + 2 || args[offset] != "--config")
        {
            await output.WriteLineAsync("Usage: naylor-nocker [validate] --config <absolute-path>");
            return 2;
        }
        ValidatedKnockOptions options;
        try { options = KnockConfiguration.Load(args[offset + 1]); }
        catch (ConfigurationException error)
        {
            if (!validate) reportStartupError?.Invoke(error);
            await output.WriteLineAsync(error.Message);
            return 2;
        }
        if (!validate) return await runService(options);
        await output.WriteLineAsync("Configuration is valid.");
        return 0;
    }
}
