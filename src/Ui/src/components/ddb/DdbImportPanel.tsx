import { useEffect, useRef, useState } from 'react';
import { client } from '../../api/client';
import { TomeStackError } from '../../api/transport';
import type {
  ChoiceSelection,
  DdbApplyResult,
  DdbPreview,
  DdbPreviewRequest,
  DdbReadResult,
  NumberAction,
  Resolution,
  RulesFamilyId,
  RulesFamilyPolicy,
} from '../../api/types';
import { DdbChooseStep } from './DdbChooseStep';
import { DdbFamilyStep } from './DdbFamilyStep';
import { DdbMatchesStep } from './DdbMatchesStep';
import { DdbNumbersStep } from './DdbNumbersStep';
import { DdbSummaryStep } from './DdbSummaryStep';

interface Props {
  rulesFamilies: RulesFamilyPolicy[];
  onError: (error: unknown) => void;
  /** Cancel at any step: the read sheet is discarded and nothing was written (SPEC C-07). */
  onCancel: () => void;
  /** May be async: the panel waits for it, and keeps Create disabled once the character is saved. */
  onCreated: (characterId: string, result: DdbApplyResult) => Promise<void> | void;
}

type Step = 1 | 2 | 3 | 4 | 5;

const titles: Record<Step, string> = { 1: 'Choose the sheet', 2: 'Rules and campaign', 3: 'Matches', 4: 'Numbers', 5: 'Create' };

/**
 * Character import from a D&D Beyond PDF sheet (features/ddb-pdf-import.md "Flow"): a step view, not a modal. Every step
 * can go back; Cancel at any step sends `ddb.discard` and writes nothing. Only "Create character" writes. Each step is a
 * labelled region, and focus moves to its heading on every step change.
 */
