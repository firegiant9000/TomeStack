import { Component, createRef, type ReactNode } from 'react';
import { DiagnosticsBox } from './DiagnosticsBox';

interface Props {
  children: ReactNode;
  /** Goes to the home screen (the character list); the boundary resets first so the next screen renders. */
  onHome?: () => void;
}

/**
 * Around the lazily loaded screens: if a screen throws or its code cannot be loaded (a chunk missing from the app folder, an
 * interrupted read) the screen says so in plain words, offers the diagnostics and a way back to the characters, and the rest of
 * the window (the navigation) stays usable, instead of the whole page going blank. The caller keys it by screen, so choosing
 * another screen starts it fresh. No text of the error is shown.
 */
export class ScreenBoundary extends Component<Props, { failed: boolean }> {
  state = { failed: false };
  private heading = createRef<HTMLHeadingElement>();

  static getDerivedStateFromError() {
    return { failed: true };
  }

  // WCAG 2.4.3: like the other screens, the fallback moves focus to its heading (on mount when the screen throws while first rendering).
  componentDidMount() {
    if (this.state.failed) this.heading.current?.focus();
  }

  componentDidUpdate(_previousProps: Props, previousState: { failed: boolean }) {
    if (this.state.failed && !previousState.failed) this.heading.current?.focus();
  }

  private back = () => {
    this.setState({ failed: false });
    this.props.onHome?.();
  };

  render() {
    if (!this.state.failed) return this.props.children;
    return (
      <section aria-labelledby="screen-error-heading">
        <h1 id="screen-error-heading" tabIndex={-1} ref={this.heading}>
          Something went wrong on this screen
        </h1>
        <p role="alert">Your saved characters are safe.</p>
        <p className="hint">If it keeps happening, copy the diagnostics for a bug report.</p>
        <DiagnosticsBox />
        <p>
          <button type="button" onClick={this.back}>
            Back to your characters
          </button>
        </p>
      </section>
    );
  }
}
