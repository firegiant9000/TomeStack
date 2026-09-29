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
  /** Spells known or prepared, per caster (character schema v6, D04). */
  spells?: KnownSpell[];
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
  units: 'score' | 'modifier' | 'bonus' | string;
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
}

export type Activation = 'action' | 'bonusAction' | 'reaction' | 'other';

/** SPEC C-02, C-04: an attack with an equipped weapon. */
export interface AttackEntry {
  item: ContentReference;
  name: string;
  effectId: string;
  attack: 'melee' | 'ranged';
  category: 'simple' | 'martial';
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
  features?: FeatureEntry[];
  hitPoints?: HitPointState;
  hitDice?: HitDiceValue[];
  spellcasting?: SpellcastingEntry[];
  spellSlots?: SlotValue[];
  pactSlots?: SlotValue;
  attacks?: AttackEntry[];
  toggles?: ToggleValue[];
  /** Content v9 (ADR-010): each class-table column at the character's level in that class. */
  scales?: ScaleValue[];
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
  | 'toggleOff';

export interface PlayAction {
  action: PlayActionKind;
  amount?: number;
  contentId?: string;
  resourceId?: string;
  condition?: string;
  /** Content v6: the toggle to switch on or off (with `contentId`). */
  toggleId?: string;
}

export interface RestChange {
  id: string;
  /** `hitDie`: one spent hit die of a short rest (`die`, `amount` hit points); `hitDice`: dice regained on a long rest. */
  kind: 'hitPoints' | 'temporaryHitPoints' | 'resource' | 'exhaustion' | 'hitDie' | 'hitDice' | 'deathSaves' | 'spellSlots' | 'pactSlots' | 'toggle';
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
  /** A death saving throw: a d20 with no modifier. */
  deathSave?: boolean;
  /** One of the character's spells: its attack roll (`spellAttack`) or its dice. */
  spell?: ContentReference;
  spellAttack?: boolean;
  /** An equipped weapon: its attack roll, or its damage (`damage`), two-handed (`versatile`). */
  weapon?: ContentReference;
  damage?: boolean;
  versatile?: boolean;
}

/** SPEC P-01: a local campaign profile. It never changes calculation; it warns about content outside it. */
export interface Campaign {
  id: string;
  schemaVersion?: number;
  name: string;
  rulesFamily: RulesFamilyId;
  allowedSources: string[];
  houseRules?: string;
  updatedAt?: string;
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
  spell?: { level: number; lists: string[]; school?: string; concentration: boolean; ritual: boolean };
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
}

export interface ExportPreview {
  purpose: ExportPurpose;
  fileName: string;
  characters: string[];
  included: LicenseNotice[];
  omitted: OmittedSource[];
  /** Gap notes the package would carry: all of the characters' notes in a backup, 0 in a share (M3 B3). */
  gapNotes: number;
}

export type GapTargetKind = 'feature' | 'field';

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
  firstPage: number;
  lastPage?: number;
  wholeDocument: boolean;
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
  createdAt: string;
  updatedAt: string;
}

export interface ImportSearchHit {
  page: number;
  snippet: string;
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
}

export interface CandidateFilter {
  page?: number;
  kind?: ContentKind;
  minConfidence?: number;
  maxConfidence?: number;
  status?: CandidateStatus;
}

export interface CandidateEdit {
  name?: string;
  kind?: ContentKind;
  rulesFamilies?: RulesFamilyId[];
  summary?: string;
  effects?: unknown[];
  dismissReferences?: string[];
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
  kind: 'source' | 'contentRevision' | 'character';
  id: string;
  name: string;
  action: PackageItemAction;
  detail?: string;
  /** Set on a source that differs from the local record; apply needs a SourceChoice for it. */
  changes?: FieldChange[];
}

export type SourceChoice = 'keepLocal' | 'useImported';

export interface PackagePreview {
  canApply: boolean;
  manifest?: PackageManifest;
  items: PackageItem[];
  errors: Diagnostic[];
  warnings: Diagnostic[];
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

export type Effect =
  | (EffectBase & { type: 'modifier'; operation: 'bonus' | 'set' | 'replace'; target: string; value: string; toggle?: string })
  // Content v6 (M3 B2): switched on and off in play; turning it on can spend one use of a resource of this revision.
  | (EffectBase & { type: 'toggle'; toggleId: string; label: string; resourceId?: string })
  | (EffectBase & { type: 'grant'; grant: 'proficiency' | 'expertise' | 'content'; target?: string; content?: ContentReference; level?: number; onlyAs?: ClassEntry })
  | (EffectBase & { type: 'resource'; resourceId: string; label: string; maximum: string })
  | (EffectBase & { type: 'recovery'; resourceId: string; on: RestPeriod; amount: string })
  | (EffectBase & { type: 'roll'; rollId: string; label: string; dice: string; resourceId?: string })
  | (EffectBase & { type: 'armor'; category: 'light' | 'medium' | 'heavy' | 'shield'; armorClass: number; dexterityCap?: number })
  | (EffectBase & { type: 'choice'; choiceId: string; count: number; options: ContentReference[]; level?: number; onlyAs?: ClassEntry })
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
}

export type LibraryBackupOutcome =
  | { saved: false }
  | { saved: true; fileName: string; bytes: number; contents: LibraryBackupPreview; warnings: Diagnostic[] };

export type LibraryRestoreChoice =
  | { chosen: false }
  | { chosen: true; token: string; fileName: string; preview: PackagePreview };

export interface LibraryRestoreResult {
  added: number;
  replaced: number;
  unchanged: number;
  pdfsCopied: number;
  /** Relative to the data folder: the database as it was before the restore, when anything was replaced. */
  safetyCopy?: string;
  warnings: Diagnostic[];
}
