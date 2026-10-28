using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

public enum Ability { Str, Dex, Con, Int, Wis, Cha }

public enum ContentKind { Species, Background, Class, Subclass, Feature, Feat, Spell, Item }

/// <summary>
/// Only <see cref="Published"/> revisions affect saved characters (SPEC I-01, I-06). The studio sandbox alone calculates
/// one draft, in memory, on an unsaved copy (<see cref="DraftOverlayCatalog"/>).
/// </summary>
public enum RevisionStatus { Draft, Published }

/// <summary>SPEC I-05. Unhandled mechanics keep their text and are marked <see cref="Reference"/>.</summary>
public enum AutomationStatus { Automatic, Assisted, Reference }

/// <summary>SPEC S-01. A source of rules content and its rights metadata.</summary>
public sealed record SourceRecord
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Publisher { get; init; }
    public required IReadOnlyList<string> RulesFamilies { get; init; }
    public required string EditionVersion { get; init; }
    public required string License { get; init; }
    public required bool Redistributable { get; init; }
    public string? Attribution { get; init; }

    /// <summary>CC-BY-4.0 §3(a)(1)(B): how this source's material was modified. Travels with <see cref="Attribution"/> into package notices.</summary>
    public string? ModificationNotice { get; init; }

    public DateTimeOffset? ImportedAt { get; init; }
    public string? Sha256 { get; init; }

    /// <summary>
    /// Obsolete machine-local path (M0/M1). Database migration v3 (ADR-005) moves it to an attachment record and keeps
    /// the old value in <c>sources.legacy_pdf_ref</c>. Never exported.
    /// </summary>
    public string? PdfRef { get; init; }

    /// <summary>ADR-005 (M2 item 6): the source's PDF attachment on this machine. Machine-local; never exported.</summary>
    public Guid? AttachmentId { get; init; }

    /// <summary>
    /// M6 slice 1 (LIVING_SPECS D14 item 6): true once material from outside the author entered this source: a PDF
    /// attached, a PDF candidate accepted, pages imported (and, later, an ADR-011 extension import). It is never cleared,
    /// not even by removing the PDF, and such a source is never shared. Absent (null) means false. Set only by this
    /// machine; a package can raise it but never lower it.
    /// </summary>
    public bool? ImportDerived { get; init; }

    /// <summary>
    /// M6 slice 1: where this source came from, set only by this machine. <see cref="SourceOrigin.Received"/> sources
    /// arrived in a package from someone else and can never be marked as your own work. Null for the bundled SRD packs and
    /// for sources stored before database v8, whose origin is not known.
    /// </summary>
    public SourceOrigin? Origin { get; init; }

    /// <summary>
    /// M6 slice 1, "Mark as shareable": when the author confirmed on this machine that the source is their own work. A
    /// source pack carries only sources with this set. Cleared when sharing is turned off or the source becomes
    /// import-derived.
    /// </summary>
    public DateTimeOffset? ShareConfirmedAt { get; init; }

    /// <summary>
    /// M6 slice 1: whether this source's content may leave the machine in a share (a character share, a source pack):
    /// redistributable and not import-derived. The bundled SRD packs pass (CC-BY-4.0); the caller decides whether a
    /// kind of export takes them at all.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool MayBeShared => Redistributable && ImportDerived != true;
}

/// <summary>M6 slice 1: <see cref="SourceRecord.Origin"/>.</summary>
public enum SourceOrigin
{
    /// <summary>Made on this machine with <c>source.createHomebrew</c>.</summary>
    Local,

    /// <summary>Arrived in a character package or a source pack.</summary>
    Received,
}

public sealed record PageRef(int Start, int? End = null)
{
    public override string ToString() => End is { } end && end != Start ? $"pp. {Start}-{end}" : $"p. {Start}";
}

public sealed record Provenance(Guid SourceId, PageRef? Page = null);

/// <summary>
/// Names a choice by the content that offers it (any of its revisions) and the <c>choiceId</c>, for
/// <see cref="ContentRevision.ExtendsChoice"/>.
/// </summary>
public sealed record ChoiceExtension(Guid ContentId, string ChoiceId);

