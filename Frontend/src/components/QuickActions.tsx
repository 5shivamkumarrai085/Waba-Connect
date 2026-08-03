import React from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Megaphone, UserPlus, Bot, FileText } from 'lucide-react'
import { buttonHoverProps } from '../utils/motion'

const ACTIONS = [
  { key: 'campaign', label: 'New Campaign', icon: Megaphone, colorClass: 'purple', path: '/campaigns/campaign/create' },
  { key: 'contact', label: 'Add Contact', icon: UserPlus, colorClass: 'green', path: '/contacts/contact' },
  { key: 'bot', label: 'Message Bot', icon: Bot, colorClass: 'blue', path: '/message-bot/bot' },
  { key: 'template', label: 'Template', icon: FileText, colorClass: 'orange', path: '/templates' }
]

export const QuickActions: React.FC = React.memo(() => {
  const navigate = useNavigate()

  return (
    <div className="quick-actions-card">
      <div className="quick-actions-title">Quick Actions</div>
      <div className="quick-actions-grid">
        {ACTIONS.map(({ key, label, icon: Icon, colorClass, path }) => (
          <motion.button
            key={key}
            className="quick-action-btn"
            onClick={() => navigate(path)}
            {...buttonHoverProps}
          >
            <span className={`quick-action-icon ${colorClass}`}>
              <Icon size={18} />
            </span>
            <span className="quick-action-label">{label}</span>
          </motion.button>
        ))}
      </div>
    </div>
  )
})

export default QuickActions
