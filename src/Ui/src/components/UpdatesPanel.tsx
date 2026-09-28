import { useEffect, useState } from 'react';
import { client } from '../api/client';
import type { CharacterView, UpdateOffer } from '../api/types';
import { UpdateReviewPanel } from './UpdateReviewPanel';

interface Props {
  view: CharacterView;
  onChanged: (view: CharacterView) => void;
  onError: (error: unknown) => void;
  onStatus: (text: string) => void;
}

/**
 * M3 C7 (SPEC I-06): newer revisions of content this character uses, from a bundled pack or the user's own sources.
 * Each is an offer: "Review update" shows what would change, and only "Apply update" there changes the character.
 */
export function UpdatesPanel({ view, onChanged, onError, onStatus }: Props) {
  const { character } = view;
  const [offers, setOffers] = useState<UpdateOffer[]>([]);
  const [reviewing, setReviewing] = useState<UpdateOffer>();

  useEffect(() => {
    let current = true;
    client
      .availableUpdates(character.id)
      .then((found) => {
        if (current) setOffers(found);
      })
      .catch(onError);
    return () => {
      current = false;
    };
  }, [character, onError]);

  if (offers.length === 0) return null;
  return (
    <section aria-labelledby="updates-heading" className="play-panel">
      <h3 id="updates-heading">Updates available</h3>
      <p className="hint">Newer revisions of content this character uses. Nothing changes until you review an update and apply it.</p>
      {reviewing ? (
        <UpdateReviewPanel
          key={`${reviewing.from.revisionId}-${reviewing.to.revisionId}`}
          characterId={character.id}
          characterName={character.name}
          contentName={reviewing.name}
          from={reviewing.from}
          to={reviewing.to}
          onError={onError}
          onCancel={() => setReviewing(undefined)}
          onApplied={(updated) => {
            setReviewing(undefined);
            onChanged(updated);
            onStatus(`Updated ${character.name}: ${reviewing.name}.`);
          }}
        />
      ) : (
        <ul className="resources">
          {offers.map((offer) => (
            <li key={offer.from.revisionId} className="resource">
              {offer.name} <span className="tag">{offer.bundled ? 'bundled' : 'your source'}</span> <span className="hint">{offer.sourceTitle}</span>{' '}
              <button type="button" onClick={() => setReviewing(offer)}>
                Review update: {offer.name}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
