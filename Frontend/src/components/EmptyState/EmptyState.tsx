import React from 'react'
import * as Icons from 'lucide-react'
import './EmptyState.css'

interface EmptyStateProps {
  iconName?: string
  message: string
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  iconName = 'ShieldAlert',
  message
}) => {
  const IconComponent = (Icons as any)[iconName] || Icons.HelpCircle

  return (
    <div className="empty-state-wrapper">
      <div className="empty-state-icon-box">
        <IconComponent size={44} strokeWidth={1} />
      </div>
      <p className="empty-state-message">{message}</p>
    </div>
  )
}
