// Mirrors the JSON contract of TomeStack.AppService / TomeStack.RulesCore (camelCase, string enums).
// The UI displays these values; it never calculates rules itself.

export type RulesFamilyId = 'srd-5.1' | 'srd-5.2.1';
export type Ability = 'str' | 'dex' | 'con' | 'int' | 'wis' | 'cha';
export type ContentKind = 'species' | 'background' | 'class' | 'subclass' | 'feature' | 'feat' | 'spell' | 'item';
export type AutomationStatus = 'automatic' | 'assisted' | 'reference';

export type AbilityScores = Record<Ability, number>;

export interface ContentReference {
  contentId: string;
  revisionId: string;
}

export interface PageRef {
  start: number;
  end?: number;
}

export interface FieldOverride {
  field: string;
  value: number;
  reason?: string;
}

/** Character schema v8 (D21): plain coin counts, never converted. */
export interface Currency {
  cp: number;
  sp: number;
  ep: number;
  gp: number;
  pp: number;
}

export interface Character {
  id: string;
  schemaVersion: number;
  name: string;
  rulesFamily: RulesFamilyId;
  campaignId?: string;
  /** Total level; with classes recorded it is their sum (the service keeps it in step). */
  level: number;
  /** Levels per class, in the order taken (character schema v3). */
  classes: ClassLevel[];
  /** Selections for choice effects (character schema v3). */
  choices: ChoiceSelection[];
  crossFamilyExceptions: CrossFamilyException[];
  /** Content used although the campaign does not allow its source, each with a reason (SPEC P-01). */
  campaignExceptions?: CampaignException[];
  baseAbilities: AbilityScores;
  pins: ContentReference[];
  overrides: FieldOverride[];
  /** Character schema v4; absent in a draft means fresh (full hit points, nothing spent). */
  play?: PlayState;
  /** Items carried; only equipped ones apply (M2 item 4). */
  equipment?: EquipmentEntry[];
  /** Character schema v8 (D21): coins carried. */
  currency?: Currency;
  /** Character schema v8 (D22): dated session notes; never in a share package. */
  notes?: { id: string; date: string; text: string; createdAt: string }[];
  /** Spells known or prepared, per caster (character schema v6, D04). */
  spells?: KnownSpell[];
  /** Character schema v9 (D42): personality, appearance, backstory, languages and tools. Absent while every field is empty. */
  details?: CharacterDetails;
  /** Character schema v9 (D42): carried things that are not content. Never calculated. */
  customItems?: CustomItem[];
  /** Character schema v9 (D42): attacks recorded by hand. Never calculated. */
  customAttacks?: CustomAttack[];
  /** Character schema v9 (D42): damage resistances, immunities and vulnerabilities (a reminder only). */
  damageDefences?: DamageDefence[];
  /** Character schema v9 (D42): familiars, mounts and the like. */
  companions?: Companion[];
  /** Character schema v9 (D42): experience points as the player records them. */
  xp?: number;
  updatedAt: string;
  /** SPEC C-08: set while archived. Only `character.archive` / `character.unarchive` change it; a save keeps it. */
  archivedAt?: string;
  // Unknown fields round-trip; keep them when re-saving.
  [extension: string]: unknown;
}

export interface EquipmentEntry {
  item: ContentReference;
  equipped: boolean;
  quantity: number;
  /** Character schema v9: names this entry. */
  id?: string;
  /** Character schema v9: absent means not attuned. */
  attuned?: boolean;
  /** Character schema v9: absent means none spent. */
  chargesSpent?: number;
  /** Character schema v11 (MECH-X item 3): mods attached to this one copy, at most 4. Absent means none; an emptied list is dropped. */
  mods?: ContentReference[];
}

/** Character schema v9 (D42): how the character looks, as free text. */
export interface CharacterAppearance {
  age?: string;
  height?: string;
  weight?: string;
  eyes?: string;
  skin?: string;
  hair?: string;
}

/** Character schema v9 (D42): the Details page. Each text is at most 2,000 characters; each list at most 30 entries of 80. */
export interface CharacterDetails {
  alignment?: string;
  personalityTraits?: string;
  ideals?: string;
  bonds?: string;
  flaws?: string;
  appearance?: CharacterAppearance;
  backstory?: string;
  alliesAndOrganisations?: string;
  languages?: string[];
  toolsAndOther?: string[];
}

export interface CustomItem {
  id: string;
  name: string;
  quantity: number;
  note?: string;
  equipped: boolean;
}

export interface CustomAttack {
  id: string;
  name: string;
  toHit: number;
  damage?: string;
  damageType?: string;
  note?: string;
}

export interface DamageDefence {
  type: string;
  kind: 'resistance' | 'immunity' | 'vulnerability';
}

export interface Companion {
  id: string;
  name: string;
  kind?: string;
  armorClass?: number;
  hitPointMaximum: number;
  speed?: string;
  attacks?: CustomAttack[];
  note?: string;
}

export interface CompanionPlay {
  id: string;
  current?: number | null;
  temporary: number;
  /** MECH-X item 4: set for a content companion (the content that gives it); `id` is then its `companionId`. */
  contentId?: string;
}

/** MECH-X item 4: a companion that content gives, calculated by the server. Its attacks and pools stay in `attacks` / `resources`, tagged with its `companionId`. */
export interface CompanionValue {
  content: ContentReference;
  contentName: string;
  effectId: string;
  companionId: string;
  label: string;
  armorClass?: number | null;
  armorClassTrace: TraceEntry[];
  hitPointMaximum?: number | null;
  hitPointTrace: TraceEntry[];
  current: number;
  temporary: number;
  speed?: string;
  attackEffectIds: string[];
  resourceIds: string[];
  automation: AutomationStatus;
  warnings: Diagnostic[];
  origin: TraceOrigin;
}

export interface ClassLevel {
  class: ContentReference;
  level: number;
}

export interface ChoiceSelection {
  source: ContentReference;
  choiceId: string;
  selected: ContentReference[];
}

/** A choice an active revision offers; unresolved ones are flagged on the sheet (SPEC C-01). */
export interface ChoiceStatus {
  source: ContentReference;
  sourceName: string;
  choiceId: string;
  text?: string;
  count: number;
  options: ContentReference[];
  selected: ContentReference[];
  resolved: boolean;
  /** Content v12: when picks may be replaced (`max` is always set on the sheet). Absent below v12. */
  replaceable?: { when: 'levelUp' | 'longRest'; max: number };
}

export interface Diagnostic {
  code: string;
  message: string;
  content?: ContentReference;
  effectId?: string;
}

export type TraceOriginKind = 'characterChoice' | 'content' | 'rulesPolicy' | 'override';

export interface TraceOrigin {
  kind: TraceOriginKind;
  rulesFamily: RulesFamilyId;
  content?: ContentReference;
  contentName?: string;
  effectId?: string;
  sourceId?: string;
  sourceTitle?: string;
  page?: PageRef;
}

export interface TraceInput {
  name: string;
  value: number;
}

export interface TraceEntry {
  order: number;
  operation: 'base' | 'add' | 'derive' | 'replace' | 'set' | 'ignored' | 'override' | string;
  description: string;
  amount?: number;
  result: number;
  origin: TraceOrigin;
  /** The field this step belongs to; a field's trace includes the steps of the fields it reads. */
  field?: string;
  inputs?: TraceInput[];
}

export interface DerivedValue {
  field: string;
  label: string;
  value: number;
  computedValue: number;
  trace: TraceEntry[];
  warnings: Diagnostic[];
  automation: AutomationStatus;
  override?: FieldOverride;
  units: 'score' | 'modifier' | 'bonus' | 'feet' | string;
  /** D24: on saves and skills, from a grant. */
  mark?: 'none' | 'proficient' | 'expertise';
  /** Content v11 (D43 S6): on saves, skills, ability modifiers and initiative, the advantage or disadvantage the sheet found; absent when none. */
  mode?: RollModeSummary;
}

/** One reason a d20 roll has advantage or disadvantage; `against` (condition keys) means the player confirms it, so it is only a note. */
export interface RollModeReason {
  mode: 'advantage' | 'disadvantage';
  source: string;
  content: ContentReference;
  against?: string[];
  /** True when the service applies this reason by itself (an automatic effect with no `against`). */
  automatic: boolean;
}

/** The mode the sheet calculates for a field, and every reason listed for it. */
export interface RollModeSummary {
  automatic: RollMode;
  reasons: RollModeReason[];
}

export type RestPeriod = 'shortRest' | 'longRest';

export interface RecoveryInfo {
  effectId: string;
  on: RestPeriod;
  amount: string;
  text?: string;
}

/** A limited-use resource; `maximum`/`current` are absent when it is tracked by hand. */
export interface ResourceValue {
  content: ContentReference;
  contentName: string;
  effectId: string;
  resourceId: string;
  label: string;
  maximum?: number;
  spent: number;
  current?: number;
  trace: TraceEntry[];
  warnings: Diagnostic[];
  automation: AutomationStatus;
  recoveries: RecoveryInfo[];
  text?: string;
  /** Content v10 (Wave 3 item 22): the group the resource is listed under on Play; absent below v10 and when the content names none. */
  activation?: Activation;
  /** Content v11 (D43): the pool counts up (`spent` is what has built up toward `maximum`, e.g. strain). */
  countsUp?: boolean;
  /** Content v11 (D43): what the pool announces when it fills; only with `countsUp`. */
  fullText?: string;
  /** Content v11 (D43): the pool is a per-turn allowance (such as reactions); actions that spend it are listed under its name. */
  allowance?: boolean;
  /** MECH-X item 4: the content companion this pool belongs to; the companion card shows it, not the Resources panel. */
  companionId?: string;
}

/** Content v12 (MECH-X item 1): how a payment is made. A pool payment is `resource` (the sheet leaves `payment` out for it). */
export type PaymentKind = 'resource' | 'hitPoints' | 'slot';

/** The slot a slot payment would take now (R35). */
export interface SlotChoice {
  level: number;
  pact: boolean;
}

/**
 * Content v12 (MECH-X item 1, R40): a hit-point or slot payment on a roll, an attack or a toggle, and whether it can be made now.
 * `payable` is false, with `reason`, when it cannot; it never changes `available`.
 */
export interface PaymentValue {
  kind: PaymentKind;
  payable: boolean;
  reason?: string;
  mayDropToZero?: boolean;
  minSlotLevel?: number;
  slot?: SlotChoice;
}

/** Content v12 (R38): the spell a roll casts without a slot; the numbers are the primary caster's (absent without a caster: by hand). */
export interface CastSpellValue {
  spell: ContentReference;
  name: string;
  level: number;
  attack: 'none' | 'melee' | 'ranged';
  save?: Ability;
  dice?: string;
  attackBonus?: number;
  saveDc?: number;
}

