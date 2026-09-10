using FluentAssertions;
using SamedisCare.Helper.Config;
using Xunit;

namespace SamedisCare.Helper.Tests;

/// <summary>
/// YamlDotNet does not treat a section header with nothing under it like an absent key: it
/// sets the property, and the value it sets is null, overwriting the initialiser on the
/// config class. So an <em>empty</em> section behaved differently from a <em>missing</em>
/// one, and every tool dereferenced its sections unguarded — a bare NullReferenceException
/// with nothing naming the section at fault. See samedis-care-issues#2884 and #2885.
/// <para>
/// The shapes below are taken from the real tool configs, not invented: nested transports
/// (log-monitor's mail.smtp), a dictionary section (log-monitor's programs), a string array
/// (staff-sync's team), and a list of objects whose elements carry lists of their own
/// (spl-sync's tenants[].actimed_cust_ids).
/// </para>
/// </summary>
public class ConfigStoreFillNullSectionsTests
{
    private class Sample
    {
        public AuthSection Auth { get; set; } = new();
        public MailSection Mail { get; set; } = new();
        public Dictionary<string, string> Programs { get; set; } = new();
        public List<string> Levels { get; set; } = new();
        public string[] Team { get; set; } = Array.Empty<string>();
        public List<TenantSection> Tenants { get; set; } = new();

        // A null string is a real value -- "not configured" -- and must stay null.
        public string? ClientSecret { get; set; }

        // No setter, so nothing can be assigned to it.
        public string Computed => "x";
    }

    private class AuthSection
    {
        public string? Uri { get; set; }
    }

    private class MailSection
    {
        public bool Enabled { get; set; }
        public SmtpSection Smtp { get; set; } = new();
    }

    private class SmtpSection
    {
        public int Port { get; set; }
        public bool UseStartTls { get; set; }
    }

    private class TenantSection
    {
        public string? Name { get; set; }
        public List<int> ActimedCustIds { get; set; } = new();
    }

    [Fact]
    public void An_empty_section_yields_defaults_like_a_missing_one()
    {
        var cfg = ConfigStore.Parse<Sample>("auth:\nmail:\nprograms:\nlevels:\nteam:\n",
                                            ignoreUnmatchedProperties: true);

        cfg.Auth.Should().NotBeNull();
        cfg.Mail.Should().NotBeNull();
        cfg.Programs.Should().NotBeNull().And.BeEmpty();
        cfg.Levels.Should().NotBeNull().And.BeEmpty();
        cfg.Team.Should().NotBeNull().And.BeEmpty();
    }

    // The case that actually crashed log-monitor: the transport one level below the section.
    [Fact]
    public void A_nested_section_is_filled_too()
    {
        var cfg = ConfigStore.Parse<Sample>("mail:\n  enabled: true\n  smtp:\n",
                                            ignoreUnmatchedProperties: true);

        cfg.Mail.Smtp.Should().NotBeNull();
        cfg.Mail.Enabled.Should().BeTrue("filling must not disturb what the file did set");
    }

    // spl-sync's tenants[].actimed_cust_ids: a null inside an element of a list section.
    [Fact]
    public void A_null_inside_a_list_element_is_filled()
    {
        var cfg = ConfigStore.Parse<Sample>(
            "tenants:\n  - name: one\n    actimed_cust_ids:\n  - name: two\n",
            ignoreUnmatchedProperties: true);

        cfg.Tenants.Should().HaveCount(2);
        cfg.Tenants[0].ActimedCustIds.Should().NotBeNull().And.BeEmpty();
        cfg.Tenants[1].ActimedCustIds.Should().NotBeNull();
    }

    // Filling a null string would turn "not configured" into "configured as empty", which is
    // a different thing everywhere these configs are read.
    [Fact]
    public void A_null_string_is_left_alone()
    {
        var cfg = ConfigStore.Parse<Sample>("auth:\n  uri:\n", ignoreUnmatchedProperties: true);

        cfg.ClientSecret.Should().BeNull();
        cfg.Auth.Uri.Should().BeNull();
    }

    [Fact]
    public void Values_the_file_sets_are_untouched()
    {
        var cfg = ConfigStore.Parse<Sample>("""
            auth:
              uri: "https://ident.services"
            mail:
              smtp:
                port: 587
                use_start_tls: true
            programs:
              external-sync: /var/log/external-sync
            levels: ["ERROR"]
            """, ignoreUnmatchedProperties: true);

        cfg.Auth.Uri.Should().Be("https://ident.services");
        cfg.Mail.Smtp.Port.Should().Be(587);
        cfg.Mail.Smtp.UseStartTls.Should().BeTrue();
        cfg.Programs.Should().ContainKey("external-sync");
        cfg.Levels.Should().ContainSingle().Which.Should().Be("ERROR");
    }

