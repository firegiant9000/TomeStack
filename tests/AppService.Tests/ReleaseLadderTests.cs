using TomeStack.AppService.Packages;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Down-conversion export, lane A: the release ladder (D83) and the "opens in" lookup.</summary>
public class ReleaseLadderTests
{
    [Fact]
    public void Rows_are_ascending_and_match_the_spec_table()
    {
        var rows = ReleaseLadder.Rows;

        Assert.Equal([new Version(0, 4, 0), new Version(0, 5, 0), new Version(0, 6, 0), new Version(0, 7, 0), new Version(0, 8, 0), new Version(0, 9, 0)], rows.Select(r => r.Release));
        Assert.Equal([(7, 9, 1, 9, 9), (8, 9, 2, 9, 10), (9, 10, 2, 9, 11), (10, 11, 2, 9, 11), (11, 12, 2, 9, 11), (11, 13, 2, 9, 13)],
            rows.Select(r => (r.CharacterSchema, r.ContentSchema, r.GapNoteSchema, r.FormatVersion, r.DatabaseSchema)));
        Assert.Equal([false, false, false, false, true, false], rows.Select(r => r.OfferedAsTarget));
    }

    [Fact]
    public void Each_bundled_set_keeps_every_id_of_the_one_before()
    {
        // Published revisions are insert-only, so a newer minor never drops a bundled revision id.
        for (var i = 1; i < ReleaseLadder.Rows.Count; i++)
            Assert.True(ReleaseLadder.Rows[i - 1].BundledRevisionIds.IsSubsetOf(ReleaseLadder.Rows[i].BundledRevisionIds),
                $"{ReleaseLadder.Rows[i].Release} drops a bundled revision {ReleaseLadder.Rows[i - 1].Release} had");
    }

    [Fact]
    public void CurrentRow_is_the_row_of_the_build_version()
    {
        Assert.Equal(new Version(AppVersionStamp.CurrentVersion).Minor, ReleaseLadder.CurrentRow.Release.Minor);
    }

    [Fact]
    public void PreviousMinor_is_the_row_before_current()
    {
        Assert.Equal(new Version(0, 8, 0), ReleaseLadder.PreviousMinor?.Release);
    }

    [Fact]
    public void Constants_never_fall_below_the_current_row()
    {
        var row = ReleaseLadder.CurrentRow;

        Assert.True(row.CharacterSchema <= Character.CurrentSchemaVersion);
        Assert.True(row.ContentSchema <= ContentRevision.CurrentSchemaVersion);
        Assert.True(row.GapNoteSchema <= GapNote.CurrentSchemaVersion);
        Assert.Equal(1, Campaign.CurrentSchemaVersion); // no ladder column: campaign data has been v1 in every release
        Assert.True(row.FormatVersion <= PackageManifest.CurrentFormatVersion);
        Assert.True(row.DatabaseSchema <= SqliteStore.LatestSchemaVersion);
    }

    [Fact]
    public void Bundled_sets_are_subsets_of_the_catalog()
    {
        var catalog = TomeStackApp.BundledRevisionIds;

        Assert.All(ReleaseLadder.Rows, row =>
        {
            Assert.NotEmpty(row.BundledRevisionIds);
            Assert.True(row.BundledRevisionIds.IsSubsetOf(catalog), $"{row.Release} has a bundled revision the current packs lack");
        });
    }

    private static readonly Guid FixtureSource = Guid.Parse("7c000000-0000-4000-8000-000000000001");

    private static Character Hero(string name = "Compat Hero") =>
        new() { Id = Guid.NewGuid(), Name = name, RulesFamily = RulesFamilies.Srd521, BaseAbilities = new(10, 10, 10, 10, 10, 10) };

    private static Character V9(Character c) => c with { Xp = 100 };

    private static Character V10(Character c) => c with { Play = (c.Play ?? new PlayState()) with { Encounter = new EncounterState(0) } };

    private static Character V11(Character c) => c with
    {
        Equipment = [new EquipmentEntry(new ContentReference(Guid.NewGuid(), Guid.NewGuid()), Mods: [new ContentReference(Guid.NewGuid(), Guid.NewGuid())])],
    };

    private static ContentRevision Revision(string name, int schemaVersion, Guid? revisionId = null) => new()
    {
        ContentId = Guid.NewGuid(),
        RevisionId = revisionId ?? Guid.NewGuid(),
        Kind = ContentKind.Feature,
        Name = name,
        RulesFamilies = [RulesFamilies.Srd521],
        Provenance = new(FixtureSource, new PageRef(1)),
        Status = RevisionStatus.Published,
        SchemaVersion = schemaVersion,
    };