/// <summary>An exact pin to one immutable content revision (ARCHITECTURE: ContentReference).</summary>
public sealed record ContentReference(Guid ContentId, Guid RevisionId);

/// <summary>
/// One immutable revision of a content entity. Identity is <see cref="ContentId"/>/<see cref="RevisionId"/>,
/// never <see cref="Name"/>. Unknown JSON fields round-trip through <see cref="Extensions"/>.
/// </summary>
public sealed record ContentRevision : IJsonOnDeserialized
{
    /// <summary>
    /// v3: level-gated grants and choices, and the <c>hitDie</c> effect (M1 item 5). v2: typed effects (ADR-003).
    /// v1 revisions are upcast to v2 on read (<see cref="UpgradedFrom"/>). v2 revisions are not upcast: v2 is a subset
    /// of v3, and keeping the written version keeps their serialized form, and so their hashes, unchanged (ADR-002).
    /// Older builds refuse v3 revisions (<c>content.schema-unsupported</c>) instead of ignoring the level gates.
    /// v4 (M2 item 5) adds <see cref="ExtendsChoice"/>; v2 and v3 revisions are not upcast, for the same reason.
    /// v5 (M2 spellcasting) adds the <c>spellcasting</c> and <c>spell</c> effect types; older revisions are not upcast.
    /// v6 (M3 B2) adds the <c>toggle</c> effect, <c>modifier.toggle</c>, and <c>roll</c> resourceContent, cost and variableCost.
    /// v7 (M3 C3) adds <c>spellcasting.multiclassCaster</c>.
    /// v8 (M2.2, the Fighter) adds the <c>attacks</c> and <c>criticalRange</c> fields as targets, armor proficiency grants
    /// (<c>armor.light</c> and so on), <c>armor.strength</c> and <c>armor.stealthDisadvantage</c>, <c>modifier.whileArmored</c>
    /// and <c>roll.bonus</c>. All are optional, so older revisions serialize unchanged.
    /// v9 (M5, ADR-010) adds the <c>scale</c> effect, the formula identifier <c>SCALE.&lt;id&gt;</c> and
    /// <c>spellcasting.multiclassCasterTable</c>, each read only in a v9 revision.
    /// v10 (Wave 3, D42) lets content target <c>speed</c> and <c>passive.*</c> (modifiers and restrictions), and adds
    /// <c>toggle.autoWhen</c>, <c>toggle.activation</c>, <c>resource.activation</c>, the grant kinds <c>damageResistance</c>,
    /// <c>damageImmunity</c> and <c>damageVulnerability</c> with <c>grant.damageType</c>, and the <c>item</c> effect (attunement
    /// and charges). All are optional, so older revisions serialize unchanged; each is read only in a v10 revision, and below
    /// v10 a key stays extension data (an <c>item</c> or a damage grant stays an unknown effect) and does nothing.
    /// v11 (play mechanics, D43) adds the <c>toggleGroup</c> effect and the toggle fields <c>group</c>, <c>groupContent</c>,
    /// <c>layer</c>, <c>allowedWith</c>, <c>allowedInBase</c>, <c>cost</c>, <c>resourceContent</c>, <c>level</c> and <c>duration</c>,
    /// the <c>upkeep</c> effect, and the resource fields <c>counts</c>, <c>resourceFull</c> and <c>allowance</c>. All are
    /// optional, so older revisions serialize unchanged; each is read only in a v11 revision, and below v11 a key stays
    /// extension data (a <c>toggleGroup</c> or <c>upkeep</c> stays an unknown effect) and does nothing.
    /// v12 (MECH-X item 0) adds <c>weapon.bonus</c>, a whole number from 0 to 10 that a weapon adds to its to-hit and damage. It
    /// is optional, so older revisions serialize unchanged; it is read only in a v12 revision, and below v12 the key stays
    /// extension data and does nothing.
    /// v12 also carries MECH-X item 1: <c>payment</c> (hit points or a spell slot instead of a pool) on a roll, a feature attack and
    /// a toggle, <c>castsSpell</c> on a roll, and <c>tags</c> on a spell. All are optional and read only in a v12 revision; below
    /// v12 each key stays extension data and does nothing.
    /// </summary>
    public const int CurrentSchemaVersion = 12;

