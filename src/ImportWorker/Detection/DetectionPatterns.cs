namespace TomeStack.ImportWorker.Detection;

/// <summary>
/// D64: the stable ids of the detector's patterns, named on a <see cref="CandidateSpan"/> so review can say which rule read a
/// proposed effect. They are ids, not prose, and name no third-party term. An id stays once shipped (stored candidates carry it);
/// a retired pattern keeps its constant.
/// </summary>
public static class DetectionPatterns
{
    // Spells
    public const string SpellHeader = "spell.header";
    public const string SpellComponents = "spell.components";

    // Feats
    public const string FeatHeader = "feat.header";
    public const string FeatPrerequisite = "feat.prerequisite";
    public const string FeatBonus = "feat.bonus";
    public const string FeatSkillProficiency = "feat.skill-proficiency";

    // Class features (the Features pass proposes text only today, so it sets a name span and no effect span)
    public const string FeatureHeader = "feature.header";
    public const string FeatureUsesPerRest = "feature.uses-per-rest";
    public const string FeatureRegainOnRest = "feature.regain-on-rest";

    // Table rows
    public const string WeaponRow = "weapon.row";
    public const string ArmorRow = "armor.row";

    // Classes
    public const string ClassTitle = "class.title";
    public const string ClassTableRow = "class.table.row";
    public const string ClassHitDie = "class.hit-die";
    public const string ClassSaves = "class.saves";
    public const string ClassArmorTraining = "class.armor-training";
    public const string ClassWeaponTraining = "class.weapon-training";
    public const string ClassSpellcasting = "class.spellcasting";
    public const string ClassSubclass = "class.subclass";
}