export interface FeatureEffect {
  id: string;
  type: string;
  automation: AutomationStatus;
  text?: string;
  label?: string;
  dice?: string;
  resourceId?: string;
  /** SPEC C-04 (content v5): when the roll's action is used; absent is "other". */
  activation?: Activation;
  /** Content v6: the content id that defines `resourceId` (a shared resource). */
  resourceContent?: string;
  /** Content v6: uses spent, or the most with `variableCost`. */
  cost?: number;
  variableCost?: boolean;
  /** Content v8: the roll's bonus formula evaluated for this character (for example the Fighter level). */
  bonus?: number;
  /** Content v11 (D43): false when the action is outside the form it needs (`unavailableReason` says which). The server always sends it. */
  available: boolean;
  unavailableReason?: string;
  /** Content v11 (D43): the save the action asks for, with its DC evaluated for this character (`saveDc` is absent when it could not be). */
  saveAbility?: Ability;
  saveDc?: number;
  saveTrace?: TraceEntry[];
  area?: RollArea;
  range?: string;
  targets?: string;
  /** Content v11 (D43): extra dice for each unit spent beyond the base cost. */
  perUnit?: RollPerUnit;
  /** Content v11 (D43): the other pools a use also changes, each amount evaluated for this character (null when it could not be). */
  alsoChange?: AlsoChangeValue[];
  /** Content v11 (D43): the cost and per-unit amount use up the counting-up pool's built-up value. */
  drawsDown?: boolean;
  /** The roll's content `rollId` (what `useAction` names); it can differ from `id`. */
  rollId?: string;
  /** Content v12 (MECH-X item 1): a hit-point or slot payment and whether it can be made now; absent for a pool payment. */
  payment?: PaymentValue;
  /** Content v12 (R38): the spell this roll casts without a slot; absent when it casts none or the spell cannot be used. */
  castsSpell?: CastSpellValue;
}

export interface AlsoChangeValue {
  resourceContent: string;
  resourceId: string;
  change: 'spend' | 'regain';
  amount?: number | null;
}

export type AreaShape = 'line' | 'cone' | 'sphere' | 'cube' | 'cylinder' | 'emanation';

/** Content v11 (D43): an area in feet; `width` applies to a line only. */
export interface RollArea {
  shape: AreaShape;
  size: number;
  width?: number;
}

export interface RollPerUnit {
  dice: string;
  maxUnits?: number;
}

/** Content v6 (M3 B2): something switched on and off at the table. */
export interface ToggleValue {
  content: ContentReference;
  contentName: string;
  effectId: string;
  toggleId: string;
  label: string;
  on: boolean;
  resourceId?: string;
  text?: string;
  /** Content v10 (D38): the sheet switches this toggle itself while the character is bloodied; it has no switch and is never stored. */
  auto?: boolean;
  /** Content v10 (Wave 3 item 22): the group the toggle is listed under on Play; absent below v10 and when the content names none. */
  activation?: Activation;
  /** Content v11 (D43): the form group the toggle belongs to (`group` id and the content that declares it); absent when ungrouped. */
  group?: string;
  groupContent?: string;
  /** The label of the toggleGroup effect that declares the group (a calculated sheet field); absent when the declarer is not on the character. */
  groupLabel?: string;
  /** A primary is the form itself (one on at a time); an overlay stacks on a primary. Always present when `group` is. */
  layer?: 'primary' | 'overlay';
  /** What switching it on spends from `costResourceContent`'s pool of `resourceId`; absent with a pool means 1. */
  cost?: number;
  costResourceContent?: string;
  /** Content v12 (MECH-X item 1): a hit-point or slot payment and whether it can be made now; absent for a pool payment. */
  payment?: PaymentValue;
  /** False when the group rules currently refuse switching it on (`unavailableReason` says why). The server always sends it. */
  available: boolean;
  unavailableReason?: string;
  /** Content v11 (D43): rounds the toggle lasts once on in an encounter, and what is left of them (absent: not counted, track by hand). */
  duration?: number;
  roundsLeft?: number;
  /** An overlay's own rule as declared: the toggles it is allowed with (a missing `contentId` is its own content), and whether it is allowed with no form on. */
  allowedWith?: { contentId?: string; toggleId: string }[];
  allowedInBase?: boolean;
}

export type TurnMoment = 'turnStart' | 'turnEnd';

/** Content v11 (D43): a pool change at the start or end of the turn, optionally tied to a toggle. Previewed and confirmed by a turn command. */
export interface UpkeepValue {
  content: ContentReference;
  effectId: string;
  toggleContent?: string;
  toggleId?: string;
  at: TurnMoment;
  resourceContent: string;
  resourceId: string;
  change: 'spend' | 'regain';
  amount?: number;
  all: boolean;
  ifShort: 'ask' | 'end';
  label: string;
}

export type Activation = 'action' | 'bonusAction' | 'reaction' | 'other';

/** Content v11 (D43): an attack rider that applies to an attack now, with its numbers. */
export interface RiderValue {
  content: ContentReference;
  effectId: string;
  dice?: string;
  toHit: number;
  damageBonus: number;
  damageType?: string;
}

/** SPEC C-02, C-04: an attack with an equipped weapon, or (D43) an attack a feature gives. */
export interface AttackEntry {
  item: ContentReference;
  name: string;
  effectId: string;
  attack: 'melee' | 'ranged';
  /** Absent for a feature attack. */
  category?: 'simple' | 'martial' | null;
  ability: Ability;
  toHit: number;
  damage: string;
  versatileDamage?: string;
  damageType: string;
  properties: string[];
  range?: string;
  mastery?: string;
  proficient: boolean;
  automation: AutomationStatus;
  trace: TraceEntry[];
  warnings: Diagnostic[];
  origin: TraceOrigin;
  /** Content v11 (D43): where the row comes from. A feature attack is rolled by `item` + `effectId` (`content` + `effectId`), not as a weapon. */
  source: 'weapon' | 'feature';
  /** MECH-X item 3: the position of the equipment entry, set only when the weapon is carried as several copies (one row per copy). */
  entryIndex?: number;
  tags?: string[] | null;
  /** The attack can stand in for one attack of the Attack action. */
  replacesAttack: boolean;
  riders?: RiderValue[] | null;
  /** False when unavailable (`unavailableReason` says why); the server always sends it. An unavailable attack has no damage to roll. */
  available: boolean;
  unavailableReason?: string | null;
  activation?: Activation | null;
  /** What using the attack spends: `cost` of the pool `resourceId` in `costResourceContent`. */
  costResourceContent?: string | null;
  resourceId?: string | null;
  cost?: number | null;
  /** Content v12 (MECH-X item 1): a hit-point or slot payment (a feature attack); absent for a pool payment. */
  payment?: PaymentValue | null;
  /** MECH-X item 4: the content companion this attack belongs to; the companion card shows it, not the Actions list. */
  companionId?: string;
}

/** SPEC I-05: an active revision with its text and automation status. */
export interface FeatureEntry {
  content: ContentReference;
  name: string;
  kind: ContentKind;
  summary?: string;
  via?: string;
  origin: TraceOrigin;
  automation: AutomationStatus;
  effects: FeatureEffect[];
  diagnostics: Diagnostic[];
  /** D25: the content whose grant or choice brought this one in. */
  grantedBy?: ContentReference;
  grantedByName?: string;
}

/** M3 B1 (`character.mechanics`): one effect of a feature, how far TomeStack automates it, and what the player does by hand. */
export interface MechanicEntry {
  content: ContentReference;
  featureName: string;
  kind: ContentKind;
  effectId?: string;
  /** The effect's type; `"text"` for a feature with no effects. */
  effectType: string;
  /** What the author wrote. Not what TomeStack delivers: read `effective` for that. */
  automation: AutomationStatus;
  /** Import Lab (R10): the author's value lowered by what the engine does (capability registry). Absent from an older host. */
  effective?: EffectiveAutomation;
  manualStep?: string;
  sourceId?: string;
  page?: PageRef;
}

/** `status` is absent when `unsupported`: the effect's type is not one this build knows. The server omits null fields. */
export interface EffectiveAutomation {
  status?: AutomationStatus | null;
  unsupported: boolean;
  ceiling?: AutomationStatus | null;
  reason?: string | null;
  evidence?: string | null;
}

/** `Unsupported` is a subset of `mechanics`: effects of a type this build does not know. */
export interface MechanicsReport {
  mechanics: MechanicEntry[];
  unsupported: MechanicEntry[];
}

export interface HitPointState {
  maximum: number;
  current: number;
  temporary: number;
}

/** Hit dice of one size; sizes pool across classes (character schema v5). */
export interface HitDiceValue {
  die: number;
  total: number;
  spent: number;
  remaining: number;
  classes: string[];
}

/** One hit die spent on a short rest and what it shows (1 to `die`). */
export interface HitDieRoll {
  die: number;
  roll: number;
}

export interface CharacterSheet {
  characterId: string;
  rulesFamily: RulesFamilyId;
  fields: DerivedValue[];
  diagnostics: Diagnostic[];
  choices?: ChoiceStatus[];
  active?: ContentReference[];
  resources?: ResourceValue[];
  /** MECH-X item 4: companions that content gives (absent when none). */
  companions?: CompanionValue[];
  features?: FeatureEntry[];
  hitPoints?: HitPointState;
  hitDice?: HitDiceValue[];
  spellcasting?: SpellcastingEntry[];
  spellSlots?: SlotValue[];
  pactSlots?: SlotValue;
  attacks?: AttackEntry[];
  toggles?: ToggleValue[];
  /** Content v11 (D43): the upkeeps the active content declares, for the turn panel and the combat strip; absent when there are none. */
  upkeeps?: UpkeepValue[];
  /** Content v9 (ADR-010): each class-table column at the character's level in that class. */
  scales?: ScaleValue[];
  /** D38: current hit points are at or below half the maximum (rounded down), with a maximum of at least 1. Absent when not bloodied. */
  bloodied?: boolean;
  /** Wave 3 item 21: the character's own damage defences and the v10 damage grants, merged; absent when there are none. */
  damageDefences?: DamageDefenceValue[];
  /** Wave 3 item 25: each equipment entry of an item that needs attunement or has charges, or (MECH-X item 3) any weapon or armor entry or one with mods, carried or equipped; absent when there are none. */
  itemUses?: ItemUseValue[];
  /** Wave 3 item 27: experience points are tracked and the total has reached the next level's threshold. Display only; absent otherwise. */
  levelUpAvailable?: boolean;
  /** Wave 3 item 27: the total needed for the next level while experience points are tracked and the level is 1 to 19; absent otherwise. */
  xpForNextLevel?: number;
  /** Content v11 (D43 S7): the damage responses the active content declares; absent when there are none. */
  responses?: ResponseValue[];
}

/** When an item's charges come back; absent `regain` means all of them. */
export interface ItemChargeRecovery {
  on: RestPeriod;
  regain?: string;
}

/**
 * One equipment entry of an item with attunement or charges. `entryKey` names it for a rest roll or a charges roll: its id, or `#`
 * and its position in the list while it has none (`entryId` absent). Charges are per entry; `chargesMax` is absent when the item has none.
 */
export interface ItemUseValue {
  item: ContentReference;
  name: string;
  entryKey: string;
  entryId?: string;
  entryIndex: number;
  equipped: boolean;
  requiresAttunement: boolean;
  attuned: boolean;
  chargesMax?: number;
  chargesSpent: number;
  chargesLeft?: number;
  recovery?: ItemChargeRecovery;
  /** MECH-X item 3: the mods attached to this entry. */
  mods: ItemModInfo[];
  /** MECH-X item 3: which kinds of mod this entry can host (a weapon, armor or shield effect on its item); empty for other items. */
  hosts: ('weapon' | 'armor')[];
}

/** MECH-X item 3: one mod on an equipment entry, named by the service. */
export interface ItemModInfo {
  reference: ContentReference;
  name: string;
  appliesTo: 'weapon' | 'armor' | 'any';
  bonus?: number;
}

/** What the player rolled for the `regain` dice of one entry's charges on a rest: the whole total. */
export interface ChargeRoll {
  entry: string;
  roll: number;
}

/** Where a damage defence comes from: the character's own list (no `content`) or a damage grant of active content. */
export interface DamageDefenceSource {
  name: string;
  content?: ContentReference;
  effectId?: string;
}

/** One damage resistance, immunity or vulnerability on the sheet: several sources of the same type and kind are one entry. */
export interface DamageDefenceValue {
  type: string;
  kind: DamageDefence['kind'];
  sources: DamageDefenceSource[];
}

