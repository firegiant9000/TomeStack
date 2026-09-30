import { configure } from '@testing-library/react';

// Each step waits on a real DevHost command. Testing Library's 1 s default was too short on the hosted runner: CI
// failed at random steps on commits whose rerun passed (2026-09-29), with the status still showing the previous
// action. A real failure still fails; it only takes longer to report.
configure({ asyncUtilTimeout: 10_000 });