    private static LadderRow Row(string release) => ReleaseLadder.Rows.Single(r => r.Release.ToString(3) == release);

    [Fact]
    public void OpensIn_is_0_5_0_for_a_v8_character_and_v9_content()
    {
        Assert.Equal(Row("0.5.0"), ReleaseLadder.OpensIn([Revision("Compat Tide Stance", 9)], [Hero()], 0, []));
    }

    [Fact]
    public void OpensIn_is_0_6_0_for_v9_character()
    {
        Assert.Equal(Row("0.6.0"), ReleaseLadder.OpensIn([], [V9(Hero())], 0, []));
    }

    [Fact]
    public void OpensIn_is_0_7_0_for_v11_content_or_v10_character()
    {
        Assert.Equal(Row("0.7.0"), ReleaseLadder.OpensIn([Revision("Compat Tide Stance", 11)], [Hero()], 0, []));
        Assert.Equal(Row("0.7.0"), ReleaseLadder.OpensIn([], [V10(Hero())], 0, []));
    }

    [Fact]
    public void OpensIn_is_0_8_0_for_v12_content_or_v11_character()
    {
        Assert.Equal(Row("0.8.0"), ReleaseLadder.OpensIn([Revision("Compat Tide Stance", 12)], [Hero()], 0, []));
        Assert.Equal(Row("0.8.0"), ReleaseLadder.OpensIn([], [V11(Hero())], 0, []));
    }

    [Fact]
    public void OpensIn_is_0_9_0_for_v13_content()
    {
        Assert.Equal(Row("0.9.0"), ReleaseLadder.OpensIn([Revision("Compat Tide Stance", 13)], [V11(Hero())], 0, []));
    }

    [Fact]
    public void OpensIn_is_null_above_every_row()
    {
        Assert.Null(ReleaseLadder.OpensIn([Revision("Compat Tide Stance", ContentRevision.CurrentSchemaVersion + 1)], [Hero()], 0, []));
        Assert.Null(ReleaseLadder.OpensIn([], [Hero() with { SchemaVersion = Character.CurrentSchemaVersion + 1 }], 0, []));
        Assert.Null(ReleaseLadder.OpensIn([], [], GapNote.CurrentSchemaVersion + 1, []));
    }

    [Fact]
    public void OpensIn_skips_rows_lacking_a_bundled_revision()
    {
        Assert.Null(ReleaseLadder.OpensIn([], [Hero()], 0, [Guid.Parse("7c00e000-0000-4000-8000-0000000000ff")]));
        var inEverySet = ReleaseLadder.Rows[0].BundledRevisionIds.First(id => ReleaseLadder.Rows.All(r => r.BundledRevisionIds.Contains(id)));
        Assert.Equal(Row("0.5.0"), ReleaseLadder.OpensIn([], [Hero()], 0, [inEverySet])); // a v8 character; the id changes nothing
        Assert.Equal(Row("0.4.0"), ReleaseLadder.OpensIn([], [], 1, []));
    }

    [Fact]
    public void Label_names_the_release_or_a_newer_build()
    {
        Assert.Equal("0.6.0", ReleaseLadder.Label(Row("0.6.0")));
        Assert.Equal("a newer TomeStack", ReleaseLadder.Label(null));
    }

    [Fact]
    public void Evaluate_warns_only_above_previous_minor()
    {
        var quiet = NewerBuild.Evaluate([Revision("Compat Tide Stance", 12)], [V11(Hero())], 0, [], _ => "");
        Assert.Equal("0.8.0", quiet.OpensIn);
        Assert.Empty(quiet.Warnings);
        Assert.Empty(quiet.Causes);

        var stance = Revision("Compat Tide Stance", 13);
        var one = NewerBuild.Evaluate([stance], [V11(Hero())], 0, [], _ => "");
        Assert.Equal("0.9.0", one.OpensIn);
        var warning = Assert.Single(one.Warnings);
        Assert.Equal(NewerBuild.Code, warning.Code);
        Assert.Equal($"Opening this needs TomeStack 0.9.0 or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): 'Compat Tide Stance' is content at file version 13. Older versions refuse the file.", warning.Message);
        var cause = Assert.Single(one.Causes);
        Assert.Equal(new OpensInCause($"content/{stance.RevisionId:D}.json", "content", stance.RevisionId, "Compat Tide Stance", 13, null, "'Compat Tide Stance' is content at file version 13"), cause);
    }

