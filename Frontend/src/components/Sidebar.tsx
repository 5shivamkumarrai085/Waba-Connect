import React from 'react'
import { NavLink } from 'react-router-dom'
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
  ChevronLeft,
  ChevronRight
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
  const { isCollapsed, toggleSidebar } = useSidebarStore()

  const menuSections: MenuSection[] = [
    {
      items: [
        { name: 'Dashboard', path: '/', icon: LayoutDashboard },
        { name: 'Reporting', path: '/reporting', icon: BarChart3 },
        { name: 'Activity Logs', path: '/activity-logs', icon: History },
        { name: 'Connect WABA', path: '/connect-waba', icon: Link2 },
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
      title: 'Settings',
      items: [
        { name: 'System Settings', path: '/system-settings', icon: Settings },
        { name: 'OmniConnect Settings', path: '/omniconnect-settings', icon: Sliders },
        { name: 'Setup', path: '/setup', icon: Cpu },
      ]
    }
  ]

  return (
    <aside className={`sidebar ${isCollapsed ? 'collapsed' : ''}`}>
      <div className="sidebar-header">
        {!isCollapsed && (
          <div className="sidebar-logo">
            <img src="/site_logo_1779180395.png" alt="RMA Logo" className="sidebar-logo-img" />
          </div>
        )}
        <button 
          className="sidebar-toggle-btn" 
          onClick={toggleSidebar}
          aria-label={isCollapsed ? 'Expand Sidebar' : 'Collapse Sidebar'}
        >
          {isCollapsed ? <ChevronRight size={14} /> : <ChevronLeft size={14} />}
        </button>
      </div>

      <div className="sidebar-content">
        {menuSections.map((section, idx) => (
          <div key={idx} className="sidebar-section">
            {section.title && !isCollapsed && (
              <h3 className="sidebar-section-title">{section.title}</h3>
            )}
            {section.items.map((item) => (
              <NavLink
                key={item.name}
                to={item.path}
                className={({ isActive }) => 
                  `sidebar-menu-item ${isActive ? 'active' : ''}`
                }
              >
                <item.icon className="sidebar-menu-icon" size={18} />
                <span className="sidebar-menu-label">{item.name}</span>
              </NavLink>
            ))}
          </div>
        ))}
      </div>
    </aside>
  )
}
