import type { SearchableValue } from './smartSearch'

/**
 * Everything about a contact that the search box should look at.
 *
 * <para>
 * Lives apart from either caller because there are two: the Contacts table filters the rows on
 * screen, and `contactStore.toggleAllRowSelection` re-derives the same set to decide what the
 * header checkbox selects. Those two had drifted — the table matched five fields, select-all
 * matched three — so ticking the header box while a search was active could select rows that were
 * not visible, or miss rows that were. One list, used by both, is what stops that recurring.
 * </para>
 * <para>
 * `name` is passed in rather than read off the contact: the two call sites derive the display name
 * slightly differently, and the search should match whatever the row actually shows.
 * </para>
 */
export const contactFields = (
  contact: Record<string, unknown>,
  displayName: string
): SearchableValue[] => {
  const value = (key: string): SearchableValue => {
    const raw = contact[key]
    return typeof raw === 'string' || typeof raw === 'number' ? raw : undefined
  }

  // The owner arrives either as a nested object or as a plain string, depending on the endpoint.
  const assigned = contact.assignedUser
  const assignedName =
    assigned && typeof assigned === 'object' && 'name' in assigned
      ? String((assigned as { name?: unknown }).name ?? '')
      : undefined

  return [
    displayName,
    value('phone'),
    value('email'),
    value('type'),
    value('status'),
    value('source'),
    value('company'),
    value('groups'),
    value('tags'),
    value('assignedTo'),
    assignedName,
    value('city'),
    value('state'),
    value('country'),
    value('website'),
    value('language')
  ]
}