    [Fact]
    public void Evaluate_joins_causes_in_the_order_characters_content_gap_notes()
    {
        // 0.9.0 reads the same character and gap note versions as 0.8.0, so a character or gap note cause means no released row opens the file.
        var hero = Hero() with { SchemaVersion = 12 };
        var stance = Revision("Compat Tide Stance", 13);

        var result = NewerBuild.Evaluate([stance], [hero], ReleaseLadder.PreviousMinor!.GapNoteSchema + 1, [], _ => "");

        Assert.Null(result.OpensIn);
        Assert.Equal(["character", "content", "gapNote"], result.Causes.Select(c => c.Kind));
        Assert.Equal($"content/{stance.RevisionId:D}.json", result.Causes[1].Entry);
        Assert.Equal(new OpensInCause("gaps/", "gapNote", Guid.Empty, "gap notes", 3, null, "the gap notes are at file version 3"), result.Causes[2]);
    }

    [Fact]
    public void Evaluate_joins_a_content_cause_and_a_bundled_cause_when_the_newest_row_opens_the_file()
    {
        var newInThisMinor = Row("0.9.0").BundledRevisionIds.Except(Row("0.8.0").BundledRevisionIds).First();
        var species = Revision("Example Species", 3, newInThisMinor);
        var hero = Hero() with { Pins = [species.Reference] };
        var stance = Revision("Compat Tide Stance", 13);

        var result = NewerBuild.Evaluate([stance, species], [hero], 0, [newInThisMinor], _ => "SRD 5.2.1");

        Assert.Equal("0.9.0", result.OpensIn);
        Assert.Equal($"Opening this needs TomeStack 0.9.0 or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): 'Compat Tide Stance' is content at file version 13, and 'Compat Hero' uses 'Example Species' from the bundled SRD 5.2.1 content, which TomeStack 0.8.0 does not have. Older versions refuse the file.",
            Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_names_a_bundled_revision_the_previous_minor_lacks()
    {
        var hero = Hero();
        var species = Revision("Example Species", 3);
        var heroWithSpecies = hero with { Pins = [species.Reference] };

        var result = NewerBuild.Evaluate([species], [heroWithSpecies], 0, [species.RevisionId], _ => "SRD 5.2.1");

        var cause = Assert.Single(result.Causes);
        Assert.Equal(new OpensInCause($"content/{species.RevisionId:D}.json", "content", species.RevisionId, "Example Species", null, "SRD 5.2.1",
            "'Compat Hero' uses 'Example Species' from the bundled SRD 5.2.1 content, which TomeStack 0.8.0 does not have"), cause);
        Assert.Null(result.OpensIn);
        Assert.Equal("Opening this needs a TomeStack version newer than 0.9.0: it holds bundled SRD content that released versions do not have.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_caps_the_warning_at_three_distinct_messages()
    {
        var revisions = new[] { "Ann", "Bo", "Cy", "Di", "Di" }.Select(n => Revision(n, 13)).ToList();

        var result = NewerBuild.Evaluate(revisions, [], 0, [], _ => "");

        Assert.Equal(5, result.Causes.Count);
        Assert.Equal($"Opening this needs TomeStack 0.9.0 or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): 'Ann' is content at file version 13, 'Bo' is content at file version 13, 'Cy' is content at file version 13, and 1 more. Older versions refuse the file.",
            Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_keeps_the_joined_form_for_exactly_three_messages()
    {
        var revisions = new[] { "Ann", "Bo", "Cy" }.Select(n => Revision(n, 13)).ToList();

        var result = NewerBuild.Evaluate(revisions, [], 0, [], _ => "");

        Assert.Equal($"Opening this needs TomeStack 0.9.0 or newer (this is TomeStack {AppVersionStamp.CurrentVersion}): 'Ann' is content at file version 13, and 'Bo' is content at file version 13, and 'Cy' is content at file version 13. Older versions refuse the file.",
            Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_does_not_borrow_a_character_for_an_unpinned_bundled_revision()
    {
        var species = Revision("Example Species", 3);

        var result = NewerBuild.Evaluate([species], [Hero("One"), Hero("Two")], 0, [species.RevisionId], _ => "SRD 5.2.1");

        Assert.Equal("'Example Species' is from the bundled SRD 5.2.1 content, which TomeStack 0.8.0 does not have", Assert.Single(result.Causes).Message);
    }

    [Fact]
    public void Evaluate_points_an_uncarried_bundled_cause_at_the_pinning_character()
    {
        var species = Revision("Example Species", 3);
        var hero = Hero() with { Pins = [species.Reference] };

        var result = NewerBuild.Evaluate([species], [Hero("Other"), hero], 0, [species.RevisionId], _ => "SRD 5.2.1", bundledCarried: false);

        Assert.Equal($"characters/{hero.Id:D}.json", Assert.Single(result.Causes).Entry);
    }

    [Fact]
    public void Row_finds_any_row_offered_or_not()
    {
        Assert.False(ReleaseLadder.Row("0.4.0")!.OfferedAsTarget);
        Assert.NotNull(ReleaseLadder.Row("0.8.0"));
        Assert.False(ReleaseLadder.Row("0.9.0")!.OfferedAsTarget);
        Assert.Null(ReleaseLadder.Row("0.8.1"));
        Assert.Null(ReleaseLadder.Row("9.9.9"));
    }

    [Fact]
    public void At_most_one_ladder_row_is_offered()
    {
        Assert.True(ReleaseLadder.Rows.Count(r => r.OfferedAsTarget) <= 1);
    }

    [Fact]
    public void OfferedFor_never_lists_the_builds_own_minor()
    {
        Assert.Empty(ReleaseLadder.OfferedFor("0.8.1"));
        Assert.Equal(["0.8.0"], ReleaseLadder.OfferedFor("0.9.0").Select(r => r.Release.ToString(3)));
        Assert.Equal(["0.8.0"], ReleaseLadder.OfferedFor("1.0.0").Select(r => r.Release.ToString(3)));
        Assert.Empty(ReleaseLadder.OfferedFor("0.7.1"));
    }

    [Fact]
    public void CurrentRowOrLast_falls_back_to_the_last_row_for_a_version_with_no_row()
    {
        Assert.Equal(ReleaseLadder.Rows[^1], ReleaseLadder.CurrentRowOrLast("0.10.0"));
        Assert.Equal(ReleaseLadder.Rows[^2], ReleaseLadder.PreviousMinorFor("0.10.0"));
        Assert.Equal(Row("0.6.0"), ReleaseLadder.CurrentRowOrLast("0.6.2"));
    }

    [Theory]
    [InlineData("x.y")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0.8.x")]
    public void A_malformed_build_stamp_fails_loudly_and_names_the_stamp(string stamp)
    {
        var offered = Assert.Throws<InvalidOperationException>(() => ReleaseLadder.OfferedFor(stamp));
        Assert.Contains($"\"{stamp}\"", offered.Message, StringComparison.Ordinal);
        var current = Assert.Throws<InvalidOperationException>(() => ReleaseLadder.CurrentRowOrLast(stamp));
        Assert.Contains($"\"{stamp}\"", current.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ReleaseLadder.PreviousMinorFor(stamp));
    }

    [Fact]
    public void A_two_part_build_stamp_still_parses()
    {
        Assert.Equal(["0.8.0"], ReleaseLadder.OfferedFor("0.9").Select(r => r.Release.ToString(3)));
        Assert.Equal(Row("0.6.0"), ReleaseLadder.CurrentRowOrLast("0.6"));
    }

    [Fact]
    public void Evaluate_gives_a_bundled_revision_that_is_also_new_content_one_cause()
    {
        var revision = Revision("Example Species", 13);

        var result = NewerBuild.Evaluate([revision], [Hero()], 0, [revision.RevisionId], _ => "SRD 5.2.1");

        var cause = Assert.Single(result.Causes);
        Assert.Equal(13, cause.SchemaVersion);
        Assert.Equal($"content/{revision.RevisionId:D}.json", cause.Entry);
    }

    [Fact]
    public void Evaluate_uses_the_gap_note_sentence_when_only_the_gap_notes_are_too_new()
    {
        var result = NewerBuild.Evaluate([], [], 3, [], _ => "");

        Assert.Null(result.OpensIn);
        Assert.Equal("Opening this needs a TomeStack version that reads gap note file version 3 or later: it holds gap notes that older versions refuse.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Evaluate_uses_the_unreleased_sentence_when_opensIn_is_null()
    {
        var content = NewerBuild.Evaluate([Revision("Compat Tide Stance", 14)], [], 0, [], _ => "");
        Assert.Null(content.OpensIn);
        Assert.Equal("Opening this needs a TomeStack version that reads content file version 14 or later: it holds content that older versions refuse.", Assert.Single(content.Warnings).Message);

        var character = NewerBuild.Evaluate([], [Hero() with { SchemaVersion = 12 }], 0, [], _ => "");
        Assert.Equal("Opening this needs a TomeStack version that reads character file version 12 or later: it holds a character that older versions refuse.", Assert.Single(character.Warnings).Message);
    }

    [Theory]
    [InlineData("0.6.0", false)]
    [InlineData("0.5.0", false)]
    [InlineData("0.4.0", false)]
    [InlineData("0.7.0", false)]
    [InlineData("0.7.1", false)]
    [InlineData("0.8.0", true)]
    [InlineData("0.9.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("0.6", false)]
    public void Target_accepts_offered_rows_only(string? release, bool offered)
    {
        var target = ReleaseLadder.Target(release);

        Assert.Equal(offered, target is not null);
        if (offered) Assert.Equal(release, target!.Release.ToString(3));
    }
}