export function DdbImportPanel({ rulesFamilies, onError, onCancel, onCreated }: Props) {
  const [step, setStep] = useState<Step>(1);
  const [read, setRead] = useState<DdbReadResult>();
  const [family, setFamily] = useState<RulesFamilyId>();
  const [campaignId, setCampaignId] = useState<string>();
  const [resolutions, setResolutions] = useState<Record<string, Resolution>>({});
  const [numbers, setNumbers] = useState<Record<string, NumberAction>>({});
  const [answers, setAnswers] = useState<ChoiceSelection[]>([]);
  const [includePlayState, setIncludePlayState] = useState(false);
  const [preview, setPreview] = useState<DdbPreview>();
  /** The request the shown preview answers; while it differs from the current one, a newer preview is on its way. */
  const [previewFor, setPreviewFor] = useState<string>();
  /** A preview that failed, keyed to the request it answered; it ends the loading state and offers "Try again". */
  const [previewError, setPreviewError] = useState<{ key: string; message: string }>();
  const [retry, setRetry] = useState(0);
  /** Why the user was sent back to step 1 (the sheet was spent, discarded or expired). */
  const [sheetError, setSheetError] = useState<string>();
  const [applying, setApplying] = useState(false);
  /** Set once the character is saved: Create never enables again, so a second click cannot save a duplicate. */
  const [created, setCreated] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  /** The token to discard if the panel goes away before "Create character" spent it. */
  const live = useRef<string | undefined>(undefined);
  /** False once the panel is cancelled or unmounted: a read still in flight then discards its own token. */
  const open = useRef(true);

  useEffect(() => {
    heading.current?.focus();
  }, [step]);

  useEffect(() => {
    open.current = true;
    return () => {
      open.current = false;
      if (live.current) void client.ddbDiscard(live.current).catch(() => undefined);
    };
  }, []);

  const request: DdbPreviewRequest | undefined =
    read && family
      ? {
          token: read.token,
          rulesFamily: family,
          campaignId,
          resolutions: Object.values(resolutions),
          numberChoices: Object.entries(numbers).map(([field, action]) => ({ field, action })),
          includePlayState,
          answers,
        }
      : undefined;
  const requestKey = JSON.stringify(request ?? null);

  // The preview follows every answer from step 3 on; a newer answer wins over a slower older reply.
  useEffect(() => {
    if (step < 3 || !request) return;
    let current = true;
    client
      .ddbPreview(request)
      .then((next) => {
        if (!current) return;
        setPreview(next);
        setPreviewFor(requestKey);
        setPreviewError(undefined);
      })
      .catch((error: unknown) => {
        if (!current) return;
        if (error instanceof TomeStackError && error.code === 'ddb.token-invalid') {
          live.current = undefined; // the service no longer holds it
          setSheetError(error.message);
          setRead(undefined);
          setPreview(undefined);
          setPreviewFor(undefined);
          setPreviewError(undefined);
          setStep(1);
          return;
        }
        setPreviewError({ key: requestKey, message: error instanceof Error ? error.message : 'The preview could not be built.' });
      });
    return () => {
      current = false;
    };
    // requestKey stands for the request's content.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [step, requestKey, retry]);
  const failed = previewError?.key === requestKey ? previewError : undefined;
  const loading = step >= 3 && request !== undefined && previewFor !== requestKey && !failed;
  const busy = loading || applying;

  function onRead(result: DdbReadResult) {
    if (!open.current) {
      void client.ddbDiscard(result.token).catch(() => undefined); // cancelled while the sheet was being read
      return;
    }
    setSheetError(undefined);
    if (live.current && live.current !== result.token) void client.ddbDiscard(live.current).catch(() => undefined);
    live.current = result.token;
    setRead(result);
    setFamily((current) => current ?? result.suggestedFamily ?? rulesFamilies[0]?.id);
    setResolutions({});
    setNumbers({});
    setAnswers([]);
    setPreview(undefined);
    setPreviewFor(undefined);
  }

  async function cancel() {
    open.current = false;
    const token = live.current;
    live.current = undefined;
    if (token) await client.ddbDiscard(token).catch(() => undefined);
    onCancel();
  }

  async function create() {
    if (!request || created || applying) return;
    try {
      setApplying(true);
      const result = await client.ddbApply(request);
      live.current = undefined; // spent
      setCreated(true);
      await onCreated(result.characterId, result);
    } catch (error) {
      onError(error);
    } finally {
      setApplying(false);
    }
  }

  const back = step > 1 ? () => setStep((s) => (s - 1) as Step) : undefined;
  const headingId = 'ddb-step-heading';

  return (
    <section className="ddb-import" aria-labelledby="ddb-import-title">
      <h2 id="ddb-import-title" className="visually-hidden">
        Import from D&amp;D Beyond PDF
      </h2>
      <p className="hint">
        Step {step} of 5. Nothing is saved until you choose “Create character”. The PDF is read once and not kept.
      </p>
      <section aria-labelledby={headingId}>
        <h3 id={headingId} ref={heading} tabIndex={-1}>
          {titles[step]}
        </h3>
        {step === 1 && sheetError && <p role="alert" className="warn">{sheetError}</p>}
        {step >= 3 && failed && (
          <div role="alert" className="warn">
            <p>{failed.message}</p>
            <button type="button" onClick={() => {
                setPreviewError(undefined);
                setRetry((n) => n + 1);
              }}>
              Try again
            </button>
          </div>
        )}
        {step === 1 && <DdbChooseStep read={read} onRead={onRead} onError={onError} />}
        {step === 2 && read && family && (
          <DdbFamilyStep
            rulesFamilies={rulesFamilies}
            suggested={read.suggestedFamily}
            family={family}
            campaignId={campaignId}
            onFamily={(next) => {
              setFamily(next);
              setCampaignId(undefined); // campaigns belong to one family; the select only lists the new family's
              setResolutions({});
              setAnswers([]);
              setPreview(undefined);
              setPreviewFor(undefined);
            }}
            onCampaign={(next) => {
              setCampaignId(next);
              setPreview(undefined);
              setPreviewFor(undefined);
            }}
            onError={onError}
          />
        )}
        {step === 3 && family && (
          <DdbMatchesStep
            preview={preview}
            family={family}
            campaignId={campaignId}
            resolutions={resolutions}
            answers={answers}
            onResolve={(resolution) => setResolutions((all) => ({ ...all, [resolution.rowId]: resolution }))}
            onClear={(rowId) =>
              setResolutions((all) => {
                const next = { ...all };
                delete next[rowId];
                return next;
              })
            }
            onAnswer={(answer) => setAnswers((all) => [...all.filter((a) => !(a.choiceId === answer.choiceId && a.source.contentId === answer.source.contentId)), answer])}
            onError={onError}
          />
        )}
        {step === 4 && <DdbNumbersStep preview={preview} numbers={numbers} onChange={(field, action) => setNumbers((all) => ({ ...all, [field]: action }))} />}
        {step === 5 && (
          <DdbSummaryStep preview={preview} family={family} suggested={read?.suggestedFamily} includePlayState={includePlayState} onIncludePlayState={setIncludePlayState} />
        )}
      </section>
      <p aria-live="polite" className="hint">
        {busy ? 'Working…' : ''}
      </p>
      <div className="actions">
        {back && (
          <button type="button" onClick={back}>
            Back
          </button>
        )}
        {step < 5 && (
          <button type="button" disabled={step === 1 ? !read : step > 2 && (!preview || loading)} onClick={() => setStep((s) => (s + 1) as Step)}>
            {step === 1 ? 'Next: rules' : step === 2 ? 'Next: matches' : step === 3 ? 'Next: numbers' : 'Next: summary'}
          </button>
        )}
        {step === 5 && (
          <button type="button" disabled={busy || created || !preview?.canApply} onClick={create}>
            Create character
          </button>
        )}
        <button type="button" onClick={cancel}>
          Cancel
        </button>
      </div>
    </section>
  );
}