/**
 * What a damage command did (set only on the view it returns, never stored). `adjusted` is the amount after defences;
 * `warnings` carries codes and numbers only (`play.massive-damage`).
 */
export interface DamageOutcome {
  amount: number;
  type?: string;
  effect: 'none' | 'immune' | 'halved' | 'doubled' | 'halvedThenDoubled';
  adjusted: number;
  absorbed: number;
  lost: number;
  warnings: Diagnostic[];
}

/** Content v11 (D43 S7): when a damage response applies. */
export type ResponsePhase = 'damageReduction' | 'hitPointOutcome' | 'threshold';
export type ResponseOutcomeKind = 'spend' | 'regain' | 'toggleOn' | 'offerRoll' | 'regainHitPoints' | 'temporaryHitPoints';

/** What a chosen response does, evaluated. */
export interface ResponseOutcomeValue {
  kind: ResponseOutcomeKind;
  resourceContent?: string;
  resourceId?: string;
  amount?: number;
  toggle?: { contentId?: string; toggleId: string };
  roll?: ContentReference;
  rollEffectId?: string;
  rollId?: string;
}

/** One damage response of the character, calculated (`CharacterSheet.responses`; also inside each offer). Numbers come from the server. */
export interface ResponseValue {
  contentId: string;
  contentName: string;
  responseId: string;
  label: string;
  phase: ResponsePhase;
  amount?: number;
  costResourceContent?: string;
  resourceId?: string;
  cost?: number;
  available: boolean;
  unavailableReason?: string;
  text?: string;
  atOrBelow?: number;
  survived: boolean;
  outcomes?: ResponseOutcomeValue[];
}

/** One outcome of a `response` effect as authored (content v11); which members apply depends on `kind`. */
export interface ResponseOutcomeDef {
  kind: ResponseOutcomeKind;
  resourceId?: string;
  resourceContent?: string;
  amount?: string;
  toggle?: { contentId?: string; toggleId: string };
  rollId?: string;
  rollContent?: string;
}

/** A response chosen for a hit. */
export interface ResponseChoice {
  contentId: string;
  responseId: string;
}

/** A response eligible for a hit; `offered` is false (with `reason`) when it cannot be chosen now. */
export interface ResponseOffer {
  response: ResponseValue;
  offered: boolean;
  reason?: string;
}

/** A roll a chosen response offers; the confirm returns it as a `damage.roll-offered` notice carrying the same content and effect id. */
export interface OfferedRoll {
  content: ContentReference;
  effectId: string;
  rollId: string;
  label: string;
}

/**
 * `character.damagePreview`: one hit with the responses chosen so far. When `notes` is not empty the choices cannot be applied and
 * `after` is the current state, not the result. `updatedAt` is the stored stamp it was planned from; the confirm sends it back.
 */
export interface DamagePreview {
  characterId: string;
  amount: number;
  type?: string;
  reduction: number;
  outcome: DamageOutcome;
  after: HitPointState;
  concentrationDc?: number;
  concentrationEnds: boolean;
  offers: ResponseOffer[];
  rolls: OfferedRoll[];
  notes: Diagnostic[];
  updatedAt: string;
}

export interface ScaleValue {
  class: ContentReference;
  className: string;
  content: ContentReference;
  scaleId: string;
  label: string;
  classLevel: number;
  value: number;
}

export interface ResourceUse {
  contentId: string;
  resourceId: string;
  spent: number;
}

/** SPEC C-05, character schema v5: changed only by the confirmed `character.play` command and confirmed rests. */
export interface PlayState {
  /** Absent or null: at the maximum. */
  currentHitPoints?: number | null;
  temporaryHitPoints: number;
  resources: ResourceUse[];
  conditions: string[];
  exhaustion: number;
  hitDiceSpent?: { die: number; spent: number }[];
  deathSaves?: { successes: number; failures: number };
  inspiration?: boolean;
  /** Character schema v8 (D19): the concentration spell and a pending Constitution save DC after damage. */
  concentration?: { spell: ContentReference; name: string; pendingSaveDc?: number };
  /** Character schema v9 (D42): current and temporary hit points per companion. */
  companions?: CompanionPlay[];
  /** Character schema v7 (M3 B2): the toggles switched on; v10 (D43) adds the rounds left on a timed one. */
  toggles?: { contentId: string; toggleId: string; roundsLeft?: number }[];
  /** Character schema v10 (D43): the encounter in progress. Round 0 means the first turn has not started; show max(1, round). */
  encounter?: { round: number };
}

export type PlayActionKind =
  | 'spend'
  | 'regain'
  | 'damage'
  | 'heal'
  | 'setTemporaryHitPoints'
  | 'setHitPoints'
  | 'addCondition'
  | 'removeCondition'
  | 'setExhaustion'
  | 'recordDeathSave'
  | 'addDeathSaveFailure'
  | 'clearDeathSaves'
  | 'setInspiration'
  | 'spendSlot'
  | 'regainSlot'
  | 'spendPactSlot'
  | 'regainPactSlot'
  | 'toggleOn'
  | 'toggleOff'
  | 'startConcentration'
  | 'endConcentration'
  | 'clearConcentrationCheck'
  | 'spendCharge'
  | 'regainCharge'
  | 'companionDamage'
  | 'companionHeal'
  | 'companionSetHitPoints'
  | 'companionSetTemporaryHitPoints'
  | 'startEncounter'
  | 'endEncounter'
  | 'useAction';

export interface PlayAction {
  action: PlayActionKind;
  amount?: number;
  /** With a `companion*` action: the content that gives the companion (a content companion); absent for a hand companion. */
  contentId?: string;
  resourceId?: string;
  condition?: string;
  /** Content v6: the toggle to switch on or off (with `contentId`). */
  toggleId?: string;
  /** With `toggleOn` (content v11): switch the form already on in the same group off first. Without it the server refuses with `toggle.group-occupied`. */
  replace?: boolean;
  /** D44: the character's `updatedAt` the change was decided from. Required by a compound change (`play.basis-required` without it); any stamp that is not the stored one is `character.stale`. */
  expectedUpdatedAt?: string;
  /** With `damage`: one of the thirteen damage types (any case). Absent: untyped, so no defence applies. */
  damageType?: string;
  /** With `damage` (D43 S7): the damage responses chosen for this hit. A non-empty list makes the change compound (it needs `expectedUpdatedAt`). */
  responses?: { contentId: string; responseId: string }[];
  /** With `useAction` (D43 S8, with `contentId`): the roll to use, by its `rollId`. Compound (needs `expectedUpdatedAt`); `amount` is the units of a per-unit roll. */
  rollId?: string;
  /** With `endEncounter` (D43 S8): counting-up pools to reset to 0 in the same change, as `{contentId}/{resourceId}` (at most 64). Any other action, or a pool that does not count up, is `play.clear-invalid`. */
  clear?: string[];
  /** With `spendCharge` or `regainCharge`: the equipment entry, by its id, or by `entryIndex` and `contentId` while it has none. */
  entryId?: string;
  entryIndex?: number;
  /** With a `companion*` action (Wave 3 item 23): the companion, by its id. `companionSetHitPoints` takes the current hit points in `amount` and, when sent, the temporary ones in `temporary`; `companionSetTemporaryHitPoints` takes only the temporary ones in `amount` and keeps the stored current. */
  companionId?: string;
  temporary?: number;
}

export interface RestChange {
  id: string;
  /** `hitDie`: one spent hit die of a short rest (`die`, `amount` hit points); `hitDice`: dice regained on a long rest. */
  kind: 'hitPoints' | 'temporaryHitPoints' | 'resource' | 'exhaustion' | 'hitDie' | 'hitDice' | 'deathSaves' | 'spellSlots' | 'pactSlots' | 'toggle' | 'concentration' | 'charges' | 'encounter';
  slotLevel?: number;
  die?: number;
  amount?: number;
  label: string;
  from: number;
  to: number;
  reason: string;
  origin: TraceOrigin;
  contentId?: string;
  resourceId?: string;
  /** Set when the change depends on the situation; the player decides. */
  condition?: string;
  /** `charges` (Wave 3 item 25): `resourceId` is the entry key, `dice` what the player still rolls while `spentAfter` is absent. */
  dice?: string;
  spentAfter?: number | null;
}

export type TurnChangeKind = 'round' | 'duration' | 'toggleEnd' | 'spend' | 'regain';

/** D43 slice 3: one proposed change of a turn. `id` is stable for the same character state, so an unticked id is skipped when confirmed. */
export interface TurnChange {
  id: string;
  kind: TurnChangeKind;
  label: string;
  /** For `spend` and `regain`, the pool's spent count before and after. */
  from: number;
  to: number;
  reason: string;
  origin: TraceOrigin;
  /** Cannot be unticked (a toggle that ends because it cannot pay, or because of an earlier change or choice). */
  required?: boolean;
  contentId?: string;
  resourceId?: string;
  toggleId?: string;
}

/** What the player does about an upkeep the pool cannot cover: end the toggle, or keep it and settle the rest by hand. */
export type TurnChoiceOption = 'end' | 'keep';

/** A question the plan asks. `id` is `short:{content}:{effect}`; `label` is the whole line to show ("Iron stance needs 2, has 1"). */
export interface TurnChoicePrompt {
  id: string;
  label: string;
  contentId: string;
  toggleId: string;
  needs: number;
  has: number;
}

/** A message to show when a counting-up pool becomes full. */
export interface TurnPrompt {
  id: string;
  text: string;
  origin: TraceOrigin;
}

/** `character.turnPreview`: the plan for the selection sent, already reflecting it. `updatedAt` is the stored stamp it was planned from; `character.turn` must send it back. */
export interface TurnPreview {
  at: TurnMoment;
  changes: TurnChange[];
  choices: TurnChoicePrompt[];
  manual: Diagnostic[];
  prompts: TurnPrompt[];
  updatedAt: string;
}

/** A rest proposal (SPEC C-05, D01). `basis` must be sent back to apply exactly this proposal. */
export interface RestPreview {
  kind: RestPeriod;
  changes: RestChange[];
  manual: Diagnostic[];
  basis: string;
}

export type RollMode = 'normal' | 'advantage' | 'disadvantage';

export interface RollModifier {
  label: string;
  amount: number;
  origin?: TraceOrigin;
}

export interface DieResult {
  term: number;
  sides: number;
  value: number;
  kept: boolean;
  fromCritical: boolean;
}

export interface RollProvenance {
  rollId: string;
  label: string;
  content?: ContentReference;
  contentName?: string;
  effectId?: string;
  sourceId?: string;
  sourceTitle?: string;
  page?: PageRef;
  linkedResourceId?: string;
  /** The content that defines the linked resource (another feature's, for a shared one). */
  linkedResourceContent?: string;
}

/** SPEC C-04: a roll record. Rolling never changes the character. */
export interface RollRecord {
  formula: string;
  mode: RollMode;
  critical: boolean;
  dice: DieResult[];
  diceTotal: number;
  expressionConstant: number;
  modifiers: RollModifier[];
  total: number;
  provenance?: RollProvenance;
  /** D43 S5: the uses this roll implies spending from a pool (informational; the Spend button sends `character.play`). */
  spend?: RollSpend;
  /** D43 S6: the mode the sheet applied to a field roll, with the content that gave it (absent when the sheet applied none). */
  automaticMode?: RollMode;
  modeSources?: string[];
  /** D72: the sheet's trace for the number rolled (a field, an attack, a caster's spell attack); absent when the roll has none. */
  explain?: TraceEntry[];
  /** MECH-X item 1: the hit-point or slot payment a roll or a feature attack's to-hit roll implies. Rolling never pays; the Pay button sends `useAction`. */
  payment?: RollPayment;
}

