import React from 'react'
import usePermission from '../../hooks/usePermission'
import NotAuthorized from '../NotAuthorized'

type CanMode = 'hide' | 'disable' | 'page'

interface CanProps {
  /** Single permission key, e.g. "User.Create". */
  permission?: string
  /** Any-of list. Use instead of `permission` when several keys grant the same UI. */
  anyOf?: string[]
  /** All-of list, for UI that genuinely needs every one. */
  allOf?: string[]
  /**
   * - `hide` (default): render nothing.
   * - `disable`: render the child disabled, with a tooltip explaining why.
   * - `page`: render the full "not authorized" screen.
   */
  mode?: CanMode
  /** Shown instead of the children when denied. Ignored in `disable` mode. */
  fallback?: React.ReactNode
  children: React.ReactNode
}

/**
 * Conditionally renders UI based on the signed-in user's permissions.
 *
 * <Can permission="User.Create"><button>New user</button></Can>
 * <Can permission="Contact.Delete" mode="disable"><MenuItem>Delete</MenuItem></Can>
 * <Can permission="Setup.View" mode="page"><SetupLayout /></Can>
 *
 * Prefer `mode="disable"` inside row-action menus: hiding items changes the menu's height
 * from row to row, which reads as a rendering bug rather than an intentional restriction.
 */
export const Can: React.FC<CanProps> = ({
  permission,
  anyOf,
  allOf,
  mode = 'hide',
  fallback = null,
  children
}) => {
  const { has, hasAny, hasAll } = usePermission()

  let allowed = true
  if (permission) allowed = has(permission)
  else if (anyOf?.length) allowed = hasAny(anyOf)
  else if (allOf?.length) allowed = hasAll(allOf)

  if (allowed) return <>{children}</>

  if (mode === 'page') {
    return <NotAuthorized requiredPermission={permission ?? anyOf?.[0] ?? allOf?.[0]} />
  }

  if (mode === 'disable' && React.isValidElement(children)) {
    // Clone rather than wrap: wrapping in a <span> would break the [data-menu-item] lookup
    // the Menu primitive uses to find its focusable items.
    const child = children as React.ReactElement<Record<string, unknown>>
    return React.cloneElement(child, {
      disabled: true,
      title: 'You do not have permission to do this',
      'aria-disabled': true
    })
  }

  return <>{fallback}</>
}

export default Can
