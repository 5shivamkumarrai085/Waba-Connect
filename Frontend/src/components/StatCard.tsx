import React from 'react'

interface StatCardProps {
  icon: React.ReactNode
  label: string
  value: string | number
  bottomLabel: string
  bottomValue: string | number
  colorClass: 'blue' | 'purple' | 'green' | 'orange'
}

export const StatCard: React.FC<StatCardProps> = React.memo(({
  icon,
  label,
  value,
  bottomLabel,
  bottomValue,
  colorClass
}) => {
  return (
    <div className="stat-card">
      <div className="stat-card-top">
        <div className={`stat-card-icon-wrapper ${colorClass}`}>
          {icon}
        </div>
        <div className="stat-card-info">
          <div className="stat-card-label">{label}</div>
          <div className="stat-card-value">{value}</div>
        </div>
      </div>
      <div className="stat-card-bottom">
        <span className="stat-card-bottom-label">{bottomLabel}</span>
        <span className="stat-card-bottom-value">{bottomValue}</span>
      </div>
    </div>
  )
})
