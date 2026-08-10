import { useMemo } from 'react'
import useAuthStore from '../store/authStore'

export interface UsePermissionResult {
  /** Whether the signed-in user holds the given permission key, e.g. "Contact.Create". */
  has: (permissionKey: string) => boolean
  /** Whether they hold at least one of the given keys. */
  hasAny: (permissionKeys: string[]) => boolean
  /** Whether they hold every one of the given keys. */
  hasAll: (permissionKeys: string[]) => boolean
  isAdministrator: boolean
  isAuthenticated: boolean
}

/**
 * Reads the signed-in user's effective permissions.
 *
 * This drives presentation only — hiding a button is a courtesy, not a security boundary.
 * The backend enforces the same keys independently via [RequiresPermission], so a user who
 * forges a permission client-side still gets a 403.
 */
export const usePermission = (): UsePermissionResult => {
  const user = useAuthStore((state) => state.user)

  return useMemo(() => {
    const isAdministrator = user?.isAdministrator ?? false
    // A Set because permission checks run per row on list pages, where a linear scan of ~90
    // keys per cell adds up.
    const granted = new Set(user?.permissions ?? [])

    // Administrators carry an empty permission list by design — the flag is the grant, so
    // that new permissions added later reach them without a re-seed.
    const has = (permissionKey: string) => isAdministrator || granted.has(permissionKey)

    return {
      has,
      hasAny: (keys: string[]) => isAdministrator || keys.some((key) => granted.has(key)),
      hasAll: (keys: string[]) => isAdministrator || keys.every((key) => granted.has(key)),
      isAdministrator,
      isAuthenticated: !!user
    }
  }, [user])
}

export default usePermission
