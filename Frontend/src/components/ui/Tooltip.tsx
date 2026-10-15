import React, { useId } from 'react'
import './ui.css'

interface TooltipProps {
  content: string
  children: React.ReactElement<{ 'aria-describedby'?: string }>
  side?: 'top' | 'bottom'
}

/**
 * A short hint shown on hover and on keyboard focus. The hint is linked to the trigger with
 * aria-describedby, so screen readers announce it too. CSS-only: no timers, no portal.
 */
export const Tooltip: React.FC<TooltipProps> = ({ content, children, side = 'top' }) => {
  const id = useId()
  return (
    <span className={`ui-tooltip ui-tooltip--${side}`}>
      {React.cloneElement(children, { 'aria-describedby': id })}
      <span role="tooltip" id={id} className="ui-tooltip-bubble">{content}</span>
    </span>
  )
}
