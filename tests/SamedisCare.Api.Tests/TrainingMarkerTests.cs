using FluentAssertions;
using SamedisCare.Api.Lookup;
using SamedisCare.Api.Routing;
using SamedisCare.Api.V4.Public;
using Xunit;

namespace SamedisCare.Api.Tests;

/// <summary>
/// The marker lookup is how a re-run tells an already-imported training from a new one, and
/// both ways of getting it wrong are expensive: missing the record imports it twice, matching
/// the wrong one drops a training that was never imported. The source system's own id is what
/// is at stake here, so the cases below are the ones that decide it.
/// </summary>
public class TrainingMarkerTests
{
    private static readonly ITenantScope Scope = TenantScope.Standard("T1", "v4");

    /// <summary>
    /// Answers the way the server does: the <c>contains</c> filter runs the value through
    /// <c>Regexp.escape</c> with <c>IGNORECASE</c>, so the marker is matched literally
    /// anywhere in the remark and its parentheses are characters, not a capture group.
    /// Filtering here rather than handing back every row is what makes these tests about the
    /// real division of labour -- the server narrows, this class decides.
    /// </summary>
    private static Trainings.Existing Find(string marker,
                                           params (string Id, string Status, string Remark)[] rows)
    {
        var matching = rows
            .Where(r => r.Remark.Contains(marker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var body = "{\"data\":[" + string.Join(",", matching.Select(r =>
                       $"{{\"id\":\"{r.Id}\",\"attributes\":{{\"status\":\"{r.Status}\",\"remark\":\"{r.Remark}\"}}}}"))
                 + $"],\"meta\":{{\"total\":{matching.Count}}}}}";

        return Trainings.FindByRemark(new FakeClient(_ => (200, body)), Scope, marker);
    }

    private static Trainings.Existing Answered(int status)
        => Trainings.FindByRemark(new FakeClient(_ => (status, "{}")), Scope, "(12)");

    [Fact]
    public void The_stamped_training_is_found()
        => Find("(12)", ("t1", "draft", "Haus B / (12)"))
               .Should().Be(new Trainings.Existing("t1", "draft", 1));

    [Fact]
    public void Nothing_matching_is_not_found()
        => Find("(12)", ("t1", "draft", "Haus B / (99)")).Found.Should().BeFalse();

    // The remark carries free text from the source system next to the stamp, so a site named
    // "Haus A (12)" contains the marker without being training 12. Taking the server's first
    // hit would report that one as already imported and drop this training for good.
    [Fact]
    public void A_marker_appearing_inside_free_text_does_not_win_over_the_stamp()
        => Find("(12)", ("wrong", "closed", "Haus A (12) / (99)"),
                         ("right", "draft",  "Haus B / (12)"))
               .Id.Should().Be("right");

    [Fact]
    public void An_incidental_hit_alone_is_still_reported_as_unclear()
    {
        var found = Find("(12)", ("only", "closed", "Haus A (12) / (99)"));

        found.Id.Should().Be("only", "skipping it would import a training the tenant has");
        found.Matches.Should().Be(1);
    }

    // Someone opening the training in the UI and adding a note after the stamp must not make
    // it invisible to the next run -- that would import it a second time.
    [Fact]
    public void A_note_written_after_the_stamp_still_matches()
        => Find("(12)", ("t1", "closed", "Haus B / (12) -- Nachtrag Geraet getauscht"))
               .Id.Should().Be("t1");

    [Fact]
    public void Two_stamped_trainings_are_reported_as_ambiguous()
    {
        var found = Find("(12)", ("t1", "closed", "Haus B / (12)"),
                                  ("t2", "draft",  "Haus C / (12)"));

        found.Ambiguous.Should().BeTrue("the tenant has a duplicate and the operator has to see it");
        found.Matches.Should().Be(2);
    }

    // A shorter id must not match a longer one. The parentheses anchor both ends and the
    // server escapes them rather than compiling them, so "(1)" is simply not a substring of
    // "(12)" -- the marker format is what makes this safe, not the code below it.
    [Fact]
    public void A_shorter_id_does_not_match_a_longer_one()
        => Find("(1)", ("t12", "closed", "Haus B / (12)"))
               .Found.Should().BeFalse();

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public void An_unanswered_lookup_never_reads_as_not_yet_imported(int status)
        => ((Action)(() => Answered(status)))
               .Should().Throw<LookupUnavailableException>(
                   "reading it as absence re-imports every training the tenant already has");

    [Fact]
    public void Only_a_404_means_the_training_is_not_there()
        => Trainings.FindByRemark(
               new FakeClient(_ => (404, "{\"meta\":{\"msg\":{\"success\":false,\"message\":\"Record not found\",\"error\":\"record_not_found_error\"}}}")), Scope, "(12)")
           .Found.Should().BeFalse();

    [Fact]
    public void The_filter_asks_for_more_than_one_candidate()
    {
        var client = new FakeClient(_ => (200, "{\"data\":[],\"meta\":{\"total\":0}}"));
        Trainings.FindByRemark(client, Scope, "(12)");

        client.Requests.Single().Should().NotContain("page[limit]=1&",
            "one record is not enough to tell an incidental hit from the stamped one");
    }
}

/// <summary>
/// The sub-resource reads decide what a resumed run attaches. An unanswered read counted as
/// "nothing attached yet" attaches every device, participant and document a second time.
/// </summary>
public class TrainingAttachmentTests
{
    private static readonly ITenantScope Scope = TenantScope.Standard("T1", "v4");

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public void An_unanswered_read_never_reads_as_nothing_attached(int status)
    {
        var client = FakeClient.AlwaysStatus(status);

        ((Action)(() => Trainings.AttachedCatalogIds(client, Scope, "t1"))).Should().Throw<LookupUnavailableException>();
        ((Action)(() => Trainings.AttachedStaffIds(client, Scope, "t1"))).Should().Throw<LookupUnavailableException>();
        ((Action)(() => Trainings.UploadCount(client, Scope, "t1"))).Should().Throw<LookupUnavailableException>();
    }

    [Fact]
    public void An_empty_collection_is_a_real_answer()
    {
        var client = FakeClient.AlwaysStatus(200, "{\"data\":[]}");

        Trainings.AttachedCatalogIds(client, Scope, "t1").Should().BeEmpty();
        Trainings.UploadCount(client, Scope, "t1").Should().Be(0);
    }
}
