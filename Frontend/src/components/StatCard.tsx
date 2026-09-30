import React from 'react'
import { motion } from 'framer-motion'
import { ArrowUp, ArrowDown } from 'lucide-react'
import { AnimatedCounter } from './AnimatedCounter'
import { cardHoverProps } from '../utils/motion'

/** One channel's contribution to a stat card's headline number. */
export interface StatCardChannelSlice {
  /** Channel key, used for the React key and the accent colour. */
  key: string
  label: string
  value: number
  /** A CSS colour — in practice `var(--channel-…)`, so dark mode is handled. */
  color: string
}

interface StatCardProps {
  icon: React.ReactNode
  label: string
  value: string | number
  colorClass: 'blue' | 'purple' | 'green' | 'orange' | 'red'
  changePercent?: number
  periodLabel?: string

  /**
   * The old two-part footer. Still supported — several cards genuinely have a secondary count —
   * but a card that passes `footnote` or `channels` uses those instead.
   */
  bottomLabel?: string
  bottomValue?: string | number

  /** A single line under the value, e.g. "89.0% delivery rate". */
  footnote?: string

  /**
   * Where the headline number came from, per channel.
   *
   * Rendered only when there is more than one channel with a value: on a WhatsApp-only account
   * a breakdown that always reads "WhatsApp 100%" is noise, and repeating the headline number
   * underneath itself makes the card harder to read, not easier.
   */
  channels?: StatCardChannelSlice[]
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
  footnote,
  channels
}) => {
  const hasTrend = typeof changePercent === 'number' && !isNaN(changePercent)
  const isPositive = (changePercent ?? 0) >= 0

  // Worth showing only when it says something the headline does not.
  const contributing = (channels ?? []).filter(c => c.value > 0)
  const showChannels = contributing.length > 1

  const hasBottomRow = bottomLabel !== undefined && bottomValue !== undefined

  // Compact, left-aligned layout so a whole row of KPIs fits one line: label beside its icon,
  // then the number, then its context. The accent (top cap and icon) follows the card's meaning
  // through `tone-*`, not its position in the row.
  return (
    <motion.div
      className={`stat-card tone-${colorClass}`}
      {...cardHoverProps}
    >
      <div className="stat-card-head">
        <span className={`stat-card-icon-wrapper ${colorClass}`} aria-hidden="true">
          {icon}
        </span>
        <span className="stat-card-label">{label}</span>
      </div>
      <AnimatedCounter value={value} className="stat-card-value" />
      {hasTrend && (
        <div className={`stat-card-trend ${isPositive ? 'positive' : 'negative'}`}>
          {isPositive ? <ArrowUp size={12} aria-hidden="true" /> : <ArrowDown size={12} aria-hidden="true" />}
          <span>{Math.abs(changePercent as number).toFixed(1)}%</span>
          {periodLabel && <span className="stat-card-trend-period">vs {periodLabel}</span>}
        </div>
      )}
      {footnote && <div className="stat-card-footnote">{footnote}</div>}

      {showChannels && (
        <div className="stat-card-channels">
          {contributing.map(slice => (
            <span className="stat-card-channel" key={slice.key} title={slice.label}>
              <span className="stat-card-channel-dot" style={{ backgroundColor: slice.color }} />
              <span className="stat-card-channel-label">{slice.label}</span>
              <span className="stat-card-channel-value">{slice.value.toLocaleString()}</span>
            </span>
          ))}
        </div>
      )}

      {hasBottomRow && (
        <div className="stat-card-bottom">
          <span className="stat-card-bottom-label">{bottomLabel}</span>
          <AnimatedCounter value={bottomValue!} className="stat-card-bottom-value" />
        </div>
      )}
    </motion.div>
  )
})
