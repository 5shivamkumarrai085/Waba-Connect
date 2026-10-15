import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { KeyRound, LogOut } from 'lucide-react'
import { Menu, MenuItem, MenuSeparator, MenuLabel } from './Menu/Menu'
import useAuthStore from '../store/authStore'
import { resolveMediaUrl } from '../utils/mediaUrl'

/**
 * The signed-in user's avatar and menu (profile, change password, sign out). Shared by the
 * top header and by full-screen pages that hide the header, so signing out is always one click
 * away.
 */
export const AccountMenu: React.FC<{ size?: 'md' | 'sm' }> = ({ size = 'md' }) => {
  const navigate = useNavigate()
  const user = useAuthStore((state) => state.user)
  const logout = useAuthStore((state) => state.logout)
  const [open, setOpen] = useState(false)

  if (!user) return null

  const initials =
    `${user.firstName?.[0] ?? ''}${user.lastName?.[0] ?? ''}`.toUpperCase() || user.email[0].toUpperCase()

  const handleLogout = async () => {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <Menu
      open={open}
      onOpenChange={setOpen}
      align="end"
      offset={8}
      ariaLabel="Account menu"
      trigger={(props) => (
        <button
          {...props}
          type="button"
          className={`header-avatar-btn${size === 'sm' ? ' header-avatar-btn--sm' : ''}`}
          aria-label={`Account menu for ${user.fullName}`}
        >
          {user.profileImageUrl
            ? <img src={resolveMediaUrl(user.profileImageUrl)} alt="" className="header-avatar-img" />
            : <span className="header-avatar-initials">{initials}</span>}
        </button>
      )}
    >
      <MenuLabel>
        <span className="header-account-name">{user.fullName}</span>
        <span className="header-account-email">{user.email}</span>
        {user.roleName && <span className="header-account-role">{user.roleName}</span>}
      </MenuLabel>
      <MenuSeparator />
      <MenuItem onSelect={() => navigate('/change-password')}>
        <KeyRound size={15} />
        <span>Change password</span>
      </MenuItem>
      <MenuItem destructive onSelect={handleLogout}>
        <LogOut size={15} />
        <span>Sign out</span>
      </MenuItem>
    </Menu>
  )
}

export default AccountMenu
