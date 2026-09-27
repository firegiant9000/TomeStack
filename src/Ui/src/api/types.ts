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
  baseAbilities: AbilityScores;
  pins: ContentReference[];
  overrides: FieldOverride[];
  /** Character schema v4; absent in a draft means fresh (full hit points, nothing spent). */
  play?: PlayState;
  /** Items carried; only equipped ones apply (M2 item 4). */
  equipment?: EquipmentEntry[];
  updatedAt: string;
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
}

export interface ResourceUse {
  contentId: string;
  resourceId: string;
  spent: number;
}

/** SPEC C-05, character schema v4: changed only by the confirmed `character.play` command. */
export interface PlayState {
  /** Absent or null: at the maximum. */
  currentHitPoints?: number | null;
  temporaryHitPoints: number;
  resources: ResourceUse[];
  conditions: string[];
  exhaustion: number;
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
  | 'setExhaustion';

export interface PlayAction {
  action: PlayActionKind;
  amount?: number;
  contentId?: string;
  resourceId?: string;
  condition?: string;
}

export interface RestChange {
  id: string;
  kind: 'hitPoints' | 'temporaryHitPoints' | 'resource' | 'exhaustion';
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
}

export interface CharacterView {
  character: Character;
  sheet: CharacterSheet;
}

export interface CharacterSummary {
  id: string;
  name: string;
  rulesFamily: RulesFamilyId;
  updatedAt: string;
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

export type Effect =
  | (EffectBase & { type: 'modifier'; operation: 'bonus' | 'set' | 'replace'; target: string; value: string })
  | (EffectBase & { type: 'grant'; grant: 'proficiency' | 'expertise' | 'content'; target?: string; content?: ContentReference; level?: number })
  | (EffectBase & { type: 'resource'; resourceId: string; label: string; maximum: string })
  | (EffectBase & { type: 'recovery'; resourceId: string; on: RestPeriod; amount: string })
  | (EffectBase & { type: 'roll'; rollId: string; label: string; dice: string; resourceId?: string })
  | (EffectBase & { type: 'armor'; category: 'light' | 'medium' | 'heavy' | 'shield'; armorClass: number; dexterityCap?: number })
  | (EffectBase & { type: 'choice'; choiceId: string; count: number; options: ContentReference[]; level?: number });

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

export interface StudioEntry {
  contentId: string;
  name: string;
  kind: ContentKind;
  revisions: ContentRevision[];
  latest: ContentRevision;
  latestPublished?: ContentRevision;
}

export type ReferenceRole = 'pin' | 'class' | 'choice' | 'grant' | 'equipment';

export interface AffectedCharacter {
  characterId: string;
  name: string;
  pinned: ContentReference;
  role: ReferenceRole;
  via?: string;
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

export interface ImportResult {
  added: number;
  replaced: number;
  unchanged: number;
  characters: string[];
  /** Relative to the data folder; set when a local character was replaced. Import it to restore. */
  backupFile?: string;
}
