import React from 'react'
import { AlertTriangle, RotateCw } from 'lucide-react'
import { Button } from './Button'
import './ui.css'

interface ErrorStateProps {
  /** What could not be done, in the reader's words: "Contacts could not be loaded." */
  title: string
  /** The server's explanation, when there is a useful one. */
  detail?: string | null
  onRetry?: () => void
  retrying?: boolean
  compact?: boolean
}

/**
 * Shown instead of a list or panel whose data failed to load. Without it a failed request looked
 * like "no records", which is worse than an error: it is wrong.
 */
export const ErrorState: React.FC<ErrorStateProps> = ({ title, detail, onRetry, retrying, compact }) => (
  <div className={`ui-error-state${compact ? ' ui-error-state--compact' : ''}`} role="alert">
    <AlertTriangle size={compact ? 18 : 28} aria-hidden="true" className="ui-error-icon" />
    <div className="ui-error-text">
      <p className="ui-error-title">{title}</p>
      {detail && <p className="ui-error-detail">{detail}</p>}
    </div>
    {onRetry && (
      <Button size="sm" onClick={onRetry} loading={retrying} icon={<RotateCw size={14} aria-hidden="true" />}>
        Try again
      </Button>
    )}
  </div>
)
