import React from 'react'
import { Check } from 'lucide-react'
import { badgeStyleFor } from '../../utils/lookupColors'
import './StatusBadge.css'

interface StatusBadgeProps {
  type: 'verified' | 'stale' | 'success' | 'warning' | string
  text?: string
  /**
   * Hex colour from the lookup table, e.g. "#22C55E".
   *
   * When present it drives the badge inline and the class lookup is bypassed entirely. That
   * matters for two reasons: an admin-created status has no CSS class at all and used to render
   * completely unstyled, and built-in values whose class name doesn't survive lowercasing —
   * "InProgress" becomes "inprogress", while the stylesheet defines ".in-progress" — matched
   * nothing either.
   *
   * Omitting it keeps the original class behaviour, so every existing caller is untouched.
   */
  color?: string | null
}


export const StatusBadge: React.FC<StatusBadgeProps> = ({ type, text, color }) => {
  const badgeClass = type.toLowerCase()
  const displayVal = text || type
  const inlineStyle = badgeStyleFor(color)

  const renderIcon = () => {
    if (badgeClass === 'verified') {
      return (
        <span className="status-badge-icon">
          <Check size={12} strokeWidth={3} />
        </span>
      )
    }
    return null
  }

  return (
    // The class still goes on when a colour is supplied — it carries the shared shape (padding,
    // radius, font) — but the inline style wins on the three colour properties.
    <span className={`status-badge ${badgeClass}`} style={inlineStyle}>
      {renderIcon()}
      <span>{displayVal}</span>
    </span>
  )
}
