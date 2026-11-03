import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { client } from '../api/client';
import type { CharacterView, DerivedValue, GapNote, LicenseNotice, TraceOrigin } from '../api/types';
import { textSize } from '../settings';
import { detailRows } from './detailsModel';

const abilities = ['str', 'dex', 'con', 'int', 'wis', 'cha'] as const;
const passives = [
  ['passive.perception', 'Perception'],
  ['passive.insight', 'Insight'],
  ['passive.investigation', 'Investigation'],
] as const;
const signed = (n: number) => (n >= 0 ? `+${n}` : `${n}`);

/** P14 (D71): the printed card's page. The card is laid out at 7.27in wide (A4 less 0.5in margins) and fitted in 10in (Letter less 0.5in margins): the smaller of each paper. */
const cardAvailableHeight = 10 * 96;
/** A small allowance (px) for rounding and printer differences; the margins themselves are measured. */
const cardSlack = 8;
/** Most tick boxes drawn for one pool. A bigger pool prints its size and one write-in box, so it stays small and bounded. */
export const maxTickBoxes = 20;

export interface PrintOptions {
  mode: 'full' | 'combat';
  largePrint: boolean;
}
export interface CardHeights {
  /** Header, footer and the core blocks: never dropped. */
  core: number;
  conditions: number;
  toggles: number;
  spells: number;
}
export interface CardFit {
  conditions: boolean;
  toggles: boolean;
  spells: boolean;
  continued: boolean;
}

/**
 * P14 (D71): which droppable sections of the combat card stay on the page. Pure, so it is tested without a layout engine. Sections
 * go in this order: the conditions list, then the toggles, then the spell slots last. The core never goes, and the font is never
 * shrunk. When something with height was dropped, or the core alone is too tall, the card says it continues on the full sheet
 * and that line (`continuedHeight`) takes room too.
 */
export function fitCombatCard(h: CardHeights, available: number, continuedHeight: number): CardFit {
  const tries: Omit<CardFit, 'continued'>[] = [
    { conditions: true, toggles: true, spells: true },
    { conditions: false, toggles: true, spells: true },
    { conditions: false, toggles: false, spells: true },
    { conditions: false, toggles: false, spells: false },
  ];
  for (const t of tries) {
    const droppedHeight = (t.conditions ? 0 : h.conditions) + (t.toggles ? 0 : h.toggles) + (t.spells ? 0 : h.spells);
    const continued = droppedHeight > 0;
    const used = h.core + (t.conditions ? h.conditions : 0) + (t.toggles ? h.toggles : 0) + (t.spells ? h.spells : 0) + (continued ? continuedHeight : 0);
    if (used <= available) return { ...t, continued };
  }
  return { conditions: false, toggles: false, spells: false, continued: true };
}