/** `contentId` declares the roll; `useId` is what `useAction` names (a roll's `rollId`, or a feature attack's effect id). `amount` is absent for a slot. */
export interface RollPayment {
  contentId: string;
  useId: string;
  kind: PaymentKind;
  amount?: number;
  minSlotLevel?: number;
  mayDropToZero: boolean;
}

export interface RollSpend {
  contentId: string;
  resourceId: string;
  amount: number;
  /** D43 S8: the pool counts up and this roll draws it down; the record's Spend is a `useAction`, never a plain `spend`. */
  drawsDown?: boolean;
}

/** A content roll effect (`content` + `effectId`) or a sheet field as a d20 test (`field`). */
export interface RollTarget {
  content?: ContentReference;
  effectId?: string;
  field?: string;
  mode?: RollMode;
  critical?: boolean;
  /** One of the character's hit dice (d6–d12), the die alone. */
  hitDie?: number;
  /** D43 S5: the points a per-unit action spends (its base cost plus the extra units); the server checks the range. */
  amount?: number;
  /** A death saving throw: a d20 with no modifier. */
  deathSave?: boolean;
  /** One of the character's spells: its attack roll (`spellAttack`) or its dice. */
  spell?: ContentReference;
  spellAttack?: boolean;
  /** An equipped weapon: its attack roll, or its damage (`damage`), two-handed (`versatile`). */
  weapon?: ContentReference;
  damage?: boolean;
  versatile?: boolean;
  /** One of the character's own hand-typed attacks (`character.customAttacks`): its attack roll, or its damage (`damage`). */
  customAttack?: string;
  /** The entry key of an item whose `regain` dice to roll for its charges (Wave 3 item 25). */
  chargeEntry?: string;
  /** One attack line of one of the character's companions (Wave 3 item 23): its attack roll, or its damage (`damage`). */
  companionAttack?: { companionId: string; attackId: string };
}

/** SPEC P-01: a local campaign profile. It never changes calculation; it warns about content outside it. */
export interface Campaign {
  id: string;
  schemaVersion?: number;
  name: string;
  rulesFamily: RulesFamilyId;
  allowedSources: string[];
  houseRules?: string;
  /**
   * M6 slice 2: allowed sources a campaign pack left out that are not installed here yet (each is also in
   * allowedSources). Set only by an import; the service keeps or drops them on save.
   */
  pendingSources?: PendingSource[];
  updatedAt?: string;
}

export interface PendingSource {
  sourceId: string;
  title: string;
  publisher: string;
  license: string;
}

export interface CampaignStatus {
  campaignId: string;
  name: string;
  rulesFamily: RulesFamilyId;
  warnings: Diagnostic[];
}

export interface CampaignException {
  content: ContentReference;
  reason: string;
  recordedAt?: string;
}

export interface CharacterView {
  character: Character;
  sheet: CharacterSheet;
  campaign?: CampaignStatus;
  /** Set only on the view a damage command returns. */
  damage?: DamageOutcome;
  /** D44: present only while the service holds a one-level undo for the last compound play change ("switch to Tide form"). */
  undoLabel?: string;
  /** D43 slice 3: set only on the reply of a turn, an encounter start or end, or a spend into a full pool ("duration: track by hand", `resource.full`). Never on a later read. */
  notices?: Diagnostic[];
}

export interface CharacterSummary {
  id: string;
  name: string;
  rulesFamily: RulesFamilyId;
  updatedAt: string;
  /** SPEC C-08: set while the character is archived. */
  archivedAt?: string;
  /** The contents this character records a cross-family exception for (ids only). */
  exceptionContentIds?: string[];
  /** D25: the character's total level. */
  level: number;
}

/** SPEC C-08: `character.archivePreview`. Nothing is removed by archiving. */
export interface ArchivePreview {
  characterId: string;
  name: string;
  alreadyArchived: boolean;
  gapNotes: number;
  campaign?: string;
}

export interface RulesFamilyPolicy {
  id: RulesFamilyId;
  displayName: string;
  abilityIncreaseSource: ContentKind;
  backgroundGrantsFeat: boolean;
  longRestExhaustionNeedsFoodAndDrink: boolean;
}

/** BACKLOG B06: a recorded, deliberate use of a pinned revision outside its rules families. */
export interface CrossFamilyException {
  content: ContentReference;
  reason: string;
  recordedAt?: string;
}

export interface AppInfo {
  version: string;
  schemaVersion: number;
  rulesFamilies: RulesFamilyPolicy[];
  /** Startup warnings, e.g. `data-dir.sync-root` when the data folder is inside OneDrive (ADR-005). */
  warnings: Diagnostic[];
  /** Every calculated field and its label (homebrew studio targets). */
  fields: FieldInfo[];
}

export interface ContentOption {
  reference: ContentReference;
  kind: ContentKind;
  name: string;
  rulesFamilies: RulesFamilyId[];
  compatible: boolean;
  sourceId: string;
  sourceTitle: string;
  page?: string;
  summary?: string;
  /** Set when listed for a campaign: whether it allows this option's source. */
  allowedInCampaign?: boolean;
  /** A newer published revision of the same content exists: listed so pinned names resolve, but not offered for new picks. */
  superseded?: boolean;
  /** False when another revision grants it or offers it in a choice (class features, skill options): it is not pinned directly. */
  standalone?: boolean;
  /** Spell options only (content schema v5). */
  spell?: { level: number; lists: string[]; school?: string; concentration: boolean; ritual: boolean; tags?: string[] };
  /** Item options only (content schema v12): the item is a mod and attaches to a weapon, armor, or either. */
  modOf?: 'weapon' | 'armor' | 'any';
}

/** A spell recorded for one caster; `caster` is the content id of the class or subclass with spellcasting. */
export interface KnownSpell {
  caster: string;
  spell: ContentReference;
  prepared?: boolean;
}

export interface SlotValue {
  level: number;
  maximum: number;
  spent: number;
  remaining: number;
  field: string;
}

export interface SpellEntry {
  spell: ContentReference;
  name: string;
  level: number;
  prepared: boolean;
  summary?: string;
  text?: string;
  school?: string;
  castingTime?: string;
  range?: string;
  components?: string;
  duration?: string;
  concentration: boolean;
  ritual: boolean;
  /** Content v12 (R39): the spell's tags, kept apart from `school`. */
  tags?: string[];
  attack: 'none' | 'melee' | 'ranged';
  save?: Ability;
  dice?: string;
  origin: TraceOrigin;
  diagnostics: Diagnostic[];
}

/** One caster (D04); the primary one's attack, save DC and slots are sheet fields. */
export interface SpellcastingEntry {
  content: ContentReference;
  name: string;
  effectId: string;
  classLevel: number;
  ability: Ability;
  attackBonus: number;
  saveDc: number;
  preparation: 'prepared' | 'known';
  spellList: string;
  slotKind: 'spellSlots' | 'pactMagic';
  slots: number[];
  cantripsAllowed?: number;
  spellsAllowed?: number;
  primary: boolean;
  origin: TraceOrigin;
  spells: SpellEntry[];
  warnings: Diagnostic[];
  /** How the attack bonus and save DC are calculated; the primary's are the sheet fields' traces (M2.1). */
  attackTrace?: TraceEntry[];
  saveDcTrace?: TraceEntry[];
}

export interface CreateCharacterRequest {
  name: string;
  rulesFamily: RulesFamilyId;
  baseAbilities: AbilityScores;
  pins: ContentReference[];
  /** The starting class and any further levels from the builder draft (SPEC C-07). */
  classes?: ClassLevel[];
  /** Choices answered in the builder draft. */
  choices?: ChoiceSelection[];
  campaignId?: string;
  campaignExceptions?: CampaignException[];
  spells?: KnownSpell[];
}

export interface LicenseNotice {
  sourceId: string;
  title: string;
  publisher: string;
  license: string;
  redistributable: boolean;
  attribution?: string;
  /** CC-BY-4.0 §3: how the material was modified (SRD packs). */
  modificationNotice?: string;
}

/** ADR-007: a backup includes everything and is not for sharing; a share leaves out non-redistributable sources. */
export type ExportPurpose = 'backup' | 'share';

export interface OmittedRevision {
  reference: ContentReference;
  name: string;
  characters: string[];
}

export interface OmittedSource {
  sourceId: string;
  title: string;
  publisher: string;
  license: string;
  revisions: OmittedRevision[];
}

export interface PackageManifest {
  format: string;
  formatVersion: number;
  createdAt: string;
  appVersion: string;
  /** Absent before format v3 (always a backup). */
  purpose?: ExportPurpose;
  characters: string[];
  notices: LicenseNotice[];
  omitted?: OmittedSource[];
  attachmentPolicy: string;
  /** Absent for character packages; `source` is a source pack (format v7, M6 slice 1), `campaign` a campaign pack (v8, slice 2). */
  scope?: 'characters' | 'library' | 'source' | 'campaign';
  /** Source packs: the sender's statement, per source, that it is their own work. TomeStack cannot verify it. */
  attestations?: { sourceId: string; statement: string; confirmedAt: string }[];
}

// ---- Import Lab L5 (`package.preflight`): the one sharing rule, as a list before any share export is saved ----

export type PreflightKind = 'package' | 'sourcePack' | 'campaignPack' | 'sheet';

/** `selection`: character ids for `package` and `sheet`, source ids for `sourcePack`, the campaign id for `campaignPack`. */
export interface PreflightRequest {
  kind: PreflightKind;
  selection: string[];
}

/** A revision row names the content; a source-level row (a blocked source, a PDF) has an empty revision id (all zeros). */
export interface PreflightRow {
  contentId: string;
  revisionId: string;
  name: string;
  sourceName: string;
  redistributable: boolean;
  importDerived?: boolean | null;
  /** `bundled`, `local`, `received` or `unknown`. */
  origin: string;
  /** A `preflight.*` code; absent for an included row. The UI says it in words. */
  reason?: string | null;
}

export interface PreflightReport {
  included: PreflightRow[];
  omitted: PreflightRow[];
  blocking: PreflightRow[];
  notices: string[];
}

/** M6 slice 1 (`package.sourcePackPreview`): what a source pack would hold. Nothing is written. */
export interface SourcePackPreview {
  fileName: string;
  sources: LicenseNotice[];
  /** Published revisions included, superseded ones too. */
  revisions: number;
  /** Drafts of these sources that stay on this machine. */
  drafts: number;
  warnings: Diagnostic[];
  opensIn?: string | null;
  opensInCauses?: OpensInCause[];
  /** The older release the copy would be made for; null or absent for everything. */
  target?: string | null;
  changes?: ExportChange[];
  /** Sent back with the export so the service can refuse a copy that differs from the one previewed. */
  changesHash?: string;
}

// ---- extensions (M6 slice 3, ADR-011 option A: declarative only) ----

export type ExtensionPermission = 'read.sheet' | 'read.content' | 'import.file' | 'write.drafts' | 'export.file';

export interface ExtensionHook {
  kind: 'import' | 'export';
  id: string;
  label: string;
  transform: string;
  accepts?: 'json' | 'csv';
  produces?: 'text' | 'json';
  fileExtension?: string;
}

export interface ExtensionManifest {
  id: string;
  name: string;
  version: string;
  author: string;
  license: string;
  homepage?: string;
  description?: string;
  extensionApi: number;
  runtime: string;
  permissions: ExtensionPermission[];
  hooks: ExtensionHook[];
}

export interface InstalledExtension {
  id: string;
  sha256: string;
  manifest: ExtensionManifest;
  grants: ExtensionPermission[];
  enabled: boolean;
  installedAt: string;
  updatedAt: string;
}

