import type { RulesFamilyId } from '../api/types';

/** The two bundled rules families by the edition people know them by (the policies' own names are not loaded in the importer). */
export const familyLabel = (id: RulesFamilyId): string => (id === 'srd-5.1' ? '2014 rules (SRD 5.1)' : id === 'srd-5.2.1' ? '2024 rules (SRD 5.2.1)' : id);

/** Short form for a sentence: "Written for SRD 5.1". */
export const familyShortName = (id: RulesFamilyId): string => (id === 'srd-5.1' ? 'SRD 5.1' : id === 'srd-5.2.1' ? 'SRD 5.2.1' : id);
