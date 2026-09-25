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
  baseAbilities: AbilityScores;
  pins: ContentReference[];
  overrides: FieldOverride[];
  updatedAt: string;
  // Unknown fields round-trip; keep them when re-saving.
  [extension: string]: unknown;
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

export interface TraceEntry {
  order: number;
  operation: 'base' | 'add' | 'derive' | 'override' | string;
  description: string;
  amount?: number;
  result: number;
  origin: TraceOrigin;
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
}

export interface CharacterSheet {
  characterId: string;
  rulesFamily: RulesFamilyId;
  fields: DerivedValue[];
  diagnostics: Diagnostic[];
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
}

export interface AppInfo {
  version: string;
  schemaVersion: number;
  rulesFamilies: RulesFamilyPolicy[];
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
}

export interface PackageManifest {
  format: string;
  formatVersion: number;
  createdAt: string;
  appVersion: string;
  characters: string[];
  notices: LicenseNotice[];
  attachmentPolicy: string;
}

export interface ExportedPackage {
  fileName: string;
  base64: string;
  manifest: PackageManifest;
}

export type PackageItemAction = 'add' | 'unchanged' | 'replace' | 'conflict';

export interface PackageItem {
  kind: 'source' | 'contentRevision' | 'character';
  id: string;
  name: string;
  action: PackageItemAction;
  detail?: string;
}

export interface PackagePreview {
  canApply: boolean;
  manifest?: PackageManifest;
  items: PackageItem[];
  errors: Diagnostic[];
  warnings: Diagnostic[];
}

export interface ImportResult {
  added: number;
  replaced: number;
  unchanged: number;
  characters: string[];
}