    /// <summary>MECH-X item 1: cost payment, roll.castsSpell, spell.tags. <b>v12</b>, the same version as <see cref="WeaponBonusSchemaVersion"/>.</summary>
    public const int PaymentSchemaVersion = 12;

    /// <summary>The content schema version that adds <c>weapon.bonus</c> (MECH-X item 0): <b>v12</b>. A test pins that this is <see cref="PlayMechanicsSchemaVersion"/> plus one.</summary>
    public const int WeaponBonusSchemaVersion = 12;

    /// <summary>
    /// The content schema version that adds the Wave 3 details listed above: <b>v10</b> (speed and passive targets,
    /// <c>autoWhen</c>, <c>activation</c>, damage defences, <c>item</c>). Not the play mechanics version, which is
    /// <see cref="PlayMechanicsSchemaVersion"/> (v11).
    /// </summary>
    public const int PlayDetailsSchemaVersion = 10;

    /// <summary>
    /// The content schema version that adds the play mechanics details (D43): <b>v11</b> (toggle groups, overlays and costs).
    /// Not the Wave 3 version, which is <see cref="PlayDetailsSchemaVersion"/> (v10). A test pins that this is that plus one.
    /// </summary>
    public const int PlayMechanicsSchemaVersion = 11;

    /// <summary>The content schema version that adds the M2.2 combat details listed above.</summary>
    public const int CombatDetailsSchemaVersion = 8;

    /// <summary>The version the ADR-003 effect migration upcasts v1 revisions to.</summary>
    public const int TypedEffectsSchemaVersion = 2;

    private int _schemaVersion = CurrentSchemaVersion;

    public required Guid ContentId { get; init; }
    public required Guid RevisionId { get; init; }

    public int SchemaVersion { get => _schemaVersion; init => _schemaVersion = value; }

    /// <summary>The schema version the JSON was written in, when it was older and upcast on read.</summary>
    [JsonIgnore]
    public int? UpgradedFrom { get; private set; }
    public required ContentKind Kind { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> RulesFamilies { get; init; }
    public required Provenance Provenance { get; init; }
    public required RevisionStatus Status { get; init; }
    public string? Summary { get; init; }

    /// <summary>
    /// Content schema v4 (M2 item 5): this revision is an additional option of another content's choice, for example a
    /// homebrew subclass for the SRD Barbarian's subclass choice. Published revisions only; never matched by name.
    /// </summary>
    public ChoiceExtension? ExtendsChoice { get; init; }

    public IReadOnlyList<Effect> Effects { get => _effects; init => _effects = value; }

    private IReadOnlyList<Effect> _effects = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    [JsonIgnore]
    public ContentReference Reference => new(ContentId, RevisionId);

    /// <summary>
    /// ADR-003 migration: schemaVersion 1 differs only in the effect shape, which <see cref="EffectJsonConverter"/>
    /// already mapped while reading. Record the upcast to v2. Versions newer than supported are left as they are, so
    /// callers refuse them with a diagnostic. An <c>armor</c> effect is typed only in a v4 (or newer) revision
    /// (<see cref="ArmorEffect"/>); in an older one it stays unknown and is written back unchanged.
    /// </summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        if (_schemaVersion is >= 1 and < TypedEffectsSchemaVersion)
        {
            UpgradedFrom = _schemaVersion;
            _schemaVersion = TypedEffectsSchemaVersion;
        }
        bool Typeable(Effect e) =>
            e is UnknownEffect unknown && VersionedEffects.ByName.TryGetValue(unknown.DeclaredType, out var typed) && _schemaVersion >= typed.Version;
        if (_effects.Any(Typeable))
            _effects = [.. _effects.Select(e => Typeable(e) ? VersionedEffects.ByName[((UnknownEffect)e).DeclaredType].Type((UnknownEffect)e, _schemaVersion) : e)];
    }
}
