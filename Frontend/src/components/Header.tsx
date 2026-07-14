import React from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { 
  Settings, 
  Plus, 
  Globe, 
  Sun,
  Moon,
  Monitor,
  ArrowLeft
} from 'lucide-react'
import { useThemeStore } from '../store/zustand'

export const Header: React.FC = () => {
  const location = useLocation()
  const navigate = useNavigate()
  const { theme, toggleTheme } = useThemeStore()
  
  const [showLangMenu, setShowLangMenu] = React.useState(false)
  const [showThemeMenu, setShowThemeMenu] = React.useState(false)
  const [, setLanguage] = React.useState('English')

  React.useEffect(() => {
    document.documentElement.className = theme === 'light' ? 'light-theme' : 'dark-theme'
  }, [theme])

  const handleCreateCampaignClick = () => {
    navigate('/campaigns/campaign')
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
          
          <button 
            className="header-icon-btn header-create-campaign-btn" 
            onClick={handleCreateCampaignClick}
            aria-label="New Campaign"
          >
            <Plus size={18} />
          </button>

          <div className="dropdown-container">
            <button 
              className={`header-icon-btn ${showLangMenu ? 'active' : ''}`}
              onClick={() => {
                setShowLangMenu(!showLangMenu)
                setShowThemeMenu(false)
              }}
              aria-label="Language Select"
            >
              <Globe size={18} />
            </button>
            {showLangMenu && (
              <div className="dropdown-menu">
                <button className="dropdown-item" onClick={() => { setLanguage('English'); setShowLangMenu(false) }}>English</button>
                <button className="dropdown-item" onClick={() => { setLanguage('Bahasa Melayu'); setShowLangMenu(false) }}>Bahasa Melayu</button>
              </div>
            )}
          </div>

          <div className="dropdown-container">
            <button 
              className={`header-icon-btn ${showThemeMenu ? 'active' : ''}`}
              onClick={() => {
                setShowThemeMenu(!showThemeMenu)
                setShowLangMenu(false)
              }}
              aria-label="Toggle Theme"
            >
              {theme === 'dark' ? <Moon size={18} /> : <Sun size={18} />}
            </button>
            {showThemeMenu && (
              <div className="dropdown-menu">
                <button className="dropdown-item" onClick={() => { toggleTheme(); setShowThemeMenu(false) }}>
                  <Sun size={14} className="dropdown-icon" /> Light
                </button>
                <button className="dropdown-item" onClick={() => { toggleTheme(); setShowThemeMenu(false) }}>
                  <Moon size={14} className="dropdown-icon" /> Dark
                </button>
                <button className="dropdown-item" onClick={() => { setShowThemeMenu(false) }}>
                  <Monitor size={14} className="dropdown-icon" /> System
                </button>
              </div>
            )}
          </div>
        </div>

        {/* User Profile */}
        <div className="header-profile">
          <div className="header-avatar">
            S
          </div>
        </div>
      </div>
    </header>
  )
}
