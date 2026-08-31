import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { PieChart, Pie, Cell, ResponsiveContainer } from 'recharts'
import { Info, ArrowUpRight } from 'lucide-react'

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
}

type ViewMode = 'delivery' | 'read'

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

export const DeliveryRateCard: React.FC<DeliveryRateCardProps> = React.memo(({ deliveryBreakdown, readBreakdown }) => {
  const navigate = useNavigate()
  const [mode, setMode] = useState<ViewMode>('delivery')

  const isDelivery = mode === 'delivery'
  const breakdown = isDelivery ? deliveryBreakdown : readBreakdown

  const total = isDelivery
    ? (breakdown?.delivered || 0) + (breakdown?.failed || 0) + (breakdown?.pending || 0)
    : (breakdown?.read || 0) + (breakdown?.unread || 0) + (breakdown?.notDelivered || 0)

  const centerPercent = isDelivery ? (breakdown?.deliveredPercent ?? 0) : (breakdown?.readPercent ?? 0)

  const chartData = isDelivery
    ? [
        { key: 'delivered', name: 'Delivered', value: breakdown?.delivered || 0, percent: breakdown?.deliveredPercent ?? 0, color: DELIVERY_COLORS.delivered },
        { key: 'failed', name: 'Failed', value: breakdown?.failed || 0, percent: breakdown?.failedPercent ?? 0, color: DELIVERY_COLORS.failed },
        { key: 'pending', name: 'Pending', value: breakdown?.pending || 0, percent: breakdown?.pendingPercent ?? 0, color: DELIVERY_COLORS.pending }
      ]
    : [
        { key: 'read', name: 'Read', value: breakdown?.read || 0, percent: breakdown?.readPercent ?? 0, color: READ_COLORS.read },
        { key: 'unread', name: 'Delivered, Unread', value: breakdown?.unread || 0, percent: breakdown?.unreadPercent ?? 0, color: READ_COLORS.unread },
        { key: 'notDelivered', name: 'Not Delivered', value: breakdown?.notDelivered || 0, percent: breakdown?.notDeliveredPercent ?? 0, color: READ_COLORS.notDelivered }
      ]

  return (
    <div className="delivery-rate-card">
      <div className="delivery-rate-header">
        <div className="delivery-rate-toggle">
          <button
            className={`delivery-rate-toggle-btn ${isDelivery ? 'active' : ''}`}
            onClick={() => setMode('delivery')}
          >
            Delivery Rate
            {isDelivery && (
              <motion.div className="delivery-rate-toggle-indicator" layoutId="delivery-rate-toggle-indicator" />
            )}
          </button>
          <button
            className={`delivery-rate-toggle-btn ${!isDelivery ? 'active' : ''}`}
            onClick={() => setMode('read')}
          >
            Read Rate
            {!isDelivery && (
              <motion.div className="delivery-rate-toggle-indicator" layoutId="delivery-rate-toggle-indicator" />
            )}
          </button>
        </div>
        <button className="delivery-rate-view-report" onClick={() => navigate('/reporting')}>
          View Report <ArrowUpRight size={13} />
        </button>
      </div>

      {total === 0 ? (
        <div className="chart-empty-container delivery-rate-empty">
          <div className="empty-state chart-empty-state">
            <Info className="empty-state-icon" />
            <h3 className="empty-state-title">No Data Available</h3>
            <p className="empty-state-desc">No messages recorded for this period yet.</p>
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
