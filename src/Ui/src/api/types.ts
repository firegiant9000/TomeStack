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
  updatedAt: string;
  // Unknown fields round-trip; keep them when re-saving.
  [extension: string]: unknown;
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

export interface CharacterSheet {
  characterId: string;
  rulesFamily: RulesFamilyId;
  fields: DerivedValue[];
  diagnostics: Diagnostic[];
  choices?: ChoiceStatus[];
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
