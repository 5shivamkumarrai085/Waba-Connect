import React from 'react'
import { motion } from 'framer-motion'
import { ArrowUp, ArrowDown } from 'lucide-react'
import { AnimatedCounter } from './AnimatedCounter'
import { cardHoverProps } from '../utils/motion'

interface StatCardProps {
  icon: React.ReactNode
  label: string
  value: string | number
  bottomLabel: string
  bottomValue: string | number
  colorClass: 'blue' | 'purple' | 'green' | 'orange'
  changePercent?: number
  periodLabel?: string
}

export const StatCard: React.FC<StatCardProps> = React.memo(({
  icon,
  label,
  value,
  bottomLabel,
  bottomValue,
  colorClass,
  changePercent,
  periodLabel
}) => {
  const hasTrend = typeof changePercent === 'number' && !isNaN(changePercent)
  const isPositive = (changePercent ?? 0) >= 0

  return (
    <motion.div
      className="stat-card"
      {...cardHoverProps}
    >
      <div className="stat-card-top">
        <div className={`stat-card-icon-wrapper ${colorClass}`}>
          {icon}
        </div>
        <div className="stat-card-info">
          <div className="stat-card-label">{label}</div>
          <AnimatedCounter value={value} className="stat-card-value" />
          {hasTrend && (
            <div className={`stat-card-trend ${isPositive ? 'positive' : 'negative'}`}>
              {isPositive ? <ArrowUp size={12} /> : <ArrowDown size={12} />}
              <span>{Math.abs(changePercent as number).toFixed(1)}%</span>
              {periodLabel && <span className="stat-card-trend-period">vs {periodLabel}</span>}
            </div>
          )}
        </div>
      </div>
      <div className="stat-card-bottom">
        <span className="stat-card-bottom-label">{bottomLabel}</span>
        <AnimatedCounter value={bottomValue} className="stat-card-bottom-value" />
      </div>
    </motion.div>
  )
})
