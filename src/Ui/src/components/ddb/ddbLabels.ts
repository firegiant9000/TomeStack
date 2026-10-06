import type { MatchKind, MatchStatus, NumberAction } from '../../api/types';

export const kindLabels: Record<MatchKind, string> = {
  class: 'Classes',
  subclass: 'Subclasses',
  species: 'Species',
  background: 'Background',
  feat: 'Feats',
  skill: 'Skills',
  spell: 'Spells',
  item: 'Equipment',
  feature: 'Features',
};

/** The order the match tables appear in: the order the import applies them. */
export const kindOrder: MatchKind[] = ['class', 'subclass', 'species', 'background', 'feat', 'skill', 'item', 'spell', 'feature'];

export const statusLabels: Record<MatchStatus, string> = {
  matched: 'Matched',
  choose: 'Needs a choice',
  notFound: 'Not found',
  noPlace: 'No place for it',
  unreadable: 'Unreadable',
  leftOut: 'Left out',
};

export const numberActionLabels: Record<NumberAction, string> = {
  useTomeStack: "Use TomeStack's number",
  keepSheet: "Keep the sheet's number",
  note: 'Note it',
};

/** Row notes are codes; these say what they mean. */
export const noteLabels: Record<string, string> = {
  'campaign.source-not-allowed': 'The campaign does not allow its source; the sheet will warn.',
  'choice.not-offered': 'The class has not reached the level of its subclass choice.',
  'class.not-matched': 'Its class was not matched.',
  'subclass.detected-from-features': "Proposed from the sheet's features; change it if the sheet meant another subclass.",
  'content.no-open-choice': 'Installed, but no open choice offers it and it cannot be added on its own.',
  'skill.no-open-choice': 'No open choice offers this skill.',
  'spell.no-caster': 'No class or subclass of the character casts spells.',
  'spell.duplicate': 'Already listed for this caster; recorded once.',
  'spell.caster-not-found': 'The caster picked for it is no longer one of the classes; pick again.',
  'resolution.not-found': 'Your earlier pick is no longer available (a newer version, or another kind); pick again.',
  'character.spells-too-many': 'Past the 500 spells a character can hold.',
  'character.class-duplicate': 'The same class is listed twice.',
};

export const notBroughtOverLabels: Record<string, string> = {
  currency: 'Currency',
  notes: 'Notes',
  speed: 'Speed',
  passivePerception: 'Passive Perception',
  attunement: 'Attunement',
  languages: 'Languages',
  toolProficiencies: 'Tool proficiencies',
  senses: 'Senses',
  appearance: 'Appearance',
  backstory: 'Backstory',
  playerName: 'Player name',
  playState: 'Hit points, spent hit dice and slots, death saves and inspiration (the character starts rested)',
};
