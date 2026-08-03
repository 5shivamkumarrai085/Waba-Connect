import React from 'react'
import { motion } from 'framer-motion'
import * as Icons from 'lucide-react'
import './Tabs.css'

export interface TabItem {
  id: string
  label: string
  iconName: string
  type: 'error' | 'success' | 'audit'
}

interface TabsProps {
  tabs: TabItem[]
  activeTab: string
  onChange: (tabId: any) => void
}

export const Tabs: React.FC<TabsProps> = ({
  tabs,
  activeTab,
  onChange
}) => {
  return (
    <div className="tabs-container">
      {tabs.map((tab) => {
        const LucideIcon = (Icons as any)[tab.iconName] || Icons.HelpCircle
        const isActive = activeTab === tab.id
        
        return (
          <motion.button
            key={tab.id}
            className={`tab-btn ${isActive ? 'active' : ''} tab-type-${tab.type}`}
            onClick={() => onChange(tab.id)}
            whileHover={{ scale: 1.02 }}
            whileTap={{ scale: 0.98 }}
            transition={{ duration: 0.12 }}
            style={{ position: 'relative' }}
          >
            <LucideIcon className="tab-icon" size={16} />
            <span>{tab.label}</span>
            {isActive && (
              <motion.div
                className="tab-active-indicator"
                layoutId="tab-active-indicator"
                transition={{ type: 'spring', stiffness: 400, damping: 30 }}
              />
            )}
          </motion.button>
        )
      })}
    </div>
  )
}
