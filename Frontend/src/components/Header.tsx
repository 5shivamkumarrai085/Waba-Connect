import React, { useState, useEffect, useRef } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
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
  Layers
} from 'lucide-react'

export const Header: React.FC = () => {
  const location = useLocation()
  const navigate = useNavigate()
  
  const [showQuickCreate, setShowQuickCreate] = useState(false)
  const quickCreateRef = useRef<HTMLDivElement>(null)

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
    { label: 'User', icon: Users, path: '/admin/permissions/user' },
    { label: 'Role', icon: ShieldCheck, path: '/admin/permissions/department' },
    { label: 'Status', icon: Sliders, path: '/contacts' },
    { label: 'Source', icon: Layers, path: '/contacts' },
  ]

  const handleNavigate = (path: string) => {
    setShowQuickCreate(false)
    navigate(path)
  }

  return (
    <header className="header">
      <div className="header-left">
        {location.pathname !== '/' && (
          <button 
            className="header-icon-btn header-back-btn" 
            onClick={() => navigate(-1)}
            aria-label="Go Back"
          >
            <ArrowLeft size={20} strokeWidth={2.5} />
          </button>
        )}
      </div>

      <div className="header-right">
        {/* Header Action Buttons */}
        <div className="header-actions">
          <button className="header-icon-btn" aria-label="Settings">
            <Settings size={18} />
          </button>
          
          <div className="dropdown-container" ref={quickCreateRef}>
            <button 
              className={`header-icon-btn header-create-plus-btn ${showQuickCreate ? 'active' : ''}`}
              onClick={() => setShowQuickCreate(!showQuickCreate)}
              aria-label="Quick Create Menu"
            >
              <Plus size={18} strokeWidth={2.5} />
            </button>

            {showQuickCreate && (
              <div className="quick-create-dropdown-menu">
                {menuItems.map((item) => {
                  const Icon = item.icon
                  return (
                    <button
                      key={item.label}
                      className="quick-create-item"
                      onClick={() => handleNavigate(item.path)}
                    >
                      <Icon size={18} className="quick-create-icon" />
                      <span>{item.label}</span>
                    </button>
                  )
                })}
              </div>
            )}
          </div>
        </div>
      </div>
    </header>
  )
}

