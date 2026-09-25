import React, { useState, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { PieChart, Pie, Cell, ResponsiveContainer } from 'recharts'
import { Info, ArrowUpRight, ChevronDown } from 'lucide-react'
import type { ChannelBreakdownRow } from './ChannelInsights/ChannelInsightsCard'

interface Breakdown {
  delivered?: number
  failed?: number
  pending?: number
  read?: number
  unread?: number
  notDelivered?: number
  deliveredPercent?: number
  failedPercent?: number
  pendingPercent?: number
  readPercent?: number
  unreadPercent?: number
  notDeliveredPercent?: number
}

interface DeliveryRateCardProps {
  deliveryBreakdown?: Breakdown
  readBreakdown?: Breakdown
  channelBreakdown?: ChannelBreakdownRow[]
}

type ViewMode = 'delivery' | 'read'
type ChannelFilter = 'all' | 'whatsapp' | 'email'

const DELIVERY_COLORS = {
  delivered: '#10b981',
  failed: '#dc2626',
  pending: '#f59e0b'
}

const READ_COLORS = {
  read: '#5a52e5',
  unread: '#3b82f6',
  notDelivered: '#dc2626'
}

export const DeliveryRateCard: React.FC<DeliveryRateCardProps> = React.memo(({
  deliveryBreakdown,
  readBreakdown,
  channelBreakdown = []
}) => {
  const navigate = useNavigate()
  const [mode, setMode] = useState<ViewMode>('delivery')
  const [channelFilter, setChannelFilter] = useState<ChannelFilter>('all')

  const isDelivery = mode === 'delivery'

  // Calculate channel-specific breakdown dynamically in real-time
  const computedBreakdown = useMemo(() => {
    if (channelFilter === 'all' || !channelBreakdown || channelBreakdown.length === 0) {
      return isDelivery ? deliveryBreakdown : readBreakdown
    }

    const row = channelBreakdown.find(
      (c) => c.channel.toLowerCase() === channelFilter.toLowerCase()
    )

    if (!row) {
      return isDelivery ? deliveryBreakdown : readBreakdown
    }

    const total = row.messages || 0
    if (isDelivery) {
      const delivered = row.delivered || 0
      const failed = row.failed || 0
      const pending = row.pending || 0
      return {
        delivered,
        failed,
        pending,
        deliveredPercent: total > 0 ? Math.round((delivered / total) * 1000) / 10 : 0,
        failedPercent: total > 0 ? Math.round((failed / total) * 1000) / 10 : 0,
        pendingPercent: total > 0 ? Math.round((pending / total) * 1000) / 10 : 0
      }
    } else {
      const read = row.read || 0
      const unread = Math.max((row.delivered || 0) - read, 0)
      const notDelivered = (row.failed || 0) + (row.pending || 0)
      return {
        read,
        unread,
        notDelivered,
        readPercent: total > 0 ? Math.round((read / total) * 1000) / 10 : 0,
        unreadPercent: total > 0 ? Math.round((unread / total) * 1000) / 10 : 0,
        notDeliveredPercent: total > 0 ? Math.round((notDelivered / total) * 1000) / 10 : 0
      }
    }
  }, [channelFilter, channelBreakdown, isDelivery, deliveryBreakdown, readBreakdown])

  const total = isDelivery
    ? (computedBreakdown?.delivered || 0) + (computedBreakdown?.failed || 0) + (computedBreakdown?.pending || 0)
    : (computedBreakdown?.read || 0) + (computedBreakdown?.unread || 0) + (computedBreakdown?.notDelivered || 0)

  const centerPercent = isDelivery
    ? (computedBreakdown?.deliveredPercent ?? 0)
    : (computedBreakdown?.readPercent ?? 0)

  const chartData = isDelivery
    ? [
        { key: 'delivered', name: 'Delivered', value: computedBreakdown?.delivered || 0, percent: computedBreakdown?.deliveredPercent ?? 0, color: DELIVERY_COLORS.delivered },
        { key: 'failed', name: 'Failed', value: computedBreakdown?.failed || 0, percent: computedBreakdown?.failedPercent ?? 0, color: DELIVERY_COLORS.failed },
        { key: 'pending', name: 'Pending', value: computedBreakdown?.pending || 0, percent: computedBreakdown?.pendingPercent ?? 0, color: DELIVERY_COLORS.pending }
      ]
    : [
        { key: 'read', name: 'Read', value: computedBreakdown?.read || 0, percent: computedBreakdown?.readPercent ?? 0, color: READ_COLORS.read },
        { key: 'unread', name: 'Delivered, Unread', value: computedBreakdown?.unread || 0, percent: computedBreakdown?.unreadPercent ?? 0, color: READ_COLORS.unread },
        { key: 'notDelivered', name: 'Not Delivered', value: computedBreakdown?.notDelivered || 0, percent: computedBreakdown?.notDeliveredPercent ?? 0, color: READ_COLORS.notDelivered }
      ]

  return (
    <div className="delivery-rate-card">
      <div className="delivery-rate-header">
        <div className="delivery-rate-toggle">
          <button
            type="button"
            className={`delivery-rate-toggle-btn ${isDelivery ? 'active' : ''}`}
            onClick={() => setMode('delivery')}
          >
            Delivery Rate
            {isDelivery && (
              <motion.div className="delivery-rate-toggle-indicator" layoutId="delivery-rate-toggle-indicator" />
            )}
          </button>
          <button
            type="button"
            className={`delivery-rate-toggle-btn ${!isDelivery ? 'active' : ''}`}
            onClick={() => setMode('read')}
          >
            Read Rate
            {!isDelivery && (
              <motion.div className="delivery-rate-toggle-indicator" layoutId="delivery-rate-toggle-indicator" />
            )}
          </button>
        </div>

        <div className="delivery-rate-actions">
          {/* Channel Dropdown Filter */}
          <div className="chart-select-wrapper" style={{ minWidth: 120 }}>
            <select
              className="chart-select-dropdown"
              value={channelFilter}
              onChange={(e) => setChannelFilter(e.target.value as ChannelFilter)}
              aria-label="Filter delivery rate by channel"
            >
              <option value="all">All Channels</option>
              <option value="whatsapp">WhatsApp</option>
              <option value="email">Email</option>
            </select>
            <ChevronDown size={13} className="chart-select-chevron" />
          </div>

          <button className="delivery-rate-view-report" onClick={() => navigate('/reporting')}>
            View Report <ArrowUpRight size={13} />
          </button>
        </div>
      </div>

      {total === 0 ? (
        <div className="chart-empty-container delivery-rate-empty">
          <div className="empty-state chart-empty-state">
            <Info className="empty-state-icon" />
            <h3 className="empty-state-title">No Data Available</h3>
            <p className="empty-state-desc">No messages recorded for this channel/period.</p>
          </div>
        </div>
      ) : (
        <div className="delivery-rate-body">
          <div className="delivery-rate-donut">
            <ResponsiveContainer width="100%" height={180}>
              <PieChart>
                <Pie
                  data={chartData}
                  dataKey="value"
                  nameKey="name"
                  innerRadius={58}
                  outerRadius={80}
                  paddingAngle={2}
                  startAngle={90}
                  endAngle={-270}
                  isAnimationActive={false}
                >
                  {chartData.map((entry) => (
                    <Cell key={entry.key} fill={entry.color} stroke="none" />
                  ))}
                </Pie>
              </PieChart>
            </ResponsiveContainer>
            <div className="delivery-rate-center">
              <div className="delivery-rate-center-value">{centerPercent}%</div>
              <div className="delivery-rate-center-label">{isDelivery ? 'Delivered' : 'Read'}</div>
            </div>
          </div>

          <div className="delivery-rate-legend">
            {chartData.map((entry) => (
              <div className="delivery-rate-legend-item" key={entry.key}>
                <span className="delivery-rate-legend-dot" style={{ backgroundColor: entry.color }} />
                <span className="delivery-rate-legend-name">{entry.name}</span>
                <span className="delivery-rate-legend-value">{entry.value.toLocaleString()}</span>
                <span className="delivery-rate-legend-percent">({entry.percent}%)</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
})

export default DeliveryRateCard
