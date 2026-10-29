// Runs before every unit-test file. userEvent waits one macrotask (setTimeout 0) after every action by default; with
// 400+ setups across the suite that hop is a large share of the run time and none of these tests depend on it
// (they await findBy*/waitFor for anything asynchronous). Pass { delay: 0 } explicitly to restore the default hop.
import userEvent from '@testing-library/user-event';

const setup = userEvent.setup.bind(userEvent);
(userEvent as { setup: typeof setup }).setup = (options?: Parameters<typeof setup>[0]) => setup({ delay: null, ...options });
