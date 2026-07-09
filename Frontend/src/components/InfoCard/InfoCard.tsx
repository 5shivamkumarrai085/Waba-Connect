import React from 'react'
import { HelpCircle, AlertTriangle } from 'lucide-react'
import './InfoCard.css'

interface InfoCardProps {
  title: string
  theme?: 'default' | 'blue' | 'orange'
  iconName?: 'help' | 'alert' | string
  children: React.ReactNode
}

export const InfoCard: React.FC<InfoCardProps> = ({
  title,
  theme = 'default',
  iconName,
  children
}) => {
  const renderIcon = () => {
    if (iconName === 'help' || theme === 'blue') {
      return <HelpCircle size={16} />
    }
    if (iconName === 'alert' || theme === 'orange') {
      return <AlertTriangle size={16} />
    }
    return null
  }

  return (
    <div className={`info-card theme-${theme}`}>
      <div className="info-card-header">
        {renderIcon()}
        <h4 className="info-card-title">{title}</h4>
      </div>
      <div className="info-card-body">
        {children}
      </div>
    </div>
  )
}
export default InfoCard
