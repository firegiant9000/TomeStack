import { useEffect, useRef, useState } from 'react';
import { client } from '../api/client';
import type { CharacterView, DerivedValue, GapNote, LicenseNotice, TraceOrigin } from '../api/types';

const abilities = ['str', 'dex', 'con', 'int', 'wis', 'cha'] as const;
const signed = (n: number) => (n >= 0 ? `+${n}` : `${n}`);

/** Source and page only: never a file name or path (ADR-005, ADR-007). */
function cite(origin: TraceOrigin): string {
  if (origin.kind !== 'content') return '';
  const page = origin.page ? (origin.page.end && origin.page.end !== origin.page.start ? `, pp. ${origin.page.start}-${origin.page.end}` : `, p. ${origin.page.start}`) : '';
  return `${origin.sourceTitle ?? 'Unknown source'}${page}`;
}

interface Props {
  view: CharacterView;
  onError: (error: unknown) => void;
  onClose: () => void;
}

/**
 * M3 C4, the printable backup (MVP "Separate replacement milestone"; SPEC P-03): a print-only layout of the sheet,
 * printed with the WebView2 print dialog (which also offers "Microsoft Print to PDF"). Everything is on this computer:
 * no socket, no path from the page, and gap notes only when the player ticks them in.
 */
export function PrintView({ view, onError, onClose }: Props) {
  const { character, sheet } = view;
  const [includeNotes, setIncludeNotes] = useState(false);
  const [includeSessionNotes, setIncludeSessionNotes] = useState(false);
  const [notes, setNotes] = useState<GapNote[]>();
  const [notices, setNotices] = useState<LicenseNotice[]>([]);
  const [version, setVersion] = useState('');
  const section = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);

  // Investigation 2026-10-06 item 9: the preview opens under the summary, often at or below the fold, and nothing moved. Scroll
  // the preview's top edge into view, then focus its heading without a second scroll (WCAG 2.4.3). Focus alone does not
  // scroll when the heading is already just in view (measured at 1280x800). jsdom has no scrollIntoView: optional call.
  useEffect(() => {
    section.current?.scrollIntoView?.({ block: 'start' });
    heading.current?.focus({ preventScroll: true });
  }, []);

  useEffect(() => {
    let current = true;
    // The backup preview lists every source's license and attribution; it writes nothing (ADR-007).
    client
      .previewExport([character.id], 'backup')
      .then((preview) => {
        if (current) setNotices(preview.included);
      })
      .catch(onError);
    client
      .info()
      .then((info) => {
        if (current) setVersion(info.version);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [character.id, onError]);

  function toggleNotes(on: boolean) {
    setIncludeNotes(on);
    if (on && !notes)
      client
        .listGapNotes(character.id)
        .then(setNotes)
        .catch(onError);
  }

  const field = (id: string): DerivedValue | undefined => sheet.fields.find((f) => f.field === id);
  const value = (id: string) => field(id)?.value ?? 0;
  const classNames = new Map((sheet.features ?? []).filter((f) => f.kind === 'class').map((f) => [f.content.contentId, f.name]));
  const skills = sheet.fields.filter((f) => f.field.startsWith('skill.'));
  const hp = sheet.hitPoints;
  const play = character.play ?? { conditions: [], exhaustion: 0, inspiration: false };
  const caster = (sheet.spellcasting ?? []).length > 0;

  return (
    <section className="print-sheet" role="region" aria-label="Print preview" id="print-preview" ref={section}>
      <div className="print-controls">
        <h3 id="print-heading" tabIndex={-1} ref={heading}>Print character</h3>
        <p className="hint">A printable backup of this sheet. It prints from this computer; nothing is sent anywhere.</p>
        <label>
          <input type="checkbox" checked={includeNotes} onChange={(e) => toggleNotes(e.target.checked)} />
          Include gap notes (private: they may describe your homebrew)
        </label>
        <label>
          <input type="checkbox" checked={includeSessionNotes} onChange={(e) => setIncludeSessionNotes(e.target.checked)} />
          Include session notes (private)
        </label>
        <div className="actions">
          <button type="button" onClick={() => window.print()}>
            Print…
          </button>
          <button type="button" onClick={onClose}>
            Close print preview
          </button>
        </div>
      </div>

      <header>
        <h3 className="print-title">{character.name}</h3>
        <p>
          {character.rulesFamily} · Level {character.level}
          {character.classes.length > 0 &&
            ` · ${character.classes.map((c) => `${classNames.get(c.class.contentId) ?? 'Class'} ${c.level}`).join(' / ')}`}
          {view.campaign && ` · Campaign: ${view.campaign.name}`}
        </p>
      </header>

      <table className="print-abilities">
        <caption>Abilities</caption>
        <thead>
          <tr>
            <th scope="col">Ability</th>
            <th scope="col">Score</th>
            <th scope="col">Modifier</th>
            <th scope="col">Saving throw</th>
          </tr>
        </thead>
        <tbody>
          {abilities.map((a) => (
            <tr key={a}>
              <th scope="row">{a.toUpperCase()}</th>
              <td>{value(`ability.${a}.score`)}</td>
              <td>{signed(value(`ability.${a}.mod`))}</td>
              <td>{signed(value(`save.${a}`))}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <p>
        Proficiency bonus {signed(value('proficiencyBonus'))} · Armor Class {value('armorClass')} · Initiative {signed(value('initiative'))} · Hit
        points {hp ? `${hp.current} of ${hp.maximum}${hp.temporary > 0 ? `, ${hp.temporary} temporary` : ''}` : value('hitPoints')}
      </p>
      {(sheet.hitDice ?? []).length > 0 && <p>Hit dice: {sheet.hitDice!.map((d) => `${d.remaining} of ${d.total} d${d.die}`).join(', ')}</p>}
      {(play.conditions.length > 0 || play.exhaustion > 0 || play.inspiration) && (
        <p>
          {play.conditions.length > 0 && `Conditions: ${play.conditions.join(', ')}. `}
          {play.exhaustion > 0 && `Exhaustion ${play.exhaustion}. `}
          {play.inspiration && 'Inspiration.'}
        </p>
      )}

      <table>
        <caption>Skills</caption>
        <tbody>
          {skills.map((s) => (
            <tr key={s.field}>
              <th scope="row">{s.label}</th>
              <td>{signed(s.value)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {(sheet.attacks ?? []).length > 0 && (
        <table>
          <caption>Attacks</caption>
          <thead>
            <tr>
              <th scope="col">Weapon</th>
              <th scope="col">To hit</th>
              <th scope="col">Damage</th>
              <th scope="col">Properties</th>
            </tr>
          </thead>
          <tbody>
            {sheet.attacks!.map((a) => (
              <tr key={`${a.item.revisionId}-${a.effectId}`}>
                <th scope="row">{a.name}</th>
                <td>{signed(a.toHit)}</td>
                <td>
                  {a.damage} {a.damageType}
                  {a.versatileDamage && ` (${a.versatileDamage} two-handed)`}
                </td>
                <td>{[...a.properties, a.range].filter(Boolean).join(', ')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {(sheet.resources ?? []).length > 0 && (
        <table>
          <caption>Resources</caption>
          <tbody>
            {sheet.resources!.map((r) => (
              <tr key={`${r.content.revisionId}-${r.resourceId}`}>
                <th scope="row">{r.label}</th>
                <td>{r.maximum === undefined ? `${r.spent} spent (tracked by hand)` : `${r.current} of ${r.maximum}`}</td>
                <td>{r.recoveries.map((x) => (x.on === 'shortRest' ? 'short rest' : 'long rest')).join(', ')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {caster && (
        <div>
          <h4>Spells</h4>
          {(sheet.spellSlots ?? []).length > 0 && (
            <p>Spell slots: {sheet.spellSlots!.map((s) => `level ${s.level}: ${s.remaining} of ${s.maximum}`).join(', ')}</p>
          )}
          {sheet.pactSlots && (
            <p>
              Pact Magic: {sheet.pactSlots.remaining} of {sheet.pactSlots.maximum} (level {sheet.pactSlots.level})
            </p>
          )}
          {sheet.spellcasting!.map((c) => (
            <div key={c.content.revisionId}>
              <p>
                {c.name} (class level {c.classLevel}): spell attack {signed(c.attackBonus)}, save DC {c.saveDc}
              </p>
              <ul>
                {c.spells.map((s) => (
                  <li key={s.spell.revisionId}>
                    {s.level === 0 ? 'Cantrip' : `Level ${s.level}`}: {s.name}
                    {c.preparation === 'prepared' && !s.prepared ? ' (not prepared)' : ''}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}

      <div>
        <h4>Features</h4>
        {(sheet.features ?? []).map((f) => (
          <div key={f.content.revisionId} className="print-feature">
            <p>
              <strong>{f.name}</strong> ({cite(f.origin) || f.kind}; {f.automation})
            </p>
            {f.summary && <p>{f.summary}</p>}
            {f.effects
              .filter((e) => e.automation !== 'automatic' && e.text)
              .map((e) => (
                <p key={e.id}>By hand: {e.text}</p>
              ))}
          </div>
        ))}
      </div>

      {character.overrides.length > 0 && (
        <div>
          <h4>Overrides</h4>
          <ul>
            {character.overrides.map((o) => (
              <li key={o.field}>
                {field(o.field)?.label ?? o.field}: {o.value} (calculated {field(o.field)?.computedValue ?? '?'}){o.reason ? `. ${o.reason}` : ''}
              </li>
            ))}
          </ul>
        </div>
      )}

      {includeSessionNotes && (character.notes ?? []).length > 0 && (
        <div>
          <h4>Session notes</h4>
          <ul>
            {[...character.notes!].sort((a, b) => a.date.localeCompare(b.date) || a.createdAt.localeCompare(b.createdAt)).map((n) => (
              <li key={n.id}>
                {n.date}: {n.text}
              </li>
            ))}
          </ul>
        </div>
      )}

      {includeNotes && (
        <div>
          <h4>Gap notes</h4>
          {notes === undefined ? (
            <p>Loading…</p>
          ) : notes.length === 0 ? (
            <p>No gap notes.</p>
          ) : (
            <ul>
              {notes.map((n) => (
                <li key={n.id}>
                  {n.target.label ?? n.target.kind} ({n.status}): {n.text}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <footer>
        <p>
          Printed from TomeStack {version} on {new Date().toLocaleDateString()}.
        </p>
        {notices.map((n) => (
          <p key={n.sourceId} className="print-notice">
            {n.title} ({n.license}){n.attribution ? `. ${n.attribution}` : ''}
            {n.modificationNotice ? ` ${n.modificationNotice}` : ''}
          </p>
        ))}
      </footer>
    </section>
  );
}
