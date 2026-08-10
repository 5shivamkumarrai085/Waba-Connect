// ── Permission catalogue ───────────────────────────────────────────────

export interface PermissionCapability {
  id: number
  key: string
  capability: string
  displayName: string
}

export interface PermissionFeature {
  feature: string
  displayName: string
  /** Only the capabilities that exist for this feature — the set is deliberately ragged. */
  capabilities: PermissionCapability[]
}

export interface PermissionGroup {
  name: string
  features: PermissionFeature[]
}

export interface PermissionCatalog {
  groups: PermissionGroup[]
}

// ── Users ──────────────────────────────────────────────────────────────

export interface SetupUserListItem {
  id: number
  firstName: string
  lastName?: string | null
  fullName: string
  email: string
  phoneNumber?: string | null
  profileImageUrl?: string | null
  roleId?: number | null
  roleName?: string | null
  isActive: boolean
  isVerified: boolean
  isAdministrator: boolean
  createdAt: string
  lastLoginAt?: string | null
}

export interface SetupUserDetail extends SetupUserListItem {
  dialCode?: string | null
  defaultLanguageCode?: string | null
  sendWelcomeEmail: boolean
  mustChangePassword: boolean
  usesCustomPermissions: boolean
  permissionKeys: string[]
}

export interface SetupUserPayload {
  firstName: string
  lastName?: string
  email: string
  phoneNumber?: string
  dialCode?: string
  profileImageUrl?: string
  defaultLanguageCode?: string
  /** Omit or leave blank on update to keep the existing password. */
  password?: string
  confirmPassword?: string
  isActive: boolean
  isVerified: boolean
  sendWelcomeEmail: boolean
  isAdministrator: boolean
  roleId?: number | null
  usesCustomPermissions: boolean
  permissionKeys: string[]
}

export interface UserDashboard {
  totalUsers: number
  activeUsers: number
  inactiveUsers: number
  administratorCount: number
}

// ── Roles ──────────────────────────────────────────────────────────────

export interface SetupRoleListItem {
  id: number
  name: string
  description?: string | null
  isSystem: boolean
  isAdministrator: boolean
  userCount: number
  permissionCount: number
  createdAt: string
}

export interface RoleUser {
  id: number
  fullName: string
  email: string
  isActive: boolean
}

export interface SetupRoleDetail extends SetupRoleListItem {
  permissionKeys: string[]
  users: RoleUser[]
}

export interface SetupRolePayload {
  name: string
  description?: string
  isAdministrator: boolean
  permissionKeys: string[]
}