const Tick = () => (
  <span className="tick" aria-hidden="true">
    ☐
  </span>
);
/** One box per point up to `maxTickBoxes`; above it, "<count> <noun>" and one blank write-in box. */
const Ticks = ({ count, noun }: { count: number; noun: string }) =>
  count > maxTickBoxes ? (
    <span className="ticks">
      {count} {noun} <Tick />
    </span>
  ) : (
    <>
      <span className="visually-hidden">
        {count} {noun}
      </span>
      <span className="ticks" aria-hidden="true">
        {Array.from({ length: count }, (_, i) => (
          <Tick key={i} />
        ))}
      </span>
    </>
  );

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
  const [includeDetails, setIncludeDetails] = useState(false);
  const [notes, setNotes] = useState<GapNote[]>();
  const [notices, setNotices] = useState<LicenseNotice[]>([]);
  const [version, setVersion] = useState('');
  // P14 (D71): print options are page memory only (component state, never stored): reopening starts on the full sheet.
  const [options, setOptions] = useState<PrintOptions>({ mode: 'full', largePrint: false });
  const [fit, setFit] = useState<CardFit>({ conditions: true, toggles: true, spells: true, continued: false });
  const section = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const pageEl = useRef<HTMLDivElement>(null);
  const conditionsEl = useRef<HTMLDivElement>(null);
  const togglesEl = useRef<HTMLDivElement>(null);
  const spellsEl = useRef<HTMLDivElement>(null);
  const combat = options.mode === 'combat';
  // Large print: 150%, or the user's text size when larger, at most 200%. Without it the print stays at 100%.
  const printPercent = options.largePrint ? Math.min(200, Math.max(150, Number(textSize()))) : 100;

  useEffect(() => {
    if (!options.largePrint) return;
    const root = document.documentElement;
    root.style.setProperty('--print-font-size', `${printPercent}%`);
    return () => {
      root.style.removeProperty('--print-font-size');
    };
  }, [options.largePrint, printPercent]);

  const measure = useCallback(() => {
    const page = pageEl.current;
    if (!page) return;
    // The page block (header, card and footer) carries the print font size and the printed width, and its header and footer
    // sizes are in em, so one measurement here is the printed size. `flow-root` keeps the children's margins inside its rect.
    const continued = page.querySelector<HTMLElement>('.card-continued');
    const els = [conditionsEl.current, togglesEl.current, spellsEl.current, continued];
    const was = els.map((el) => Boolean(el?.hidden));
    const set = (el: HTMLElement | null, hidden: boolean) => {
      if (el) el.hidden = hidden;
    };
    els.forEach((el) => set(el, true));
    const core = page.getBoundingClientRect().height + cardSlack;
    const alone = (el: HTMLElement | null) => {
      if (!el) return 0;
      el.hidden = false;
      const height = el.getBoundingClientRect().height;
      el.hidden = true;
      return height;
    };
    const conditions = alone(conditionsEl.current);
    const toggles = alone(togglesEl.current);
    const spells = alone(spellsEl.current);
    els.forEach((el, i) => set(el, was[i] ?? false));
    const lineHeight = parseFloat(getComputedStyle(page).fontSize) || 16;
    const next = fitCombatCard({ core, conditions, toggles, spells }, cardAvailableHeight, lineHeight * 2.5);
    setFit((prev) => (prev.conditions === next.conditions && prev.toggles === next.toggles && prev.spells === next.spells && prev.continued === next.continued ? prev : next));
  }, []);

  // jsdom has no layout: every height is 0 there, so everything fits. In a browser the card is measured at the printed size.
  useLayoutEffect(() => {
    if (combat) measure();
  }, [combat, printPercent, measure, notices, version, sheet]);

  useEffect(() => {
    if (!combat) return;
    const before = () => flushSync(measure);
    window.addEventListener('beforeprint', before);
    return () => window.removeEventListener('beforeprint', before);
  }, [combat, measure]);

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
      .catch((error) => {
        if (current) onError(error);
      });
    client
      .info()
      .then((info) => {
        if (current) setVersion(info.version);
      })
      .catch((error) => {
        if (current) onError(error);
      });
    return () => {
      current = false;
    };
  }, [character.id, onError]);

  // The panel may close before the notes arrive; a late reply or failure must not touch the sheet (no effect owns this call).
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  function toggleNotes(on: boolean) {
    setIncludeNotes(on);
    if (on && !notes)
      client
        .listGapNotes(character.id)
        .then((list) => {
          if (mounted.current) setNotes(list);
        })
        .catch((error) => {
          if (mounted.current) onError(error);
        });
  }

  const field = (id: string): DerivedValue | undefined => sheet.fields.find((f) => f.field === id);
  const value = (id: string) => field(id)?.value ?? 0;
  /** The combat card shows a dash for a field the sheet does not have, never a made-up 0. */
  const shown = (id: string, format: (n: number) => string = String) => {
    const f = field(id);
    return f ? format(f.value) : '—';
  };
  const classNames = new Map((sheet.features ?? []).filter((f) => f.kind === 'class').map((f) => [f.content.contentId, f.name]));
  const skills = sheet.fields.filter((f) => f.field.startsWith('skill.'));
  const hp = sheet.hitPoints;
  const play = character.play ?? { conditions: [], exhaustion: 0, inspiration: false };
  const caster = (sheet.spellcasting ?? []).length > 0;
  const printedDetails = detailRows(character.details);

  return (
    <section className={options.largePrint ? 'print-sheet print-large' : 'print-sheet'} role="region" aria-label="Print preview" id="print-preview" ref={section}>
      <div className="print-controls">
        <h3 id="print-heading" tabIndex={-1} ref={heading}>Print character</h3>
        <p className="hint">A printable backup of this sheet. It prints from this computer; nothing is sent anywhere.</p>
        <fieldset>
          <legend>Print</legend>
          <label>
            <input type="radio" name="print-mode" checked={!combat} onChange={() => setOptions((o) => ({ ...o, mode: 'full' }))} />
            Full sheet
          </label>
          <label>
            <input type="radio" name="print-mode" checked={combat} onChange={() => setOptions((o) => ({ ...o, mode: 'combat' }))} />
            Combat card
          </label>
        </fieldset>
        <label>
          <input type="checkbox" checked={options.largePrint} onChange={(e) => setOptions((o) => ({ ...o, largePrint: e.target.checked }))} />
          Large print
        </label>
        {combat ? (
          <p className="hint">The combat card is one page of numbers and blank boxes to fill in at the table. It leaves out notes, gap notes and details.</p>
        ) : (
          <>
            <label>
              <input type="checkbox" checked={includeNotes} onChange={(e) => toggleNotes(e.target.checked)} />
              Include gap notes (private: they may describe your homebrew)
            </label>
            <label>
              <input type="checkbox" checked={includeSessionNotes} onChange={(e) => setIncludeSessionNotes(e.target.checked)} />
              Include session notes (private)
            </label>
            <label>
              <input type="checkbox" checked={includeDetails} onChange={(e) => setIncludeDetails(e.target.checked)} />
              Include details (personality, appearance, backstory, languages and tools)
            </label>
          </>
        )}
        <div className="actions">
          <button type="button" onClick={() => window.print()}>
            Print…
          </button>
          <button type="button" onClick={onClose}>
            Close print preview
          </button>
        </div>
      </div>

      <div className={combat ? 'combat-scroll' : undefined}>
      <div ref={pageEl} className={combat ? 'combat-page' : undefined} style={combat ? { fontSize: `${(16 * printPercent) / 100}px` } : undefined}>
      <header>
        <h3 className="print-title">{character.name}</h3>
        <p>
          {character.rulesFamily} · Level {character.level}
          {character.classes.length > 0 &&
            ` · ${character.classes.map((c) => `${classNames.get(c.class.contentId) ?? 'Class'} ${c.level}`).join(' / ')}`}
          {view.campaign && ` · Campaign: ${view.campaign.name}`}
        </p>
      </header>

      {combat ? (
          <div className="combat-card">
            <div>
              <div className="card-block">
                <table>
                  <caption>Vitals</caption>
                  <thead>
                    <tr>
                      <th scope="col">Armor Class</th>
                      <th scope="col">Hit point maximum</th>
                      <th scope="col">Current hit points</th>
                      <th scope="col">Temporary hit points</th>
                      <th scope="col">Speed</th>
                      <th scope="col">Initiative</th>
                      <th scope="col">Proficiency bonus</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td>{shown('armorClass')}</td>
                      <td>{shown('hitPoints')}</td>
                      <td className="blank-box" />
                      <td className="blank-box" />
                      <td>{shown('speed', (n) => `${n} ft.`)}</td>
                      <td>{shown('initiative', signed)}</td>
                      <td>{shown('proficiencyBonus', signed)}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div className="card-block">
                <table>
                  <caption>Saves</caption>
                  <thead>
                    <tr>
                      {abilities.map((a) => (
                        <th key={a} scope="col">
                          {a.toUpperCase()}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      {abilities.map((a) => (
                        <td key={a}>{shown(`save.${a}`, signed)}</td>
                      ))}
                    </tr>
                  </tbody>
                </table>
              </div>
              <div className="card-block">
                <table>
                  <caption>Passive scores</caption>
                  <thead>
                    <tr>
                      {passives.map(([id, label]) => (
                        <th key={id} scope="col">
                          {label}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      {passives.map(([id]) => (
                        <td key={id}>{shown(id)}</td>
                      ))}
                    </tr>
                  </tbody>
                </table>
              </div>
              {(sheet.attacks ?? []).length > 0 && (
                <div className="card-block">
                  <table>
                    <caption>Attacks</caption>
                    <thead>
                      <tr>
                        <th scope="col">Weapon</th>
                        <th scope="col">To hit</th>
                        <th scope="col">Damage</th>
                      </tr>
                    </thead>
                    <tbody>
                      {sheet.attacks!.map((a) => (
                        <tr key={`${a.item.revisionId}-${a.effectId}`}>
                          <th scope="row">{a.name}</th>
                          <td>{signed(a.toHit)}</td>
                          <td>
                            {a.damage} {a.damageType}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              {(sheet.resources ?? []).length > 0 && (
                <div className="card-block">
                  <table>
                    <caption>Resources</caption>
                    <tbody>
                      {sheet.resources!.map((r) => (
                        <tr key={`${r.content.revisionId}-${r.resourceId}`}>
                          <th scope="row">{r.label}</th>
                          <td>
                            {r.maximum === undefined ? (
                              <span className="blank-line" aria-hidden="true" />
                            ) : (
                              <Ticks count={r.maximum} noun={r.countsUp ? 'to build up' : 'uses'} />
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
            {((sheet.spellSlots ?? []).length > 0 || sheet.pactSlots) && (
              <div className="card-block" ref={spellsEl} hidden={!fit.spells}>
                <table>
                  <caption>Spell slots</caption>
                  <tbody>
                    {(sheet.spellSlots ?? []).map((s) => (
                      <tr key={s.level}>
                        <th scope="row">Level {s.level}</th>
                        <td>
                          <Ticks count={s.maximum} noun="slots" />
                        </td>
                      </tr>
                    ))}
                    {sheet.pactSlots && (
                      <tr>
                        <th scope="row">Pact Magic (level {sheet.pactSlots.level})</th>
                        <td>
                          <Ticks count={sheet.pactSlots.maximum} noun="slots" />
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            )}
            {(sheet.toggles ?? []).length > 0 && (
              <div className="card-block" ref={togglesEl} hidden={!fit.toggles}>
                <h4>Toggles</h4>
                <ul className="card-toggles">
                  {sheet.toggles!.map((t) => (
                    <li key={`${t.content.revisionId}-${t.effectId}`}>
                      <Tick /> {t.label}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            <div className="card-block" ref={conditionsEl} hidden={!fit.conditions}>
              <h4>Conditions</h4>
              <ul className="card-lines" aria-hidden="true">
                {[0, 1, 2, 3].map((i) => (
                  <li key={i} className="blank-line" />
                ))}
              </ul>
            </div>
            {fit.continued && <p className="card-continued">Continued on the full sheet.</p>}
          </div>
      ) : (
        <>
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
                <td>{r.maximum === undefined ? `${r.spent} ${r.countsUp ? 'built up' : 'spent'} (tracked by hand)` : r.countsUp ? `${r.spent} built up of ${r.maximum}` : `${r.current} of ${r.maximum}`}</td>
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

      {includeDetails && printedDetails.length > 0 && (
        <div>
          <h4>Details</h4>
          <dl className="print-details">
            {printedDetails.map(([label, text]) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>{text}</dd>
              </div>
            ))}
          </dl>
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
                  {n.target.label || n.target.kind} ({n.status}): {n.text}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

        </>
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
      </div>
      </div>
    </section>
  );
}
