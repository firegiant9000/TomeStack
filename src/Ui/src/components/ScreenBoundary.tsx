import { Component, type ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

/**
 * Around the lazily loaded screens: if a screen's code cannot be loaded (a chunk missing from the app folder, an interrupted read)
 * the screen says so in plain words and the rest of the window (the navigation) stays usable, instead of the whole page going
 * blank. The caller keys it by screen, so choosing another screen starts it fresh. No text of the error is shown.
 */
export class ScreenBoundary extends Component<Props, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? <p role="alert">This screen couldn't be loaded. Choose another screen, or restart TomeStack.</p> : this.props.children;
  }
}
