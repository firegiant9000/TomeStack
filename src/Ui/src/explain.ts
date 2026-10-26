import type { RollMode, RollRecord, TraceEntry, TraceOrigin } from './api/types';

/** `'attack'` for an attack trace, whose `result` is the running to-hit, not the number the step is about. */
export type StepContext = 'value' | 'attack';

const RIDER_DAMAGE_SUFFIX = 'damage (attack rider)';
const ATTACK_ROLL_IDS = new Set(['weapon.attack', 'weapon.damage', 'attack.attack', 'attack.damage']);

function signed(n: number): string {
  return n < 0 ? `${n}` : `+${n}`;
}

/** Who a step came from, in words; empty when there is nothing honest to say. */
function origin(o: TraceOrigin): string {
  switch (o.kind) {
    case 'content':
      return o.contentName ?? o.sourceTitle ?? '';
    case 'characterChoice':
      return 'your choice';
    case 'rulesPolicy':
      return 'the rules';
    default:
      return '';
  }
}

function isRiderDice(step: TraceEntry): boolean {
  return step.operation === 'add' && step.amount === undefined && step.description.endsWith(RIDER_DAMAGE_SUFFIX);
}

function isDamageStep(step: TraceEntry): boolean {
  if (step.operation === 'add' && step.description.endsWith(RIDER_DAMAGE_SUFFIX)) return true;
  return step.operation === 'set' && step.amount === undefined && step.description.startsWith('Damage ');
}

function explainStep(step: TraceEntry, context: StepContext): string {
  const { operation, description, amount, result } = step;
  switch (operation) {
    case 'base':
      return amount !== undefined ? `Starts at ${amount}: ${description}` : description;
    case 'add': {
      if (amount === undefined) return description;
      const sign = signed(amount);
      if (description.includes(sign)) return description;
      const from = origin(step.origin);
      const cited = from !== '' && !description.toLowerCase().includes(from.toLowerCase());
      return `${sign} ${description}${cited ? ` (from ${from})` : ''}`;
    }
    case 'derive':
      return `${description}: ${result}`;
    case 'set':
    case 'replace':
      if (amount !== undefined) return `Set to ${amount}: ${description}`;
      return context === 'attack' ? description : `${description}: ${result}`;
    case 'override':
      return `Your override: ${result}`;
    case 'ignored':
    case 'rider.not-applied':
      return `Not counted: ${description}`;
    default:
      return description;
  }
}

/** One sentence per step, in order. */
export function explainSteps(steps: TraceEntry[], context: StepContext = 'value'): string[] {
  return steps.map((s) => explainStep(s, context));
}

/** An attack trace holds the to-hit and the damage steps together; this keeps the part the roll is about. */
export function attackSteps(steps: TraceEntry[], part: 'toHit' | 'damage'): TraceEntry[] {
  return part === 'damage' ? steps.filter((s) => s.operation === 'base' || isDamageStep(s)) : steps.filter((s) => !isDamageStep(s));
}

const capitalise = (mode: string) => mode.charAt(0).toUpperCase() + mode.slice(1);
const opposite = (mode: RollMode): RollMode => (mode === 'advantage' ? 'disadvantage' : 'advantage');

/** The longer sentence behind the headline's mode wording; undefined for a plain normal roll. */
export function explainMode(record: RollRecord): string | undefined {
  const { mode, automaticMode } = record;
  const sources = (record.modeSources ?? []).join(', ') || 'the sheet';
  if (!automaticMode) return mode === 'normal' ? undefined : `${capitalise(mode)}: your choice`;
  if (automaticMode === 'normal') {
    return mode === 'normal'
      ? `Normal (cancelled): advantage and disadvantage from ${sources}`
      : `${capitalise(mode)}: your choice; advantage and disadvantage from ${sources} cancel`;
  }
  if (automaticMode === mode) return `${capitalise(mode)} from ${sources}`;
  if (mode === 'normal') return `Normal (cancelled): ${automaticMode} from ${sources}; your choice: ${opposite(automaticMode)}`;
  return `${capitalise(mode)}: your choice; ${automaticMode} from ${sources}`;
}

/** `steps` are the steps `explainRoll` shows. */
export function explainCritical(record: RollRecord, steps: TraceEntry[]): string | undefined {
  if (!record.critical) return undefined;
  return steps.some(isRiderDice)
    ? 'Critical hit: all dice are doubled, rider dice included; flat bonuses are not.'
    : 'Critical hit: the dice are doubled; flat bonuses are not.';
}

/** Mode sentence, then one sentence per step, then the critical sentence; empty when there is nothing to say. */
export function explainRoll(record: RollRecord): string[] {
  const lines: string[] = [];
  const mode = explainMode(record);
  if (mode) lines.push(mode);

  let steps: TraceEntry[] = [];
  if (record.explain) {
    const rollId = record.provenance?.rollId ?? '';
    if (ATTACK_ROLL_IDS.has(rollId)) {
      steps = attackSteps(record.explain, rollId.endsWith('.damage') ? 'damage' : 'toHit');
      lines.push(...explainSteps(steps, 'attack'));
    } else {
      steps = record.explain;
      lines.push(...explainSteps(steps));
    }
  } else {
    lines.push(...record.modifiers.map((m) => `${m.label}: ${signed(m.amount)}`));
  }

  const critical = explainCritical(record, steps);
  if (critical) lines.push(critical);
  return lines;
}
