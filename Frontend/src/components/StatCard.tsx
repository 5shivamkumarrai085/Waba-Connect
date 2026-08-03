import React from 'react'
import { motion } from 'framer-motion'
import { AreaChart, Area, ResponsiveContainer } from 'recharts'
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
  sparkline?: { name: string; value: number }[]
}

export const StatCard: React.FC<StatCardProps> = React.memo(({
  icon,
  label,
  value,
  bottomLabel,
  bottomValue,
  colorClass,
  changePercent,
  periodLabel,
  sparkline
}) => {
  const hasTrend = typeof changePercent === 'number' && !isNaN(changePercent)
  const isPositive = (changePercent ?? 0) >= 0
  const hasSparkline = sparkline && sparkline.length > 0

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
        {hasSparkline && (
          <div className={`stat-card-sparkline stat-card-sparkline-${colorClass}`}>
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={sparkline} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
                <defs>
                  <linearGradient id={`spark-${colorClass}`} x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="currentColor" stopOpacity={0.35} />
                    <stop offset="95%" stopColor="currentColor" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <Area
                  type="monotone"
                  dataKey="value"
                  stroke="currentColor"
                  strokeWidth={1.75}
                  fill={`url(#spark-${colorClass})`}
                  isAnimationActive={false}
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>
      <div className="stat-card-bottom">
        <span className="stat-card-bottom-label">{bottomLabel}</span>
        <AnimatedCounter value={bottomValue} className="stat-card-bottom-value" />
      </div>
    </motion.div>
  )
})
