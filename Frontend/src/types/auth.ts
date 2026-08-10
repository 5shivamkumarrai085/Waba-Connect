export interface CurrentUser {
  id: number
  firstName: string
  lastName?: string | null
  fullName: string
  email: string
  phoneNumber?: string | null
  profileImageUrl?: string | null
  defaultLanguageCode?: string | null
  roleId?: number | null
  roleName?: string | null
  /** Bypasses every permission check. When true, `permissions` is empty by design. */
  isAdministrator: boolean
  mustChangePassword: boolean
  isVerified: boolean
  /** Flat permission keys, e.g. "Contact.Create". Empty for administrators. */
  permissions: string[]
}

export interface LoginPayload {
  email: string
  password: string
}

export interface LoginResult {
  token: string
  expiresAt: string
  user: CurrentUser
}

export interface ChangePasswordPayload {
  currentPassword: string
  newPassword: string
  confirmPassword: string
}