export interface ExtensionInstallPreview {
  /** Set only when it can be installed. */
  token?: string;
  canInstall: boolean;
  manifest?: ExtensionManifest;
  sha256?: string;
  permissions: { permission: ExtensionPermission; description: string }[];
  update?: {
    installedVersion: string;
    newVersion: string;
    sameFile: boolean;
    permissionsAdded: string[];
    permissionsRemoved: string[];
    hooksAdded: string[];
    hooksRemoved: string[];
  };
  errors: Diagnostic[];
  warnings: Diagnostic[];
}

/** ADR-007 item 11: `share` keeps only totals for content that may not leave; `personal` also lets out your own homebrew. */
export type SheetPurpose = 'share' | 'personal';

export interface SheetDropped {
  source: string;
  publisher: string;
  items: number;
}

export interface SheetNotice {
  title: string;
  publisher: string;
  license: string;
  redistributable: boolean;
  attribution?: string;
  modificationNotice?: string;
  totalsOnly: boolean;
}

export interface ExtensionRunPreview {
  token: string;
  kind: 'import' | 'export';
  purpose: SheetPurpose;
  fileName?: string;
  bytes?: number;
  excerpt?: string;
  sourceTitle?: string;
  drafts: { name: string; kind: ContentKind; errors: Diagnostic[]; warnings: Diagnostic[] }[];
  dropped: SheetDropped[];
  notices: SheetNotice[];
  warnings: Diagnostic[];
}

/** M6 slice 4 (ADR-012): export targets. */
export type ExportTarget = 'foundry-dnd5e' | 'sheet-json';

export interface VttExportPreview {
  token: string;
  target: ExportTarget;
  fileName: string;
  bytes: number;
  adapterVersion: string;
  /** The target release the adapter was verified against. */
  targetVersion?: string;
  purpose: SheetPurpose;
  dropped: SheetDropped[];
  notices: SheetNotice[];
  /** Values Foundry calculates itself that may not match TomeStack's. */
  differences: string[];
  warnings: Diagnostic[];
}

export interface ExtensionRunRequest {
  extensionId: string;
  hookId: string;
  characterId?: string;
  sourceIds?: string[];
  purpose?: SheetPurpose;
  rulesFamily?: RulesFamilyId;
  sourceTitle?: string;
  /** From chooseExtensionInput (desktop). */
  inputToken?: string;
  /** Browser development: the file's bytes. */
  inputBase64?: string;
}

/** A source a campaign pack leaves out, and why (M6 slice 2). */
export interface LeftOutSource {
  sourceId: string;
  title: string;
  publisher: string;
  license: string;
  reason: Diagnostic;
}

/** M6 slice 2 (`package.campaignPackPreview`): what a campaign pack would hold. Nothing is written. */
export interface CampaignPackPreview {
  fileName: string;
  campaignId: string;
  name: string;
  rulesFamily: RulesFamilyId;
  /** Allowed sources the pack carries, with their published revisions. */
  included: LicenseNotice[];
  /** Bundled SRD sources: referenced by id, never copied. */
  referenced: LicenseNotice[];
  leftOut: LeftOutSource[];
  revisions: number;
  warnings: Diagnostic[];
  opensIn?: string | null;
  opensInCauses?: OpensInCause[];
  /** The older release the copy would be made for; null or absent for everything. */
  target?: string | null;
  changes?: ExportChange[];
  /** Sent back with the export so the service can refuse a copy that differs from the one previewed. */
  changesHash?: string;
}

export interface ExportPreview {
  purpose: ExportPurpose;
  fileName: string;
  characters: string[];
  included: LicenseNotice[];
  omitted: OmittedSource[];
  /** Gap notes the package would carry: all of the characters' notes in a backup, 0 in a share (M3 B3). */
  gapNotes: number;
  /** G6: `package.needs-newer-build` when the package would hold content or a character that TomeStack 0.6.x refuses, or content at schema 12 (a weapon's bonus), which names no release. */
  warnings?: Diagnostic[];
  /** The oldest TomeStack release that opens the file; null when it needs a newer one than this. Absent from a service without the feature. */
  opensIn?: string | null;
  opensInCauses?: OpensInCause[];
  /** The older release the copy would be made for; null or absent for everything. */
  target?: string | null;
  changes?: ExportChange[];
  /** Sent back with the export so the service can refuse a copy that differs from the one previewed. */
  changesHash?: string;
}

/** The releases a copy can be made for ("Save for an older TomeStack"): offered rows only, newest first; the current row is not in `targets`. */
export interface PackageTargets {
  current: string;
  currentRow: string;
  previousMinor: string | null;
  targets: { release: string; characterSchema: number; contentSchema: number }[];
}

/** Why a file needs a newer TomeStack than the previous minor: one character, content item or gap note. */
export interface OpensInCause {
  entry: string;
  kind: 'character' | 'content' | 'gapNote';
  id: string;
  name: string;
  schemaVersion?: number;
  bundledPack?: string;
  message: string;
}

/** One thing a copy for an older TomeStack drops, blocks on, copies under a new id, or leaves out. */
export interface ExportChange {
  entry: string;
  kind: 'character' | 'content';
  id: string;
  name: string;
  sourceTitle?: string;
  action: 'dropped' | 'blocks' | 'copied' | 'leftOut';
  code: string;
  message: string;
}

/** `import` (gap-note schema v2, D16b): an item a D&D Beyond sheet import could not match or place, named by its label. Only the importer writes it. */
export type GapTargetKind = 'feature' | 'field' | 'import';

export type GapNoteStatus = 'open' | 'resolved';

/** What a gap note is about. `label` is filled in by the service from the sheet. */
export interface GapTarget {
  kind: GapTargetKind;
  contentId?: string | null;
  effectId?: string | null;
  fieldId?: string | null;
  label?: string | null;
}

/** M3 B3: a local session feedback note. Never transmitted; it leaves the machine only in a personal backup. */
export interface GapNote {
  id: string;
  schemaVersion: number;
  characterId: string;
  target: GapTarget;
  text: string;
  status: GapNoteStatus;
  createdAt: string;
  updatedAt: string;
}

// ---- PDF import (M4; docs/features/pdf-import.md) ----

export type ImportJobStatus = 'queued' | 'running' | 'completed' | 'cancelled' | 'failed' | 'interrupted';

/** An import job: extraction of one source's PDF, then candidate detection. Local only; never exported. */
export interface ImportJob {
  id: string;
  sourceId: string;
  /** SHA-256 of the document the job read (the PDF, or the Markdown file's bytes); its pages are stored under it. */
  sha256: string;
  /** `markdown` for a Markdown import (import pages only: no PDF, not resumable); absent for a PDF job. */
  format?: 'markdown';
  firstPage: number;
  lastPage?: number;
  wholeDocument: boolean;
  /** The families the reviewer said the book is written for; absent: the source's families. */
  writtenFor?: RulesFamilyId[];
  status: ImportJobStatus;
  pageCount?: number;
  nextPage?: number;
  pagesDone: number;
  pagesFailed: number;
  pagesFromOcr: number;
  pagesWithoutText: number;
  /** A code such as `pdf.encrypted`; never text from the PDF. */
  failureCode?: string;
  failureMessage?: string;
  candidates: number;
  /** More candidates were found than the list keeps (5,000); absent otherwise. */
  truncatedCandidates?: boolean;
  /** Detection stopped for size before this page; absent otherwise. */
  detectionLimitedFromPage?: number;
  createdAt: string;
  updatedAt: string;
}

export interface ImportSearchHit {
  page: number;
  snippet: string;
}

