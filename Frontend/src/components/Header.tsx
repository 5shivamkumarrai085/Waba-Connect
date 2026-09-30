import React, { useState, useEffect, useRef } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { motion, AnimatePresence } from 'framer-motion'
import {
  Settings,
  Plus,
  ArrowLeft,
  User,
  Megaphone,
  MessageSquare,
  Tag,
  GitBranch,
  Users,
  ShieldCheck,
  Sliders,
  Layers,
  KeyRound,
  LogOut
} from 'lucide-react'
import { fadeScale, transitions } from '../utils/motion'
import { Menu, MenuItem, MenuSeparator, MenuLabel } from './Menu/Menu'
import useAuthStore from '../store/authStore'
import { resolveMediaUrl } from '../utils/mediaUrl'

export const Header: React.FC = () => {
  const location = useLocation()
  const navigate = useNavigate()

  const user = useAuthStore((state) => state.user)
  const logout = useAuthStore((state) => state.logout)

  const [showQuickCreate, setShowQuickCreate] = useState(false)
  const [showAccountMenu, setShowAccountMenu] = useState(false)
  const quickCreateRef = useRef<HTMLDivElement>(null)

  const handleLogout = async () => {
    await logout()
    navigate('/login', { replace: true })
  }

  const initials = user
    ? `${user.firstName?.[0] ?? ''}${user.lastName?.[0] ?? ''}`.toUpperCase() || user.email[0].toUpperCase()
    : ''

  // Close dropdown on outside click or Escape key
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (quickCreateRef.current && !quickCreateRef.current.contains(e.target as Node)) {
        setShowQuickCreate(false)
      }
    }
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setShowQuickCreate(false)
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [])

  const menuItems = [
    { label: 'Contact', icon: User, path: '/contacts/contact' },
    { label: 'Campaign', icon: Megaphone, path: '/campaigns/campaign/create' },
    { label: 'Message Bot', icon: MessageSquare, path: '/message-bot/bot' },
    { label: 'Template Bot', icon: Tag, path: '/template-bot/bot' },
    { label: 'Bot Flow', icon: GitBranch, path: '/bot-flow' },
    { label: 'User', icon: Users, path: '/setup/users/new' },
    { label: 'Role', icon: ShieldCheck, path: '/setup/roles/new' },
    { label: 'Status', icon: Sliders, path: '/setup/status' },
    { label: 'Source', icon: Layers, path: '/setup/source' },
  ]

  const handleNavigate = (path: string) => {
    setShowQuickCreate(false)
    navigate(path)
  }

  return (
    <header className="header">
      <div className="header-left">
        {location.pathname !== '/' && (
          <motion.button 
            className="header-icon-btn header-back-btn" 
            onClick={() => navigate(-1)}
            aria-label="Go Back"
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            transition={{ duration: 0.1 }}
          >
            <ArrowLeft size={20} strokeWidth={2.5} />
          </motion.button>
        )}
      </div>

      <div className="header-right">
        {/* Header Action Buttons */}
        <div className="header-actions">
          { <motion.button
            className="header-icon-btn"
            aria-label="OmniConnect settings"
            title="OmniConnect Settings"
            onClick={() => navigate('/omniconnect-settings')}
            whileHover={{ scale: 1.05, rotate: 15 }}
            whileTap={{ scale: 0.95 }}
            transition={{ duration: 0.15 }}
          >
            <Settings size={18} />
          </motion.button> }
          
          <div className="dropdown-container" ref={quickCreateRef}>
            <motion.button 
              className={`header-icon-btn header-create-plus-btn ${showQuickCreate ? 'active' : ''}`}
              onClick={() => setShowQuickCreate(!showQuickCreate)}
              aria-label="Quick Create Menu"
              whileTap={{ scale: 0.9 }}
              transition={{ duration: 0.1 }}
            >
              <Plus size={18} strokeWidth={2.5} />
            </motion.button>

            <AnimatePresence>
              {showQuickCreate && (
                <motion.div 
                  className="quick-create-dropdown-menu"
                  variants={fadeScale}
                  initial="hidden"
                  animate="visible"
                  exit="exit"
                  transition={transitions.snappy}
                  style={{ transformOrigin: 'top right' }}
                >
                  {menuItems.map((item, index) => {
                    const Icon = item.icon
                    return (
                      <motion.button
                        key={item.label}
                        className="quick-create-item"
                        onClick={() => handleNavigate(item.path)}
                        initial={{ opacity: 0, x: 8 }}
                        animate={{ opacity: 1, x: 0 }}
                        transition={{ delay: index * 0.03, duration: 0.15 }}
                        whileHover={{ x: 4, backgroundColor: '#f1f5f9' }}
                      >
                        <Icon size={18} className="quick-create-icon" />
                        <span>{item.label}</span>
                      </motion.button>
                    )
                  })}
                </motion.div>
              )}
            </AnimatePresence>
          </div>

          {user && (
            <Menu
              open={showAccountMenu}
              onOpenChange={setShowAccountMenu}
              align="end"
              offset={8}
              ariaLabel="Account menu"
              trigger={(props) => (
                <button
                  {...props}
                  type="button"
                  className="header-avatar-btn"
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
          )}
        </div>
      </div>
    </header>
  )
}
