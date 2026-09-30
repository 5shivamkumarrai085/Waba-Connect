import React from 'react'
import { AlertTriangle, RotateCcw } from 'lucide-react'

interface Props {
  children: React.ReactNode
  /** Short name of the area, used in the message ("This section" when omitted). */
  area?: string
}

interface State {
  error: Error | null
}

/**
 * Contains a rendering error to the part of the screen that failed.
 *
 * Without one, any exception thrown while rendering unmounts the whole React tree and the user is
 * left with a blank page and no way back except a reload. With it, the sidebar and header stay
 * usable, the failed area offers a retry, and navigating elsewhere recovers on its own (the shell
 * keys this boundary by route).
 */
export class ErrorBoundary extends React.Component<Props, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    // Kept to the console: the details are for developers, the screen shows a plain message.
    console.error('Rendering error contained by ErrorBoundary', error, info.componentStack)
  }

  private reset = () => this.setState({ error: null })

  render() {
    if (!this.state.error) return this.props.children

    return (
      <div className="error-boundary" role="alert">
        <div className="error-boundary-icon">
          <AlertTriangle size={28} />
        </div>
        <h2 className="error-boundary-title">{this.props.area ?? 'This section'} could not be displayed</h2>
        <p className="error-boundary-text">
          Something went wrong while showing this page. The rest of the application still works.
          Try again, or go to another page and come back.
        </p>
        <button type="button" className="btn-primary error-boundary-retry" onClick={this.reset}>
          <RotateCcw size={16} /> Try again
        </button>
      </div>
    )
  }
}

export default ErrorBoundary