/** One text line of a stored block, in PDF points. */
export interface StoredTextLine {
  text: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

/** One text block of a stored page, in PDF points. */
export interface StoredTextBlock {
  text: string;
  x: number;
  y: number;
  width: number;
  height: number;
  fontSize: number;
  bold: boolean;
  lines?: StoredTextLine[];
}

/** `import.page`: the stored extraction of one page (ADR-009). Page text is data: render it as text nodes only. */
export interface StoredPage {
  page: number;
  text: string;
  fromOcr: boolean;
  width: number;
  height: number;
  blocks: StoredTextBlock[];
  warnings: string[];
  error?: string;
}

/**
 * D64: where on the stored page text one proposed effect (or the name) was read. `blockStart`/`blockEnd` index the page's
 * stored blocks (`blockEnd` is on `pageEnd` when set, else on `page`); `lineStart`/`lineEnd` are source lines (Markdown
 * only; absent for a PDF). `patternId` is a stable id, never prose.
 */
export interface CandidateSpan {
  page: number;
  pageEnd?: number;
  blockStart: number;
  blockEnd: number;
  lineStart?: number;
  lineEnd?: number;
  patternId: string;
  confidence: number;
}

export interface DraftCandidate {
  id: string;
  sourceId: string;
  page: PageRef;
  excerpt: string;
  proposedKind: ContentKind;
  proposedName: string;
  rulesFamilies: RulesFamilyId[];
  proposedEffects: (Effect | Record<string, unknown>)[];
  /** A UI hint, never permission (ADR-004). */
  confidence: number;
  uncertainties: string[];
  unresolvedReferences: string[];
  fields: Record<string, string>;
  lowConfidenceFields: string[];
  summary?: string;
  /** D64: by proposed effect id. Absent on a candidate stored before spans existed (review then shows the whole excerpt). */
  effectSpans?: Record<string, CandidateSpan>;
  /** D64: where the name was read; absent when unknown. */
  nameSpan?: CandidateSpan;
}

export type CandidateStatus = 'pending' | 'accepted' | 'acceptedAsReference' | 'ignored';

export interface StoredCandidate {
  id: string;
  jobId: string;
  candidate: DraftCandidate;
  /** The reviewer's version, when edited. */
  edited?: DraftCandidate;
  status: CandidateStatus;
  draft?: ContentReference;
  /** Item 11 (D34): on a class, the feature candidates of the same import that it grants, with the class level each is granted at. */
  grantsCandidates?: { candidateId: string; level: number }[];
  /** R29: the reviewer saved a rules family for this candidate; absent (false) until then. */
  familiesReviewed?: boolean;
  /** D64: SHA-256 of the job's attachment, so a span resolves to the stored page text after the attachment is replaced; absent before spans existed. */
  attachmentSha256?: string;
  /** R120: the compare-and-swap token of the stored row; sent back as `CandidateEdit.expected`. */
  etag?: string;
  updatedAt: string;
}

export interface CandidateDependency {
  kind: 'source' | 'content' | 'missing-content' | 'unresolved-name';
  name: string;
  reference?: ContentReference;
}

export interface CandidateCheck {
  candidateId: string;
  report: ValidationReport;
  dependencies: CandidateDependency[];
  blockers: Diagnostic[];
  canAccept: boolean;
  canAcceptAsReference: boolean;
  /** Task 5 (R66): what accepting will leave out, said rather than blocked. */
  notes?: string[];
  /** Task 5: on a class, every feature its table links, by level then name. */
  linked?: CandidateLinkSummary[];
}

/** One feature a class links: a candidate of this import (with its status), or one installed feature (R67). */
export interface CandidateLinkSummary {
  candidateId?: string;
  name: string;
  level: number;
  status?: CandidateStatus;
  installed: boolean;
}

export interface CandidateFilter {
  /** Only candidates that reach this page or later. */
  firstPage?: number;
  /** Only candidates that start on or before this page. */
  lastPage?: number;
  kind?: ContentKind;
  minConfidence?: number;
  maxConfidence?: number;
  status?: CandidateStatus;
}

/** One page of a job's filtered candidates; `total` counts the whole filtered set. */
export interface CandidatePage {
  items: StoredCandidate[];
  total: number;
}

export interface CandidateEdit {
  name?: string;
  kind?: ContentKind;
  rulesFamilies?: RulesFamilyId[];
  summary?: string;
  effects?: unknown[];
  dismissReferences?: string[];
  /** R120: the candidate's `etag` as the editor last saw it; the edit is refused when the stored row has changed since. */
  expected?: string;
}

/** `gap.listAll` (M3 C5): a note with its character's name. */
export interface GapNoteListing {
  note: GapNote;
  characterName: string;
}

export interface ExportedPackage {
  fileName: string;
  base64: string;
  manifest: PackageManifest;
}

export interface SaveOutcome {
  saved: boolean;
  fileName?: string;
}

export type PackageItemAction = 'add' | 'unchanged' | 'replace' | 'conflict';

/** Values are compact JSON of the field on each side. */
export interface FieldChange {
  field: string;
  local?: string;
  imported?: string;
}

export interface PackageItem {
  kind: 'source' | 'contentRevision' | 'character' | 'campaign' | 'gapNote' | 'attachment';
  id: string;
  name: string;
  action: PackageItemAction;
  detail?: string;
  /**
   * Set on a source that differs from the local record, and (M6 slice 2) on a campaign pack's campaign that differs
   * from yours; apply needs a SourceChoice for it.
   */
  changes?: FieldChange[];
}

export type SourceChoice = 'keepLocal' | 'useImported';

/** M6 slice 2: a character whose content "use the imported campaign" would make not allowed. */
export interface CampaignImpact {
  campaignId: string;
  characterId: string;
  characterName: string;
  notAllowed: string[];
  /** The imported campaign's rules family, when it differs from the character's. */
  rulesFamily?: RulesFamilyId;
}

export interface PackagePreview {
  canApply: boolean;
  manifest?: PackageManifest;
  items: PackageItem[];
  errors: Diagnostic[];
  warnings: Diagnostic[];
  campaignImpact?: CampaignImpact[];
}

// ---- homebrew studio (M2 item 5) ----

export type EffectTiming = 'always' | 'whileActive' | 'onRoll' | 'onShortRest' | 'onLongRest';

interface EffectBase {
  id: string;
  automation?: AutomationStatus;
  timing?: EffectTiming;
  text?: string;
}

/** Content v5 (D04): only for the starting class, or only for a class taken later. */
export type ClassEntry = 'startingClass' | 'multiclass';

export type SpellcastingAbility = 'str' | 'dex' | 'con' | 'int' | 'wis' | 'cha';

/** Content v12 (MECH-X item 1): the payment an authored roll, attack or toggle makes. Absent means the pool cost (never written as `resource`). */
export interface EffectPayment {
  kind: PaymentKind;
  mayDropToZero?: boolean;
  minSlotLevel?: number;
}

export type Effect =
  | (EffectBase & {
      type: 'modifier';
      operation: 'bonus' | 'set' | 'replace';
      target: string;
      value: string;
      toggle?: string;
      /** Content v11 (D43): the content that declares `toggle`. Absent: this revision. */
      toggleContent?: string;
    })
  // Content v6 (M3 B2): switched on and off in play; turning it on can spend one use of a resource of this revision.
  // Content v10 (Wave 3, D42): autoWhen and activation on a toggle, activation on a resource, three damage-defence grant
  // kinds with a damageType, and the item effect. All optional; a revision below v10 never carries them.
  | (EffectBase & {
      type: 'toggle';
      toggleId: string;
      label: string;
      resourceId?: string;
      autoWhen?: 'bloodied';
      activation?: Activation;
      // Content v11 (D43): the form group (id, and the content that declares it; absent: this revision), layer, the primaries an
      // overlay may be on with (absent contentId: this revision), a cost formula, the pool's content and the level it opens at.
      group?: string;
      groupContent?: string;
      layer?: 'primary' | 'overlay';
      allowedWith?: { contentId?: string; toggleId: string }[];
      allowedInBase?: boolean;
      cost?: string;
      resourceContent?: string;
      level?: number;
      /** Content v11 (D43): the rounds it lasts once on, a formula; absent: until switched off. */
      duration?: string;
      /** Content v12 (MECH-X item 1): pay from hit points or a spell slot instead of a pool. */
      payment?: EffectPayment;
    })
  | (EffectBase & { type: 'toggleGroup'; groupId: string; label: string })
  // Content v11 (D43): a pool change at the start or end of the turn, while a toggle is on (absent: always). `amount` is a formula, or `all` for a regain.
  | (EffectBase & {
      type: 'upkeep';
      toggle?: string;
      toggleContent?: string;
      at: 'turnStart' | 'turnEnd';
      resourceId: string;
      resourceContent?: string;
      change: 'spend' | 'regain';
      amount: string;
      ifShort?: 'ask' | 'end';
    })
  | (EffectBase & {
      type: 'grant';
      grant: 'proficiency' | 'expertise' | 'content' | 'damageResistance' | 'damageImmunity' | 'damageVulnerability';
      target?: string;
      content?: ContentReference;
      level?: number;
      onlyAs?: ClassEntry;
      damageType?: string;
      /** Content v11 (D43, slice 6): a defence that applies only while this toggle is on (absent contentId: this revision). */
      whileToggle?: { contentId?: string; toggleId: string };
    })
  // Content v11 (D43, slice 6): automatic advantage or disadvantage on a save, skill check or ability check.
  | (EffectBase & {
      type: 'rollMode';
      target: string;
      mode: 'advantage' | 'disadvantage';
      against?: string[];
      toggle?: string;
      toggleContent?: string;
    })
  // Content v11 (D43 S7): a damage-time response. `amount` belongs to a damageReduction or hitPointOutcome response, `when` to a threshold one.
  | (EffectBase & {
      type: 'response';
      responseId: string;
      label: string;
      phase: ResponsePhase;
      amount?: string;
      when?: { atOrBelow?: string; survived?: true };
      resourceId?: string;
      resourceContent?: string;
      cost?: string;
      toggle?: { contentId?: string; toggleId: string };
      outcomes?: ResponseOutcomeDef[];
    })
  | (EffectBase & {
      type: 'resource';
      resourceId: string;
      label: string;
      maximum: string;
      activation?: Activation;
      // Content v11 (D43): counts up from zero ("built up"), what a full pool says (only with counts), and a per-turn allowance.
      counts?: 'up';
      resourceFull?: { text: string };
      allowance?: boolean;
    })
  | (EffectBase & { type: 'item'; requiresAttunement?: boolean; charges?: { max: number; recovery?: { period: RestPeriod; regain?: string } } })
  | (EffectBase & { type: 'itemMod'; appliesTo: 'weapon' | 'armor' | 'any'; bonus?: number; modifiers?: Extract<Effect, { type: 'modifier' }>[] })
  | (EffectBase & { type: 'companion'; companionId: string; label: string; armorClass: string; hitPointMaximum: string; speed?: string; attacks?: string[]; resourceIds?: string[] })
  | (EffectBase & { type: 'recovery'; resourceId: string; on: RestPeriod; amount: string })
  | (EffectBase & {
      type: 'roll';
      rollId: string;
      label: string;
      /** Content v11: a roll may have no dice (an action that only spends a cost); every earlier version requires them. */
      dice?: string;
      resourceId?: string;
      /** Content v5: where the action is listed; absent: "Other". */
      activation?: Activation;
      /** Content v6: the content that defines the pool (absent: this revision), the uses spent (a formula) and whether the player picks how many. */
      resourceContent?: string;
      cost?: string;
      variableCost?: boolean;
      /** Content v8: a formula added to the dice. */
      bonus?: string;
      /** Content v11 (D43): the roll is available only while this toggle is on (absent contentId: this revision). */
      whileToggle?: { contentId?: string; toggleId: string };
      /** Content v11 (D43, slice 5): the saving throw the action asks of its targets. */
      save?: { ability: Ability; dc: string };
      /** Content v11: the area, in feet; width is for a line only. */
      area?: { shape: 'line' | 'cone' | 'sphere' | 'cube' | 'cylinder' | 'emanation'; size: number; width?: number };
      range?: string;
      targets?: string;
      /** Content v11: extra dice for each unit spent beyond the base cost. */
      perUnit?: { dice: string; maxUnits?: number };
      /** Content v11 (D43 S8): other pool changes made in the same use, at most 4; `amount` is a formula. */
      alsoChange?: { resourceId: string; resourceContent?: string; change: 'spend' | 'regain'; amount: string }[];
      /** Content v11 (D43 S8): the roll draws down the built-up charge of its pool (a pool that counts up). */
      drawsDown?: boolean;
      /** Content v12 (MECH-X item 1): pay from hit points or a spell slot instead of a pool. */
      payment?: EffectPayment;
      /** Content v12 (R38): the content id of a spell this roll casts without a slot. */
      castsSpell?: string;
    })
  // Content v11 (D43, slice 5): a named formula, read as VALUE.<valueId>.
  | (EffectBase & { type: 'value'; valueId: string; formula: string })
  // Content v11 (D43): an attack a feature gives, and a rider that adds to the attacks carrying one of its tags.
  | (EffectBase & {
      type: 'attack';
      attackId: string;
      label: string;
      kind: 'melee' | 'ranged';
      abilities: Ability[];
      proficient?: boolean;
      damage: { count: string; die: string };
      damageBonus?: string;
      damageType: string;
      range?: string;
      tags?: string[];
      replacesAttack?: boolean;
      activation?: Activation;
      resourceId?: string;
      resourceContent?: string;
      cost?: string;
      whileToggle?: { contentId?: string; toggleId: string };
      /** Content v12 (MECH-X item 1): pay from hit points or a spell slot instead of a pool. */
      payment?: EffectPayment;
    })
  | (EffectBase & {
      type: 'attackRider';
      tags: string[];
      toHit?: string;
      dice?: string;
      damageBonus?: string;
      damageType?: string;
      toggle?: string;
      toggleContent?: string;
      stackGroup?: string;
    })
  // An item's weapon. Content v12 (MECH-X item 0) adds bonus (0 to 10), added to to-hit and damage.
  | (EffectBase & {
      type: 'weapon';
      category: 'simple' | 'martial';
      attack: 'melee' | 'ranged';
      damage: string;
      damageType: string;
      properties?: string[];
      versatile?: string;
      range?: string;
      weaponKey: string;
      mastery?: string;
      bonus?: number;
    })
  | (EffectBase & { type: 'armor'; category: 'light' | 'medium' | 'heavy' | 'shield'; armorClass: number; dexterityCap?: number })
  | (EffectBase & { type: 'choice'; choiceId: string; count: number; options: ContentReference[]; level?: number; onlyAs?: ClassEntry; replaceable?: { when: 'levelUp' | 'longRest'; max?: number } })
  // M5 slice 1b: the class editor (content v3/v5 effects, and v9 scale and multiclassCasterTable; ADR-010).
  | (EffectBase & { type: 'hitDie'; die: number })
  | (EffectBase & { type: 'restriction'; field: string; minimum: number; multiclass?: boolean; group?: string })
  | (EffectBase & { type: 'scale'; scaleId: string; label: string; values: number[] })
  | (EffectBase & {
      type: 'spellcasting';
      ability: SpellcastingAbility;
      preparation?: 'prepared' | 'known';
      spellList: string;
      slotKind?: 'spellSlots' | 'pactMagic';
      slots: number[][];
      cantrips?: number[];
      spellsTable?: number[];
      spellsFormula?: string;
      multiclassCaster?: 'full' | 'half' | 'third';
      multiclassCasterTable?: number[];
    })
  // Content v5: a spell's own data (level 0 is a cantrip). Item 12: the studio's spell editor writes it.
  | (EffectBase & {
      type: 'spell';
      level: number;
      school?: string;
      castingTime?: string;
      range?: string;
      components?: string;
      duration?: string;
      concentration?: boolean;
      ritual?: boolean;
      lists?: string[];
      attack?: 'none' | 'melee' | 'ranged';
      save?: SpellcastingAbility;
      dice?: string;
      /** Content v12 (R39): tags kept apart from the school; `all` is refused. */
      tags?: string[];
    });

export interface ChoiceExtension {
  contentId: string;
  choiceId: string;
}

export interface ContentRevision {
  contentId: string;
  revisionId: string;
  schemaVersion?: number;
  kind: ContentKind;
  name: string;
  rulesFamilies: RulesFamilyId[];
  provenance: { sourceId: string; page?: PageRef };
  status: 'draft' | 'published';
  summary?: string;
  extendsChoice?: ChoiceExtension;
  effects: Effect[];
  [extension: string]: unknown;
}

export interface SourceRecord {
  id: string;
  title: string;
  publisher: string;
  rulesFamilies: RulesFamilyId[];
  editionVersion: string;
  license: string;
  redistributable: boolean;
  /** M6 slice 1: material from a PDF or a Markdown file entered this source; it is never shared, and this never goes away. */
  importDerived?: boolean;
  /** M6 slice 1: `received` sources came in someone else's package and cannot be marked as your own work. */
  origin?: 'local' | 'received';
  /** M6 slice 1: when you confirmed the source is your own work ("Mark as shareable"). */
  shareConfirmedAt?: string;
}

/** ADR-005: a source's PDF, without any path. */
export interface AttachmentInfo {
  sourceId: string;
  attachmentId: string;
  originalFileName: string;
  byteLength: number;
  mode: 'managed' | 'linked';
  status: 'available' | 'missing' | 'changed';
}

export interface DetachPreview {
  sourceId: string;
  originalFileName: string;
  pageLinks: number;
  contentNames: string[];
}

export interface OpenPageOutcome {
  opened: boolean;
  page: number;
  warnings: Diagnostic[];
}

export interface StudioEntry {
  contentId: string;
  name: string;
  kind: ContentKind;
  revisions: ContentRevision[];
  latest: ContentRevision;
  latestPublished?: ContentRevision;
}

export type ReferenceRole = 'pin' | 'class' | 'choice' | 'grant' | 'equipment' | 'spell';

export interface AffectedCharacter {
  characterId: string;
  name: string;
  pinned: ContentReference;
  role: ReferenceRole;
  via?: string;
}

/** `character.updates` (M3 C7): a newer published revision of content the character uses; an offer, never applied by itself. */
export interface UpdateOffer {
  from: ContentReference;
  to: ContentReference;
  name: string;
  role: ReferenceRole;
  sourceTitle: string;
  /** The source ships with TomeStack (an SRD pack) rather than being made in TomeStack. */
  bundled: boolean;
}

export interface PublishResult {
  draft: ContentReference;
  published: ContentReference;
  report: ValidationReport;
  affected: AffectedCharacter[];
}

/** Item 9: grants and choice options of one content that move to the revisions published in the same batch. */
export interface RePointing {
  dependent: ContentReference;
  dependentName: string;
  grantsMoved: number;
  optionsMoved: number;
}

/** One content of a bulk publish: its pending draft (or, for a dependent, its newest revision), validation and findings. */
export interface PublishManyEntry {
  draft: ContentReference;
  name: string;
  kind: ContentKind;
  report: ValidationReport;
  findings: DebugFinding[];
  replacesPublished: boolean;
  rePointings: RePointing[];
}

export interface PublishManyPreview {
  entries: PublishManyEntry[];
  dependents: PublishManyEntry[];
  canPublish: boolean;
}

export interface PublishManyResult {
  published: PublishResult[];
}

/** Item 9: a candidate bulk accept skipped, with the diagnostic codes (never text from the document). */
export interface BlockedCandidate {
  candidateId: string;
  codes: string[];
}

export interface AcceptManyResult {
  accepted: StoredCandidate[];
  blocked: BlockedCandidate[];
}

/** Item 10: what `import.candidate.addToSpellList` did; the skipped carry codes only. */
export interface CandidateSpellListResult {
  listKey: string;
  updated: StoredCandidate[];
  skipped: BlockedCandidate[];
  /** Updated candidates that need a word: `spell-list.other-lists-unreviewed` means other proposed lists are still to check (R59). */
  notes: BlockedCandidate[];
}

export interface SkippedContent {
  contentId: string;
  codes: string[];
}

/** Item 10: `content.addToSpellList` saves drafts only (never publishes); `note` says characters keep their recorded revisions. */
export interface ContentSpellListResult {
  listKey: string;
  drafts: ContentReference[];
  skipped: SkippedContent[];
  note: string;
}

/** A spell list key that exists in published content, with how many spells are on it. */
export interface SpellListKeyCount {
  key: string;
  spells: number;
}

export interface EffectChange {
  effectId: string;
  change: 'added' | 'removed' | 'changed';
  type?: string;
  before?: string;
  after?: string;
}

export interface UpdateReview {
  characterId: string;
  from: ContentReference;
  to: ContentReference;
  mechanics: { properties: { property: string; before?: string; after?: string }[]; effects: EffectChange[] };
  fields: { field: string; label: string; before: number; after: number }[];
  newDiagnostics: Diagnostic[];
  resolvedDiagnostics: Diagnostic[];
  unresolvedChoices: ChoiceStatus[];
  affectedOverrides: FieldOverride[];
}

export interface FieldInfo {
  id: string;
  label: string;
}

export interface ValidationReport {
  revision: ContentReference;
  errors: Diagnostic[];
  warnings: Diagnostic[];
  canPublish: boolean;
}

/** M5 slice 2 (B02): how much a debugger finding matters; errors block publishing. */
export type FindingSeverity = 'error' | 'warning' | 'note';

export interface DebugFinding {
  code: string;
  severity: FindingSeverity;
  message: string;
  content: ContentReference;
  contentName: string;
  effectId?: string;
}

/** `content.diagnose`: what the homebrew debugger found. It writes nothing. */
export interface DebugReport {
  scope: ContentReference[];
  findings: DebugFinding[];
  errors: number;
  warnings: number;
  /** The graph walk hit its bound, so some reach findings may be missing. */
  truncated: boolean;
}

/** M5 slice 8 (B08): a snapshot of a character, as the list shows it. Snapshots are local: never in a package or backup. */
export interface SnapshotSummary {
  id: string;
  characterId: string;
  createdAt: string;
  reason: 'manual' | 'beforeRestore';
  label?: string;
  name: string;
  level: number;
}

/** One page of a character's snapshots, newest first. Older ones: ask again with `before` = the last item's id. */
export interface SnapshotPage {
  items: SnapshotSummary[];
  hasMore: boolean;
}

/** `character.restorePreview`: what restoring would change. The token is good for one restore of this exact state. */
export interface RestorePreview {
  token: string;
  snapshot: SnapshotSummary;
  fields: FieldDelta[];
  newDiagnostics: Diagnostic[];
  added: ContentReference[];
  removed: ContentReference[];
  playChanges: boolean;
  /** The name the restore brings back, when it differs. */
  nameAfter?: string;
  exceptionsChange?: boolean;
  /** New campaign warnings: the campaign stays as it is, so the snapshot's content may not be allowed there. */
  campaignWarnings?: Diagnostic[];
  /** Set only when the restore changes the coins (they roll back with the snapshot): the coins now, and the coins it brings back. */
  currencyNow?: Currency;
  currencyAfter?: Currency;
  /** Names of the detail fields (such as "alignment") the restore changes; never their text. Set only when some differ (D36). */
  detailsChange?: string[];
  /** Whether the restore changes the custom items, the custom attacks, or the experience points, damage defences and companions (R128). Flags only, never names. */
  customItemsChange?: boolean;
  customAttacksChange?: boolean;
  otherSheetValuesChange?: boolean;
}

export interface RestoreResult {
  view: CharacterView;
  undo: SnapshotSummary;
}

/** M5 slice 7: one design hint. An opinion only: it never blocks, never changes a calculation and is never stored. */
export interface DesignHint {
  code: string;
  message: string;
  effectId?: string;
  family?: RulesFamilyId;
  level?: number;
}

/** M5 slice 5 (B19): one node of a content's relationship tree. */
export interface TreeNode {
  id: string;
  kind: 'content' | 'level' | 'choice' | 'resource' | 'roll' | 'recovery' | 'toggle' | 'scale' | 'missing';
  label: string;
  content?: ContentReference;
  /** The effect the node stands for; it belongs to `owner` (for a granted content, the content that grants it). */
  effectId?: string;
  owner?: ContentReference;
  children: TreeNode[];
  note?: string;
}

export interface ContentTreeView {
  root: TreeNode;
  truncated: boolean;
}

/** M5 slice 4 (B07): one text of a revision pair, line by line. `whole`: too long to align (all old lines, then all new). */
export interface TextChange {
  where: string;
  lines: { kind: 'same' | 'added' | 'removed'; text: string }[];
  whole: boolean;
  /** Lines a whole text left out to stay under the caps (0: nothing cut). */
  notShown: number;
}

/** One character with each revision (M5 slice 4, B04): what changes, or why it could not run. */
export interface CompareRun {
  name: string;
  characterId?: string;
  fields: FieldDelta[];
  newDiagnostics: Diagnostic[];
  resolvedDiagnostics: Diagnostic[];
  unresolvedChoices: ChoiceStatus[];
  problems: Diagnostic[];
}

/** `content.compare`: two revisions by mechanics and text, run on unsaved copies. Nothing is written. */
export interface ContentComparison {
  mechanics: UpdateReview['mechanics'];
  text: TextChange[];
  runs: CompareRun[];
}

export interface CompareRequest {
  from: ContentReference;
  to?: ContentReference;
  toRevision?: ContentRevision;
  characterIds?: string[];
  blank?: { rulesFamily?: RulesFamilyId; level?: number };
}

/** A displayed value that would change. */
export interface FieldDelta {
  field: string;
  label: string;
  before: number;
  after: number;
}

/** `content.sandbox` (M5 slice 3): one draft tried on an unsaved copy or a blank character. Nothing is saved. */
export interface SandboxRequest {
  revision?: ContentRevision;
  reference?: ContentReference;
  characterId?: string;
  rulesFamily?: RulesFamilyId;
  level?: number;
}

export interface SandboxView {
  /** The unsaved copy and its sheet, with the draft counted as published. Never stored. */
  view: CharacterView;
  draft: ContentReference;
  /** For a copy of a saved character: every calculated sheet field the draft changes (not resource maximums or class columns). */
  changes: FieldDelta[];
  validation: ValidationReport;
}

export interface ImportResult {
  added: number;
  replaced: number;
  unchanged: number;
  characters: string[];
  /** Relative to the data folder; set when a local character was replaced. Import it to restore. */
  backupFile?: string;
  /** M6 slice 2: a database copy taken because a campaign was replaced, beside a character backup in backupFile. */
  databaseCopy?: string;
}

/** `diagnostics.copy`: plain text for a bug report; paths, the user name and source titles are already replaced. */
export interface DiagnosticsText {
  text: string;
}

// ---- full library backup (M2.1) ----

/** What "Back up everything" would write; nothing is written. */
export interface LibraryBackupPreview {
  fileName: string;
  characters: number;
  campaigns: number;
  gapNotes: number;
  sources: number;
  publishedRevisions: number;
  draftRevisions: number;
  managedPdfs: number;
  managedPdfBytes: number;
  linkedPdfs: number;
  /** Sources whose PDF copy is missing or damaged, and would be left out. */
  unreadable: string[];
  opensIn?: string | null;
  opensInCauses?: OpensInCause[];
  /** The older release the copy would be made for; null or absent for everything. */
  target?: string | null;
  changes?: ExportChange[];
  /** Sent back with the save so the service can refuse a copy that differs from the one previewed. */
  changesHash?: string;
}

export type LibraryBackupOutcome =
  | { saved: false }
  | { saved: true; fileName: string; bytes: number; contents: LibraryBackupPreview; warnings: Diagnostic[] };

export type LibraryRestoreChoice =
  | { chosen: false }
  | { chosen: true; token: string; fileName: string; preview: PackagePreview };

/** D77: the daily copies setting and the copies kept in `backups/daily` of the data folder, newest first (file names only). */
export interface DailyCopies {
  on: boolean;
  copies: string[];
  /** Their total size: each copy holds every PDF copy. */
  bytes: number;
}

export interface LibraryRestoreResult {
  added: number;
  replaced: number;
  unchanged: number;
  pdfsCopied: number;
  /** Relative to the data folder: the database as it was before the restore, when anything was replaced. */
  safetyCopy?: string;
  warnings: Diagnostic[];
}

// ---- character import from a character sheet PDF: a D&D Beyond export or the fillable 2014 sheet (features/ddb-pdf-import.md) ----

export interface DdbSummary {
  /** The character's name as read; shown, never logged. */
  name: string;
  /** "Name level (subclass)" joined with " / ", or empty when the classes could not be read. */
  classText: string;
  features: number;
  spells: number;
  items: number;
  /** Item 19a: the lines of a text import that were not understood; 0 or absent for a PDF. */
  notRead?: number;
}

/** `ddb.read`, `ddb.readData` and `ddb.readText`: the sheet is held in memory under the one-use token. `chosen` is false when the dialog was cancelled. */
export interface DdbReadResult {
  chosen?: boolean;
  token: string;
  layout: string;
  /** The layout in words ("D&D Beyond 2014", "the fillable 2014 sheet"); the id in `layout` is the fallback. */
  layoutName?: string;
  suggestedFamily?: RulesFamilyId;
  summary: DdbSummary;
}

export type MatchKind = 'species' | 'background' | 'class' | 'subclass' | 'feat' | 'skill' | 'spell' | 'item' | 'feature';
export type MatchStatus = 'matched' | 'choose' | 'notFound' | 'noPlace' | 'unreadable' | 'leftOut';
export type PlacementKind = 'pin' | 'choice' | 'class' | 'spell' | 'equipment' | 'none' | 'nowhere';

export interface Placement {
  kind: PlacementKind;
  choiceSource?: ContentReference;
  choiceId?: string;
  /** For a spell: the content id of the class or subclass that casts it. */
  caster?: string;
}

export interface MatchCandidate {
  reference: ContentReference;
  name: string;
  sourceTitle: string;
  families: RulesFamilyId[];
  /** With a campaign: whether it allows the option's source. */
  allowedInCampaign?: boolean;
  placement: Placement;
}

export interface MatchRow {
  /** Stable per sheet item: `species`, `class:0`, `class:0:subclass`, `skill:athletics`, `spell:3`, … */
  rowId: string;
  kind: MatchKind;
  /** The sheet's text. */
  label: string;
  status: MatchStatus;
  candidates: MatchCandidate[];
  chosen?: MatchCandidate;
  /** A code: why a row has no place, or a warning on a match. */
  note?: string;
}

export interface Resolution {
  rowId: string;
  chosen?: ContentReference;
  leaveOut: boolean;
  caster?: string;
}

export type NumberAction = 'useTomeStack' | 'keepSheet' | 'note';

export interface NumberChoice {
  field: string;
  action: NumberAction;
}

export interface NumberRow {
  field: string;
  label: string;
  sheet?: number;
  calculated: number;
  differs: boolean;
}

export interface AbilityNote {
  ability: Ability;
  code: string;
  message: string;
}

export interface AbilityPlan {
  proposedBase: AbilityScores;
  notes: AbilityNote[];
}

export interface DdbReport {
  matched: number;
  chosen: number;
  notFound: number;
  noPlace: number;
  unreadable: number;
  leftOut: number;
  /** Codes of what the import leaves out by design (`currency`, `speed`, `playState`, …). */
  notBroughtOver: string[];
  sameNameExists: boolean;
  familyMismatch: boolean;
  /** The layout in words, as in `DdbReadResult.layoutName`. */
  layoutName?: string;
  /** Lines from reading and importing the sheet's free text (a field cut short, a weapon row left empty): labels and counts only, never the sheet's text. */
  lines?: string[];
}

export interface DdbPreviewRequest {
  token: string;
  rulesFamily: RulesFamilyId;
  campaignId?: string;
  resolutions?: Resolution[];
  numberChoices?: NumberChoice[];
  includePlayState: boolean;
  /** Open choices answered in the matches step (a 2024 background's ability scores). */
  answers?: ChoiceSelection[];
  /** D16g: create matched weapons and armour equipped. */
  equipMatched?: boolean;
  /** D35: the row id of the one class the sheet names that TomeStack lacks, to be created as a minimal homebrew class. */
  createClassRow?: string;
  /** Item 19a: keep a text import's lines that were not read as session notes (the service defaults to true). Part of the request, so a change re-previews. */
  keepNotRead?: boolean;
  /** R129: the created class will be published with the character. The preview counts the class only then, so a toggle re-previews. */
  publishClass?: boolean;
}

/** Item 19a (D37): a line of the pasted or file text that was not understood. The user's own text: shown as plain text only. */
export interface NotReadLine {
  /** 1-based. */
  line: number;
  text: string;
}

/** D35: a class the sheet names that is not installed, and whether a stub can be created for it. `note` is null when it can. */
export interface StubOffer {
  rowId: string;
  name: string;
  hitDie?: number | null;
  note?: 'stub.hit-die-missing' | 'stub.hit-die-unreadable' | 'stub.one-per-import' | null;
}

/** D35: the stub class in full, as reviewed before it is created. `saves` are ability keys (`str` to `cha`). */
export interface StubClassReview {
  rowId: string;
  name: string;
  rulesFamily: RulesFamilyId;
  hitDie: number;
  saves: string[];
  sourceTitle: string;
}

/** Item 16 (D16(c)): one reference draft the import would write (name and description only). `firstLine` is the description's first line. */
export interface ReferenceDraftInfo {
  rowId: string;
  kind: 'feat' | 'feature';
  name: string;
  firstLine: string;
  length: number;
}

/** `ddb.preview`: the proposed character and everything the steps show. Nothing is stored. */
export interface DdbPreview {
  character: Character;
  matches: MatchRow[];
  openChoices: ChoiceStatus[];
  comparison: NumberRow[];
  abilityPlan: AbilityPlan;
  report: DdbReport;
  diagnostics: Diagnostic[];
  canApply: boolean;
  /** One offer per unmatched class row (D35). */
  stubOffers?: StubOffer[] | null;
  /** Set when the previewed request asked for a class to be created. */
  stub?: StubClassReview | null;
  /** The reference drafts for unmatched feats and features that carry text (item 16); absent when there are none. */
  referenceDrafts?: ReferenceDraftInfo[] | null;
  /** `campaign.source-not-allowed` when a published feat would be pinned from a source the chosen campaign does not allow. */
  referenceNote?: 'campaign.source-not-allowed' | null;
  /** Item 19a: the text lines that were not read, in order; absent for a PDF or when every line was understood. */
  notRead?: NotReadLine[] | null;
}

export interface DdbApplyResult {
  characterId: string;
  overrides: number;
  gapNotes: number;
  gapNotesNotStored: number;
  report: DdbReport;
  /** The created class's content id, when one was created. */
  createdClass?: string | null;
  /** The content ids of the reference drafts written, when any were. */
  referenceDrafts?: string[] | null;
}

/** One sample class level of a formula preview (D61): a value, or the problem at that level. The server omits null fields. */
export interface FormulaPreviewPoint {
  level: number;
  value?: number | null;
  problem?: string | null;
}

/** The value of a formula at the sample levels (D61, `content.previewFormula`). Read-only; writes nothing. */
export interface FormulaPreviewResult {
  formula: string;
  error?: { code: string; message: string } | null;
  unavailable?: string | null;
  points: FormulaPreviewPoint[];
}

/** Import Lab (D62 L1): effects by how far TomeStack really goes with them. `unsupported` means the type is not known. */
export interface CoverageCounts {
  automatic: number;
  assisted: number;
  reference: number;
  unsupported: number;
}

/** One effect's author value, the engine's ceiling and the effective automation. The server omits null fields. */
export interface EffectCoverage {
  /** Absent for the text-only line of a feature that has no effects. */
  effectId?: string | null;
  type: string;
  author: AutomationStatus;
  ceiling?: AutomationStatus | null;
  effective?: AutomationStatus | null;
  reason?: string | null;
  evidence?: string | null;
  unsupported: boolean;
}

export interface FeatureCoverage {
  contentId: string;
  revisionId: string;
  name: string;
  kind: ContentKind;
  status: 'published' | 'draft';
  totals: CoverageCounts;
  /** The feature may need a mechanic: advice, never a gate. */
  missingMechanic: boolean;
  effects: EffectCoverage[];
}

export interface LevelCoverage {
  level: number;
  features: FeatureCoverage[];
}

export interface ClassCoverage {
  contentId: string;
  name: string;
  totals: CoverageCounts;
  levels: LevelCoverage[];
}

export interface SourceCoverage {
  sourceId: string;
  name: string;
  totals: CoverageCounts;
  classes: ClassCoverage[];
  other: FeatureCoverage[];
}

/** `content.coverage`: read-only; needs no character. */
export interface CoverageReport {
  scope: { sourceId?: string | null; contentId?: string | null; includeDrafts: boolean };
  totals: CoverageCounts;
  sources: SourceCoverage[];
}

export interface CoverageRequest {
  sourceId?: string;
  contentId?: string;
  includeDrafts?: boolean;
}

// ---- Import Lab L6 (`content.duplicates`): entries that look alike, listed and never merged ----

export type DuplicateClass = 'exact' | 'sameNameDifferentRules' | 'sameRulesDifferentName';

/** `jobId`: that job's pending candidates against the library. `sourceId`: only groups that source is in (with a job: only that source's library content). */
export interface DuplicatesRequest {
  sourceId?: string;
  jobId?: string;
}

/** A published library entry (`contentId`, `revisionId`) or a pending candidate (`candidateId`). */
export interface DuplicateMember {
  candidateId?: string | null;
  contentId?: string | null;
  revisionId?: string | null;
  kind: ContentKind;
  name: string;
  sourceId: string;
  sourceName: string;
}

/** Entries of one kind inside one rules family; the families are never compared with each other. */
export interface DuplicateGroup {
  class: DuplicateClass;
  family: RulesFamilyId;
  members: DuplicateMember[];
}

/** A feature a class grants at a level, or an option it offers there through a choice. */
export interface ClassTableRef {
  contentId: string;
  name: string;
}

export interface ClassLevelRow {
  level: number;
  proficiencyBonus: number;
  features: ClassTableRef[];
  choices: ClassTableRef[];
  /** Scale values as text, by column name; empty where the scale has no value at this level. */
  scales: Record<string, string>;
  /** Spell slots by spell level (1, 2, ...); absent without ordinary slots at this level. */
  slots?: number[] | null;
  pactSlots?: number | null;
  pactLevel?: number | null;
}

/** `content.classTable`: read-only; the 20-level progression of one class revision, published or draft. */
export interface ClassTableView {
  columns: string[];
  rows: ClassLevelRow[];
}
