import type React from 'react'

/**
 * Shared helpers for rendering database-driven lookups (contact status, type, source, group)
 * in the colour an administrator chose in Setup.
 *
 * Before this existed the same alpha maths was written out three times — in StatusBadge, in
 * ContactsList's group pill, and inline in the Setup groups table — so a tweak to the badge
 * treatment had to be made in three places or they drifted apart.
 */

/** The shape every contact lookup endpoint returns: an immutable value, a label, a colour. */
export interface LookupItem {
  id: string
  name: string
  color?: string | null
}

export interface ResolvedLookup {
  name: string
  color?: string | null
}

/**
 * Indexes lookups by their stored value.
 *
 * The key is `id`, not `name`: contacts store the lookup's immutable `Value` (that is what the
 * endpoints return as `id`), which is precisely what makes renaming a status in Setup a
 * label-only change. Matching on the display name would break the moment someone renamed one.
 */
export const buildLookupMap = (items: readonly LookupItem[] | null | undefined): Map<string, ResolvedLookup> => {
  const map = new Map<string, ResolvedLookup>()
  for (const item of items ?? []) {
    if (!item?.id) continue
    map.set(item.id, { name: item.name, color: item.color })
  }
  return map
}

/**
 * Resolves a stored value to its label and colour.
 *
 * Falls back to the raw stored value as the label rather than rendering blank — a contact
 * carrying a value whose lookup row was deleted still shows something meaningful.
 */
export const resolveLookup = (
  map: Map<string, ResolvedLookup>,
  value: string | null | undefined
): ResolvedLookup => {
  if (!value) return { name: '' }
  return map.get(value) ?? { name: value }
}

/**
 * Badge styling derived from a single hex colour: the colour itself for text and border, and
 * low-opacity washes of it for the border and fill.
 *
 * Alpha suffixes rather than pre-mixed lighter colours, so the badge sits correctly on any card
 * background instead of assuming white.
 */
export const badgeStyleFor = (hex?: string | null): React.CSSProperties | undefined => {
  if (!hex) return undefined
  const normalized = hex.trim()
  return {
    color: normalized,
    borderColor: `${normalized}59`, // ~35% alpha
    backgroundColor: `${normalized}1F` // ~12% alpha
  }
}
