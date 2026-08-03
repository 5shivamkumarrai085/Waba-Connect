import React, { useEffect } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { motion, AnimatePresence } from 'framer-motion'
import { useSidebarStore } from '../store/zustand'
import {
  LayoutDashboard,
  BarChart3,
  History,
  Link2,
  Users,
  FileText,
  Megaphone,
  Layers,
  MessageSquare,
  Bot,
  GitFork,
  MessageCircle,
  Settings,
  Sliders,
  Cpu,
  Shield,
  ShieldCheck,
  ChevronLeft,
  ChevronRight,
  Menu,
  X
} from 'lucide-react'

interface MenuItem {
  name: string
  path: string
  icon: React.ComponentType<any>
}

interface MenuSection {
  title?: string
  items: MenuItem[]
}

export const Sidebar: React.FC = () => {
  const { isCollapsed, toggleSidebar, setCollapsed } = useSidebarStore()
  const location = useLocation()

  // Auto-close sidebar on route change on mobile/tablet
  useEffect(() => {
    if (window.innerWidth < 1200) {
      setCollapsed(true)
    }
  }, [location.pathname])

  // Prevent scroll when sidebar is open on mobile/tablet
  useEffect(() => {
    const handleBodyScroll = () => {
      const isMobile = window.innerWidth < 1200
      if (isMobile && !isCollapsed) {
        document.body.classList.add('sidebar-open')
      } else {
        document.body.classList.remove('sidebar-open')
      }
    }
    handleBodyScroll()
    window.addEventListener('resize', handleBodyScroll)
    return () => {
      document.body.classList.remove('sidebar-open')
      window.removeEventListener('resize', handleBodyScroll)
    }
  }, [isCollapsed])

  const handlePrefetch = (path: string) => {
    const loadFns: Record<string, () => Promise<any>> = {
      '/': () => import('../pages/Dashboard'),
      '/campaigns/campaign': () => import('../pages/Campaigns/CampaignsList'),
      '/reporting': () => import('../pages/Reporting'),
      '/activity-logs': () => import('../pages/ActivityLogs'),
      '/connect-waba': () => import('../pages/ConnectWABA/ConnectWABA'),
      '/contacts': () => import('../pages/Contacts/ContactsList'),
      '/templates': () => import('../pages/Templates/TemplatesList'),
      '/bulk-campaigns': () => import('../pages/BulkCampaign/BulkCampaign'),
      '/chat': () => import('../pages/Chat/Chat'),
      '/template-bot': () => import('../pages/TemplateBot/TemplateBotList'),
      '/bot-flow': () => import('../pages/BotFlow/BotFlowList')
    }
    if (loadFns[path]) {
      loadFns[path]().catch(() => {})
    }
  }

  const menuSections: MenuSection[] = [
    {
      items: [
        { name: 'Dashboard', path: '/', icon: LayoutDashboard },
        { name: 'Reporting', path: '/reporting', icon: BarChart3 },
        { name: 'Activity Logs', path: '/activity-logs', icon: History },
      ]
    },
    {
      title: 'Contact',
      items: [
        { name: 'Contact', path: '/contacts', icon: Users },
      ]
    },
    {
      title: 'Templates',
      items: [
        { name: 'Templates', path: '/templates', icon: FileText },
      ]
    },
    {
      title: 'Marketing',
      items: [
        { name: 'Campaign', path: '/campaigns/campaign', icon: Megaphone },
        { name: 'Bulk Campaign', path: '/bulk-campaigns', icon: Layers },
        { name: 'Message Bot', path: '/message-bot', icon: MessageSquare },
        { name: 'Template Bot', path: '/template-bot', icon: Bot },
        { name: 'Bot Flow', path: '/bot-flow', icon: GitFork },
      ]
    },
    {
      title: 'Support',
      items: [
        { name: 'Chat', path: '/chat', icon: MessageCircle },
      ]
    },
    {
      title: 'Admin',
      items: [
        { name: 'User Permissions', path: '/admin/permissions/user', icon: Shield },
        { name: 'Dept Permissions', path: '/admin/permissions/department', icon: ShieldCheck },
      ]
    },
    {
      title: 'Settings',
      items: [
        { name: 'Connections', path: '/connections', icon: Link2 },
        { name: 'System Settings', path: '/system-settings', icon: Settings },
        { name: 'OmniConnect Settings', path: '/omniconnect-settings', icon: Sliders },
        { name: 'Setup', path: '/setup', icon: Cpu },
      ]
    }
  ]

  return (
    <>
      {/* Floating hamburger button for tablet/mobile */}
      <motion.button 
        className="sidebar-hamburger" 
        onClick={toggleSidebar}
        aria-label="Toggle Navigation Menu"
        whileTap={{ scale: 0.9 }}
        transition={{ duration: 0.1 }}
      >
        {isCollapsed ? <Menu size={20} /> : <X size={20} />}
      </motion.button>

      {/* Backdrop overlay for tablet/mobile */}
      <AnimatePresence>
        {!isCollapsed && (
          <motion.div 
            className="sidebar-backdrop" 
            onClick={() => setCollapsed(true)}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
          />
        )}
      </AnimatePresence>

      <aside className={`sidebar ${isCollapsed ? 'collapsed' : ''}`}>
        <div className="sidebar-header">
          <AnimatePresence mode="wait">
            {!isCollapsed && (
              <motion.div 
                className="sidebar-logo"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.15 }}
              >
                <img src="/rma.png" alt="RMA Logo" className="sidebar-logo-img" />
              </motion.div>
            )}
          </AnimatePresence>
          <motion.button 
            className="sidebar-toggle-btn" 
            onClick={toggleSidebar}
            aria-label={isCollapsed ? 'Expand Sidebar' : 'Collapse Sidebar'}
            whileHover={{ scale: 1.1 }}
            whileTap={{ scale: 0.9 }}
            transition={{ duration: 0.1 }}
          >
            {isCollapsed ? <ChevronRight size={14} /> : <ChevronLeft size={14} />}
          </motion.button>
        </div>

        <div className="sidebar-content">
          {menuSections.map((section, idx) => (
            <div key={idx} className="sidebar-section">
              <AnimatePresence>
                {section.title && !isCollapsed && (
                  <motion.h3 
                    className="sidebar-section-title"
                    initial={{ opacity: 0, x: -8 }}
                    animate={{ opacity: 1, x: 0 }}
                    exit={{ opacity: 0, x: -8 }}
                    transition={{ duration: 0.15 }}
                  >
                    {section.title}
                  </motion.h3>
                )}
              </AnimatePresence>
              {section.items.map((item) => (
                <NavLink
                  key={item.name}
                  to={item.path}
                  className={({ isActive }) => 
                    `sidebar-menu-item ${isActive ? 'active' : ''}`
                  }
                  onMouseEnter={() => handlePrefetch(item.path)}
                  onFocus={() => handlePrefetch(item.path)}
                >
                  <item.icon className="sidebar-menu-icon" size={18} />
                  <span className="sidebar-menu-label">{item.name}</span>
                </NavLink>
              ))}
            </div>
          ))}
        </div>

        {/* Sidebar Footer — version & fun tagline */}
        <AnimatePresence>
          {!isCollapsed && (
            <motion.div 
              className="sidebar-footer"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.15 }}
            >
              <div className="sidebar-footer-version">OmniConnect v2.0</div>
              <div className="sidebar-footer-tagline">Built with ☕ and ambition</div>
            </motion.div>
          )}
        </AnimatePresence>
      </aside>
    </>
  )
}
export default Sidebar
