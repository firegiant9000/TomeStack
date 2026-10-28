import { useEffect, useId } from 'react';
import type { Effect, EffectPayment } from '../api/types';

type Payer = Extract<Effect, { type: 'roll' | 'attack' | 'toggle' }>;
type ReportProblem = (field: string, problem: string | undefined) => void;

const kinds = ['resource', 'hitPoints', 'slot'] as const;

/**
 * MECH-X item 1 (R31, R33): the effect with its payment changed. A pool payment is no `payment` key at all (never `kind: resource`),
 * so an unchanged pool effect still publishes at its old version. Hit points and a slot take no pool, and a slot takes no cost either.
 * Everything else the effect carries is kept.
 */
export function withPayment<T extends Payer>(effect: T, kind: 'resource' | 'hitPoints' | 'slot'): T {
  const next: Record<string, unknown> = { ...effect };
  if (kind === 'resource') {
    delete next.payment;
    return next as T;
  }
  for (const key of ['resourceId', 'resourceContent', 'drawsDown', 'alsoChange', 'variableCost']) delete next[key];
  if (kind === 'slot') for (const key of ['cost', 'perUnit']) delete next[key];
  const kept: EffectPayment = { ...(effect.payment ?? { kind }), kind };
  if (kind === 'hitPoints') delete kept.minSlotLevel;
  else delete kept.mayDropToZero;
  next.payment = kept;
  return next as T;
}

/**
 * The "Pays with" choice shared by the roll, attack and toggle editors. `slotRefusal` is a reason a slot cannot be chosen (a roll
 * that casts a spell without a slot), shown beside the field.
 */
export function PaymentFields(props: { effect: Payer; onChange: (effect: Effect) => void; onProblem: ReportProblem; slotRefusal?: string }) {
  const { effect, onChange } = props;
  const payment = effect.payment;
  const known = payment == null ||(kinds as readonly string[]).includes(payment.kind);
  const reasonId = useId();
  const { onProblem } = props;
  const problem = known ? undefined : "Pays with: this payment doesn't read.";
  // A stored payment of an unknown kind blocks saving while it is there (also on load), and is cleared when the field goes away.
  useEffect(() => {
    onProblem(`${effect.id}-payment`, problem);
    return () => onProblem(`${effect.id}-payment`, undefined);
  }, [effect.id, problem, onProblem]);
  if (!known) {
    return <p className="error">This payment doesn&apos;t read. It is kept as it is; remove this rule to get rid of it.</p>;
  }
  const kind = payment?.kind ?? 'resource';
  const refused = props.slotRefusal !== undefined && kind !== 'slot';
  return (
    <>
      <label className="field">
        Pays with
        <select
          value={kind}
          aria-describedby={refused ? reasonId : undefined}
          onChange={(e) => onChange(withPayment(effect, e.target.value === 'hitPoints' ? 'hitPoints' : e.target.value === 'slot' ? 'slot' : 'resource'))}
        >
          <option value="resource">A pool</option>
          <option value="hitPoints">Hit points</option>
          <option value="slot" disabled={refused}>
            A spell slot
          </option>
        </select>
      </label>
      {refused && (
        <p id={reasonId} className="hint">
          {props.slotRefusal}
        </p>
      )}
      {payment?.kind === 'hitPoints' && (
        <label className="choice">
          <input
            type="checkbox"
            checked={payment.mayDropToZero === true}
            onChange={(e) => {
              // Unticking removes only this key, so any other key the payment carries is kept.
              const next: typeof payment = { ...payment, mayDropToZero: true };
              if (!e.target.checked) delete next.mayDropToZero;
              onChange({ ...effect, payment: next });
            }}
          />
          May drop to 0 hit points
        </label>
      )}
      {payment?.kind === 'slot' && (
        <label className="field">
          Lowest slot level
          <select
            value={payment.minSlotLevel === undefined || payment.minSlotLevel === 1 ? '' : String(payment.minSlotLevel)}
            onChange={(e) => {
              // Blank (level 1) removes only this key, so any other key the payment carries is kept.
              const next: typeof payment = { ...payment, minSlotLevel: Number(e.target.value) };
              if (e.target.value === '') delete next.minSlotLevel;
              onChange({ ...effect, payment: next });
            }}
          >
            <option value="">1</option>
            {[2, 3, 4, 5, 6, 7, 8, 9].map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
      )}
    </>
  );
}
