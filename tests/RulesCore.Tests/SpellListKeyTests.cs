namespace TomeStack.RulesCore.Tests;

/// <summary>Tester feedback item 10: one normaliser and one validator for spell list keys.</summary>
public class SpellListKeyTests
{
    [Theory]
    [InlineData("Wizard", "wizard")]
    [InlineData("  Fixture   Seer  ", "fixture-seer")]
    [InlineData("FIXTURE\tSEER", "fixture-seer")]
    [InlineData("bard", "bard")]
    public void A_key_is_trimmed_lower_cased_and_inner_whitespace_becomes_one_hyphen(string text, string expected) =>
        Assert.Equal(expected, SpellListKey.Normalise(text));

    [Theory]
    [InlineData("wizard", true)]
    [InlineData("fixture-seer-2", true)]
    [InlineData("", false)]
    [InlineData("Wizard", false)]
    [InlineData("seer's", false)]
    [InlineData("a b", false)]
    [InlineData("é", false)]
    public void A_key_is_one_to_sixty_letters_digits_or_hyphens(string key, bool valid) =>
        Assert.Equal(valid, SpellListKey.IsValid(key));

    [Fact]
    public void The_length_limit_is_sixty()
    {
        Assert.True(SpellListKey.IsValid(new string('a', 60)));
        Assert.False(SpellListKey.IsValid(new string('a', 61)));
    }

    [Fact]
    public void A_spell_on_no_list_warns_in_words_that_say_what_to_do()
    {
        var pack = Fixtures.Pack();
        var revision = new ContentRevision
        {
            ContentId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), Kind = ContentKind.Spell, Name = "Testy Spell",
            RulesFamilies = [RulesFamilies.Srd51], Provenance = new(Fixtures.SourceShared), Status = RevisionStatus.Draft,
            Effects = [new SpellEffect { Id = "spell", Level = 1 }],
        };

        var report = ContentValidator.Validate(revision, new InMemoryContentCatalog(pack.Sources, pack.Revisions));

        var warning = Assert.Single(report.Warnings, w => w.Code == "validate.spell-lists");
        Assert.Equal("Spell 'spell' is on no spell list, so no caster can choose it. Add it to a spell list.", warning.Message);
    }

    [Fact]
    public void TryNormalise_gives_the_valid_key_or_nothing()
    {
        Assert.True(SpellListKey.TryNormalise(" Fixture Seer ", out var key));
        Assert.Equal("fixture-seer", key);
        Assert.False(SpellListKey.TryNormalise("   ", out _));
        Assert.False(SpellListKey.TryNormalise("Seer's", out _));
    }
}
