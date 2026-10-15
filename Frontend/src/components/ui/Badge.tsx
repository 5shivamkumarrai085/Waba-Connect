import React from 'react'
import './ui.css'

export type BadgeTone = 'neutral' | 'brand' | 'success' | 'warning' | 'danger' | 'info'

interface BadgeProps {
  children: React.ReactNode
  tone?: BadgeTone
  /**
   * A colour configured by an admin (e.g. a Setup › Status colour). Used for the dot and a soft
   * tint; the text stays the readable ink colour whatever the colour is.
   */
  color?: string | null
  /** Small leading dot, for statuses. */
  dot?: boolean
  className?: string
  title?: string
}

/** A small status or category label. Tone comes from tokens; an admin colour can override it. */
export const Badge: React.FC<BadgeProps> = ({ children, tone = 'neutral', color, dot, className, title }) => {
  const style = color ? ({ '--badge-color': color } as React.CSSProperties) : undefined
  return (
    <span
      className={`ui-badge ui-badge--${color ? 'custom' : tone}${className ? ` ${className}` : ''}`}
      style={style}
      title={title}
    >
      {dot && <span className="ui-badge-dot" aria-hidden="true" />}
      {children}
    </span>
  )
}