    [Fact]
    public void An_entirely_empty_file_still_yields_a_usable_config()
    {
        var cfg = ConfigStore.Parse<Sample>("", ignoreUnmatchedProperties: true);

        cfg.Auth.Should().NotBeNull();
        cfg.Mail.Smtp.Should().NotBeNull();
    }

    // The tools that keep their own loader (spl-sync, fluke-sync) need to call this on an
    // object they deserialized themselves, so it has to work standalone.
    [Fact]
    public void It_can_be_applied_to_an_object_from_another_loader()
    {
        var cfg = new Sample { Auth = null!, Mail = null!, Team = null! };

        ConfigStore.FillNullSections(cfg);

        cfg.Auth.Should().NotBeNull();
        cfg.Mail.Smtp.Should().NotBeNull();
        cfg.Team.Should().NotBeNull();
    }

    [Fact]
    public void Opting_out_leaves_the_nulls_in_place()
    {
        var cfg = ConfigStore.Parse<Sample>("auth:\n", ignoreUnmatchedProperties: true,
                                            fillNullSections: false);

        cfg.Auth.Should().BeNull("the caller asked to see the file as it is");
    }

    private class SelfReferencing
    {
        public SelfReferencing? Next { get; set; }
        public AuthSection Auth { get; set; } = new();
    }

    // Walking an object graph has to terminate even when a config type points at itself.
    [Fact]
    public void A_self_referencing_graph_does_not_hang()
    {
        var node = new SelfReferencing { Auth = null! };
        node.Next = node;

        var act = () => ConfigStore.FillNullSections(node);

        act.Should().NotThrow();
        node.Auth.Should().NotBeNull();
    }
}

/// <summary>
/// Termination and reach of the walk. Both cases here were review findings on the release PR:
/// the reference-identity set only stops an already-cyclic <em>instance</em> graph, and
/// "walks into the elements of anything enumerable" was not true for a dictionary.
/// </summary>
public class ConfigStoreWalkTerminationTests
{
    private class A { public B? B { get; set; } }
    private class B { public A? A { get; set; } }
    private class MutualCfg { public A? Root { get; set; } }

    // Every CreateDefault hands back a fresh instance the "seen" set has never seen, so
    // A -> B -> A built itself forever. Measured before the fix: "Stack overflow." inside
    // Walk, on an *empty* config file -- and a StackOverflowException cannot be caught, so
    // the process died without a message. That is strictly worse than the
    // NullReferenceException this whole change exists to remove.
    [Fact]
    public void A_mutually_referencing_config_type_terminates()
    {
        var act = () => ConfigStore.Parse<MutualCfg>("", ignoreUnmatchedProperties: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void A_mutually_referencing_config_type_still_gets_its_first_level()
    {
        var cfg = ConfigStore.Parse<MutualCfg>("", ignoreUnmatchedProperties: true);

        cfg.Root.Should().NotBeNull("the shape being unbounded is no reason to hand back a null section");
    }

    private class Inner { public int Port { get; set; } }
    private class Section { public Inner Inner { get; set; } = new(); }
    private class DictCfg { public Dictionary<string, Section> Targets { get; set; } = new(); }
    private class ListCfg { public List<Section> Items { get; set; } = new(); }

    // A dictionary enumerates as KeyValuePair<,>, which is a value type, so the values were
    // never reached. Measured: Targets["a"].Inner came back null while the same shape in a
    // List came back filled.
    [Fact]
    public void A_null_under_a_dictionary_value_is_filled()
    {
        var cfg = ConfigStore.Parse<DictCfg>("targets:\n  a:\n    inner:\n", ignoreUnmatchedProperties: true);

        cfg.Targets["a"].Inner.Should().NotBeNull();
    }

    [Fact]
    public void A_null_under_a_list_element_is_filled()
    {
        var cfg = ConfigStore.Parse<ListCfg>("items:\n  - inner:\n", ignoreUnmatchedProperties: true);

        cfg.Items[0].Inner.Should().NotBeNull();
    }

    // Documented rather than fixed: an element written as a bare "-" stays null. There is no
    // section to default there -- the author wrote an empty list entry, which is a different
    // thing from an empty section, and silently turning it into an object would hide the typo.
    [Fact]
    public void A_null_list_element_is_left_as_it_was_written()
    {
        var cfg = ConfigStore.Parse<ListCfg>("items:\n  -\n", ignoreUnmatchedProperties: true);

        cfg.Items.Should().ContainSingle().Which.Should().BeNull();
    }
}
