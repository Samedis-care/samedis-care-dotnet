using FluentAssertions;
using SamedisCare.Helper.Config;
using Xunit;
using YamlDotNet.Core;

namespace SamedisCare.Helper.Tests;

// ConfigStore replaces LoadFromYaml, which existed in six tools. What matters is the
// underscored naming convention every config.yml relies on, and that the
// ignoreUnmatchedProperties choice is honoured — three tools had it on, three off.
public class ConfigStoreTests
{
    private class Sample
    {
        public string TenantId { get; set; } = string.Empty;
        public int LogLevel { get; set; }
        public bool ValidCertificate { get; set; }
        public Nested Http { get; set; } = new();
    }

    private class Nested
    {
        public string ProxyUsername { get; set; } = string.Empty;
    }

    [Fact]
    public void Underscored_keys_map_onto_pascal_case_properties()
    {
        const string yaml = """
        tenant_id: 63f5c0491b57cc000df2b2c7
        log_level: 2
        valid_certificate: true
        http:
          proxy_username: svc_sync
        """;

        var cfg = ConfigStore.Parse<Sample>(yaml, ignoreUnmatchedProperties: false);

        cfg.TenantId.Should().Be("63f5c0491b57cc000df2b2c7");
        cfg.LogLevel.Should().Be(2);
        cfg.ValidCertificate.Should().BeTrue();
        cfg.Http.ProxyUsername.Should().Be("svc_sync");
    }

    // The whole point of the parameter: a typo in config.yml either fails the run or is
    // swallowed, and which one it does must stay the tool's decision.
    [Fact]
    public void An_unknown_key_throws_when_unmatched_properties_are_not_ignored()
    {
        const string yaml = """
        tenant_id: t1
        loglevel: 2
        """;

        var act = () => ConfigStore.Parse<Sample>(yaml, ignoreUnmatchedProperties: false);

        act.Should().Throw<YamlException>();
    }

    [Fact]
    public void An_unknown_key_is_accepted_when_they_are_ignored()
    {
        const string yaml = """
        tenant_id: t1
        loglevel: 2
        """;

        var cfg = ConfigStore.Parse<Sample>(yaml, ignoreUnmatchedProperties: true);

        cfg.TenantId.Should().Be("t1");
        cfg.LogLevel.Should().Be(0, "the misspelled key must not have been applied");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public void An_empty_document_yields_defaults_rather_than_null(string yaml)
    {
        var cfg = ConfigStore.Parse<Sample>(yaml, ignoreUnmatchedProperties: true);

        cfg.Should().NotBeNull();
        cfg.TenantId.Should().BeEmpty();
    }

    [Fact]
    public void Invalid_yaml_throws()
    {
        var act = () => ConfigStore.Parse<Sample>("tenant_id: [unclosed", ignoreUnmatchedProperties: true);

        act.Should().Throw<YamlException>();
    }

    [Fact]
    public void A_missing_file_throws_FileNotFound_with_the_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nope_{Guid.NewGuid():N}.yml");

        var act = () => ConfigStore.Load<Sample>(path, ignoreUnmatchedProperties: true);

        act.Should().Throw<FileNotFoundException>().Which.FileName.Should().Be(path);
    }

    [Fact]
    public void Load_reads_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cfg_{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, "tenant_id: from_disk\nlog_level: 1\n");
        try
        {
            ConfigStore.Load<Sample>(path, ignoreUnmatchedProperties: false)
                       .TenantId.Should().Be("from_disk");
        }
        finally { File.Delete(path); }
    }
}
