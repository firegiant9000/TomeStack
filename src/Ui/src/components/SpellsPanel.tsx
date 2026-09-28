import type { Ability, Character, CharacterView, PlayAction, RollTarget, SlotValue, SpellcastingEntry, SpellEntry } from '../api/types';
import { pageText } from './PlayPanels';

const abilityNames: Record<Ability, string> = {
  str: 'Strength',
  dex: 'Dexterity',
  con: 'Constitution',
  int: 'Intelligence',
  wis: 'Wisdom',
  cha: 'Charisma',
};

type Act = (action: PlayAction) => void;

/**
 * D04 (M2 spellcasting): slots and spells. Every slot change is one confirmed `character.play` press; rolling a spell
 * never spends a slot. "Cast" spends the lowest slot that is left at the spell's level or higher (Pact Magic for a pact
 * caster); to cast at a higher level, spend that slot with its own button instead.
 */
export function SpellsPanel({
  view,
  act,
  roll,
  onSave,
}: {
  view: CharacterView;
  act: Act;
  roll: (target: RollTarget) => void;
  /** Saves a changed spell list (preparing is a choice, not play state). */
  onSave: (character: Character) => void;
}) {
  const casters = view.sheet.spellcasting ?? [];
  if (casters.length === 0) return null;
  const slots = view.sheet.spellSlots ?? [];
  const pact = view.sheet.pactSlots;

  const canCast = (entry: SpellcastingEntry, spell: SpellEntry) =>
    entry.slotKind === 'pactMagic' ? (pact?.remaining ?? 0) > 0 : slots.some((s) => s.level >= spell.level && s.remaining > 0);

  function cast(entry: SpellcastingEntry, spell: SpellEntry) {
    if (entry.slotKind === 'pactMagic') {
      act({ action: 'spendPactSlot' });
      return;
    }
    const slot = slots.find((s) => s.level >= spell.level && s.remaining > 0);
    if (slot) act({ action: 'spendSlot', amount: slot.level });
  }

  function togglePrepared(entry: SpellcastingEntry, spell: SpellEntry) {
    const spells = (view.character.spells ?? []).map((s) =>
      s.caster === entry.content.contentId && s.spell.revisionId === spell.spell.revisionId ? { ...s, prepared: !spell.prepared } : s,
    );
    onSave({ ...view.character, spells });
  }

  return (
    <section aria-labelledby="spells-heading" className="play-panel">
      <h3 id="spells-heading">Spells and slots</h3>
      {(slots.length > 0 || pact) && (
        <ul className="resources" aria-label="Spell slots">
          {slots.map((slot) => (
            <SlotItem key={slot.level} label={`Level ${slot.level} slots`} slot={slot} spend={{ action: 'spendSlot', amount: slot.level }} regain={{ action: 'regainSlot', amount: slot.level }} act={act} />
          ))}
          {pact && <SlotItem label={`Pact Magic slots (level ${pact.level})`} slot={pact} spend={{ action: 'spendPactSlot' }} regain={{ action: 'regainPactSlot' }} act={act} />}
        </ul>
      )}
      {casters.map((entry) => (
        <section key={entry.content.revisionId} aria-labelledby={`caster-${entry.content.revisionId}`}>
          <h4 id={`caster-${entry.content.revisionId}`}>
            {entry.name} (level {entry.classLevel}, {abilityNames[entry.ability]}): spell attack {entry.attackBonus >= 0 ? '+' : ''}
            {entry.attackBonus}, save DC {entry.saveDc}
          </h4>
          <p className="hint">
            {entry.cantripsAllowed !== undefined && `${entry.cantripsAllowed} cantrips · `}
            {entry.spellsAllowed !== undefined && `${entry.spellsAllowed} ${entry.preparation === 'known' ? 'spells known' : 'spells prepared'} · `}
            {entry.origin.sourceTitle}
            {entry.origin.page ? `, ${pageText(entry.origin.page)}` : ''}
            {!entry.primary && ' · its slots are not combined with the first caster’s (record the total as an override)'}
          </p>
          {entry.warnings.length > 0 && (
            <ul className="warnings" aria-label={`${entry.name} spell warnings`}>
              {entry.warnings.map((w, i) => (
                <li key={`${w.code}-${i}`}>{w.message}</li>
              ))}
            </ul>
          )}
          {entry.spells.length === 0 ? (
            <p className="hint">No spells recorded. Pick them in the builder ("Make choices" or at level-up).</p>
          ) : (
            <ul className="features" aria-label={`${entry.name} spells`}>
              {entry.spells.map((spell) => (
                <li key={spell.spell.revisionId} className="feature">
                  <details>
                    <summary>
                      <span className="option-name">{spell.name}</span> <span className="tag">{spell.level === 0 ? 'cantrip' : `level ${spell.level}`}</span>
                      {spell.concentration && <span className="tag">concentration</span>}
                      {spell.ritual && <span className="tag">ritual</span>}
                      {!spell.prepared && <span className="hint"> not prepared</span>}
                    </summary>
                    <p className="hint">
                      {[spell.school, spell.castingTime, spell.range, spell.components, spell.duration].filter(Boolean).join(' · ')}
                      {spell.save && ` · ${abilityNames[spell.save]} save (DC ${entry.saveDc})`}
                    </p>
                    {spell.summary && <p>{spell.summary}</p>}
                    {spell.text && <p>{spell.text}</p>}
                    <p className="hint">
                      {spell.origin.sourceTitle}
                      {spell.origin.page ? `, ${pageText(spell.origin.page)}` : ''}
                    </p>
                  </details>
                  {entry.preparation === 'prepared' && spell.level > 0 && (
                    <label className="choice">
                      <input type="checkbox" checked={spell.prepared} onChange={() => togglePrepared(entry, spell)} />
                      Prepared: {spell.name}
                    </label>
                  )}
                  {spell.attack !== 'none' && (
                    <button type="button" onClick={() => roll({ spell: spell.spell, spellAttack: true })}>
                      Roll {spell.name} attack
                    </button>
                  )}
                  {spell.dice && (
                    <button type="button" onClick={() => roll({ spell: spell.spell })}>
                      Roll {spell.name} ({spell.dice})
                    </button>
                  )}
                  {spell.level > 0 && spell.prepared && (
                    <button type="button" disabled={!canCast(entry, spell)} onClick={() => cast(entry, spell)}>
                      Cast {spell.name} (spend a slot)
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
      ))}
    </section>
  );
}

function SlotItem({ label, slot, spend, regain, act }: { label: string; slot: SlotValue; spend: PlayAction; regain: PlayAction; act: Act }) {
  return (
    <li className="resource">
      <h4>
        {label}: {slot.remaining} of {slot.maximum}
      </h4>
      <div className="actions">
        <button type="button" disabled={slot.remaining === 0} onClick={() => act(spend)}>
          Spend 1 {label.toLowerCase().replace(' slots', ' slot')}
        </button>
        <button type="button" disabled={slot.spent === 0} onClick={() => act(regain)}>
          Regain 1 {label.toLowerCase().replace(' slots', ' slot')}
        </button>
      </div>
    </li>
  );
}
