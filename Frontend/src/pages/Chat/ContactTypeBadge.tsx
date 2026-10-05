import React from 'react'
import { resolveLookup, badgeStyleFor, type ResolvedLookup } from '../../utils/lookupColors'

/**
 * A contact's type, using the label and colour configured in Setup → Type. The value arriving
 * from the API is the type's stored Value, which is exactly the key the lookup map is built on.
 */
export const ContactTypeBadge: React.FC<{
  value?: string | null
  typeMap: Map<string, ResolvedLookup>
}> = ({ value, typeMap }) => {
  if (!value) return null
  const resolved = resolveLookup(typeMap, value)
  return (
    <span className="conversation-status-badge" style={badgeStyleFor(resolved.color)}>
      {resolved.name}
    </span>
  )
}
