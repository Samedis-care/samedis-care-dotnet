using System.Collections;
using System.Reflection;
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
    /// <param name="fillNullSections">
    /// Whether a section that is present but empty is turned back into defaults. On by
    /// default; see <see cref="FillNullSections{T}"/> for what that means and why the
    /// default is not the other way round.
    /// </param>
    public static T Load<T>(string path, bool ignoreUnmatchedProperties, bool fillNullSections = true)
        where T : new()
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {path}", path);

        return Parse<T>(File.ReadAllText(path), ignoreUnmatchedProperties, fillNullSections);
    }

    /// <summary>
    /// Same as <see cref="Load{T}"/> but from a string, so a caller can validate config
    /// without writing a file. Used by the tests.
    /// </summary>
    public static T Parse<T>(string yaml, bool ignoreUnmatchedProperties, bool fillNullSections = true)
        where T : new()
    {
        var builder = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance);

        if (ignoreUnmatchedProperties)
            builder = builder.IgnoreUnmatchedProperties();

        // An empty or whitespace-only file deserializes to null; the tools all treated
        // that as "defaults", so keep that.
        var config = builder.Build().Deserialize<T>(yaml) ?? new T();

        // Guarded on IsValueType rather than constraining T to class: tightening the
        // constraint would be a breaking change for a consumer outside this repository, and
        // walking a boxed struct would mutate the box and throw the result away -- worse than
        // doing nothing. Every config type in the family is a class.
        if (fillNullSections && !typeof(T).IsValueType && config is { } node)
            Walk(node, new HashSet<object>(ReferenceEqualityComparer.Instance));

        return config;
    }

    /// <summary>
    /// Replaces every null section in a loaded config with a default instance, so a section
    /// that is present but empty means the same as one that is absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// YamlDotNet does not treat <c>logging:</c> with nothing under it like a missing key: it
    /// <em>sets</em> the property, and the value it sets is null, overwriting the initialiser
    /// on the config class. A half-filled config.yml is a normal state while setting a tool up
    /// or after commenting a block out, and every tool dereferenced its sections without a
    /// null check, so the result was a bare <see cref="NullReferenceException"/> naming
    /// nothing. See samedis-care-issues#2884 and #2885.
    /// </para>
    /// <para>
    /// This is not a hidden default, which is why it is on by default and
    /// <c>ignoreUnmatchedProperties</c> is not: a missing section already means defaults, so
    /// there is no third behaviour to choose between — only the question of whether the two
    /// spellings agree. Pass <c>fillNullSections: false</c> if a caller genuinely needs to see
    /// which sections the file omitted.
    /// </para>
    /// <para>
    /// Exposed publicly because two tools (spl-sync, fluke-sync) keep their own loader for
    /// DPAPI-encrypted secrets and need to apply this to an object they deserialized
    /// themselves — before they touch it, since their decrypt step dereferences sections.
    /// </para>
    /// <para>
    /// What it fills: any readable and writable reference-typed property that is null and
    /// whose type has a public parameterless constructor, plus arrays (created empty). It
    /// then walks into the value, into the elements of a sequence and into the values of a
    /// dictionary, so a null nested under a section, inside a list element or under a
    /// dictionary value is filled too. What it deliberately leaves alone: strings, because a
    /// null string means "not configured" and an empty one does not; value types, which are
    /// never null in the first place; properties without a setter; types with no parameterless
    /// constructor, which it cannot construct; a dictionary <em>key</em>; and a list element
    /// written as a bare <c>-</c>, which is an empty entry rather than an empty section.
    /// Filling stops at <see cref="MaxDepth"/>.
    /// </para>
    /// </remarks>
    /// <returns>The same instance, for chaining.</returns>
    public static T FillNullSections<T>(T config) where T : class
    {
        Walk(config, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return config;
    }

    /// <summary>
    /// How deep the walk goes before it stops filling. Guards a config <em>type</em> whose
    /// shape is unbounded -- <c>class A { B B; }</c> with <c>class B { A A; }</c> -- which the
    /// <c>seen</c> set below cannot catch, because every <see cref="CreateDefault"/> hands back
    /// a fresh instance it has never seen. Before this cap that shape stack-overflowed on an
    /// entirely empty config file, and a StackOverflowException cannot be caught: the process
    /// died without a message, which is strictly worse than the NullReferenceException this
    /// class exists to remove.
    /// <para>
    /// A cap rather than a per-path type set on purpose: the same type legitimately appears
    /// twice along a path in real nested data (a tree loaded from YAML), and refusing to fill
    /// there would trade a crash for a quiet null. 64 is far past any hand-written config.
    /// </para>
    /// </summary>
    private const int MaxDepth = 64;

    private static void Walk(object node, HashSet<object> seen, int depth = 0)
    {
        if (depth >= MaxDepth) return;

        // A config type may point back at itself; without this the walk would not terminate.
        // This covers a cyclic instance graph; MaxDepth covers a cyclic type.
        if (!seen.Add(node)) return;

        // A dictionary has to come first and be handled by its values: it enumerates as
        // KeyValuePair<,>, a value type, so ShouldWalk would reject every entry and the values
        // would never be visited at all.
        if (node is IDictionary dictionary)
        {
            foreach (var value in dictionary.Values)
                if (ShouldWalk(value))
                    Walk(value!, seen, depth + 1);
            return;
        }

        if (node is IEnumerable sequence)
        {
            // An element written as a bare "-" stays null: there is no section to default
            // there, and quietly turning an empty list entry into an object would hide it.
            foreach (var item in sequence)
                if (ShouldWalk(item))
                    Walk(item!, seen, depth + 1);
            return;
        }

        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (property.GetIndexParameters().Length > 0) continue;
            if (property.PropertyType.IsValueType || property.PropertyType == typeof(string)) continue;

            var value = property.GetValue(node);

            if (value is null)
            {
                value = CreateDefault(property.PropertyType);
                if (value is null) continue;
                property.SetValue(node, value);
            }

            if (ShouldWalk(value))
                Walk(value, seen, depth + 1);
        }
    }

    /// <summary>
    /// Framework types are not walked into -- only their elements, handled above. Walking a
    /// BCL object's properties would be pointless at best and, for anything with a getter
    /// that does real work, actively harmful.
    /// </summary>
    private static bool ShouldWalk(object? value)
        => value is not null
           && value is not string
           && !value.GetType().IsValueType
           && (value is IEnumerable || value.GetType().Namespace?.StartsWith("System", StringComparison.Ordinal) != true);

    private static object? CreateDefault(Type type)
    {
        if (type.IsArray)
            return type.GetArrayRank() == 1
                ? Array.CreateInstance(type.GetElementType()!, 0)
                : null;

        if (type.IsAbstract || type.IsInterface) return null;
        if (type.GetConstructor(Type.EmptyTypes) is null) return null;

        return Activator.CreateInstance(type);
    }
}
