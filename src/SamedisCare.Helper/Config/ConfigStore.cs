using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SamedisCare.Helper.Config;

/// <summary>
/// Loads a tool's <c>config.yml</c>. Replaces the <c>LoadFromYaml</c> copy that existed in
/// six of the sync tools — the most duplicated non-API concern in the family.
/// <para>
/// Generic over the config type on purpose: every tool has its own shape, which is why
/// the previous per-tool copies were typed against their own <c>AppConfig</c> and could
/// not be shared.
/// </para>
/// </summary>
public static class ConfigStore
{
    /// <summary>
    /// Reads YAML from <paramref name="path"/> into <typeparamref name="T"/>, using the
    /// underscored naming convention every tool's config.yml is written in
    /// (<c>tenant_id</c> maps to <c>TenantId</c>).
    /// </summary>
    /// <param name="path">Path to the YAML file.</param>
    /// <param name="ignoreUnmatchedProperties">
    /// Whether an unknown key in the file is tolerated.
    /// <para>
    /// This has no safe default, which is why it must be passed: three of the six tools
    /// ignored unmatched properties and three did not. Turning it on where it was off
    /// makes a typo in config.yml pass silently instead of failing the run — so a caller
    /// should pass what its own code did before, and prefer <c>false</c> for new tools.
    /// </para>
    /// </param>
    /// <returns>The deserialized config, or a new instance when the file is empty.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="YamlDotNet.Core.YamlException">The file is not valid YAML, or has an unknown key while <paramref name="ignoreUnmatchedProperties"/> is false.</exception>
    public static T Load<T>(string path, bool ignoreUnmatchedProperties) where T : new()
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {path}", path);

        return Parse<T>(File.ReadAllText(path), ignoreUnmatchedProperties);
    }

    /// <summary>
    /// Same as <see cref="Load{T}"/> but from a string, so a caller can validate config
    /// without writing a file. Used by the tests.
    /// </summary>
    public static T Parse<T>(string yaml, bool ignoreUnmatchedProperties) where T : new()
    {
        var builder = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance);

        if (ignoreUnmatchedProperties)
            builder = builder.IgnoreUnmatchedProperties();

        // An empty or whitespace-only file deserializes to null; the tools all treated
        // that as "defaults", so keep that.
        return builder.Build().Deserialize<T>(yaml) ?? new T();
    }
}
