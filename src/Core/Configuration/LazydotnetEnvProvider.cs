using System.Collections;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace lazydotnet.Core.Configuration;

public class LazydotnetEnvProvider : ConfigurationProvider
{
    private const string Prefix = "LAZYDOTNET_";

    public override void Load()
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key.ToString();
            var value = entry.Value?.ToString();

            if (string.IsNullOrEmpty(key) || !key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var configKey = key[Prefix.Length..].Replace('_', ':');

            if (TryParseJsonArray(configKey, value))
            {
                continue;
            }

            Data[configKey] = value;
        }
    }

    private bool TryParseJsonArray(string configKey, string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.TrimStart().StartsWith('[') || !value.TrimEnd().EndsWith(']'))
            return false;

        try
        {
            var array = JsonSerializer.Deserialize<string[]>(value);
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    Data[$"{configKey}:{i}"] = array[i];
                }
                return true;
            }
        }
        catch
        {
            // If JSON parsing fails, fall back to returning false
        }
        return false;
    }
}

public class LazydotnetEnvConfigurationSource : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new LazydotnetEnvProvider();
    }
}

public static class LazydotnetEnvConfigurationExtensions
{
    public static IConfigurationBuilder AddLazydotnetEnvironmentVariables(this IConfigurationBuilder builder)
    {
        return builder.Add(new LazydotnetEnvConfigurationSource());
    }
}
