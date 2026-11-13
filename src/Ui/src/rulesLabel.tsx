import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { RulesFamilyPolicy } from './api/types';

// The words a player reads for a rules family come from the service (`shortLabel`), never from a map kept in the UI.
// Lookup order: shortLabel, then displayName, then the id itself (before app.info arrives, or for a family this build does not know).
type RulesLabel = (id: string) => string;

const idOnly: RulesLabel = (id) => id;
const RulesLabelContext = createContext<RulesLabel>(idOnly);

export function RulesLabelProvider({ families, children }: { families: readonly RulesFamilyPolicy[] | undefined; children: ReactNode }) {
  const label = useMemo<RulesLabel>(
    () => (id) => {
      const policy = families?.find((f) => f.id === id);
      return policy?.shortLabel || policy?.displayName || id;
    },
    [families],
  );
  return <RulesLabelContext.Provider value={label}>{children}</RulesLabelContext.Provider>;
}

export function useRulesLabel(): RulesLabel {
  return useContext(RulesLabelContext);
}
