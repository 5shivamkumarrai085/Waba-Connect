import React from 'react'
import { AreaChart, Area, ResponsiveContainer } from 'recharts'
import { CheckCircle2, Eye, Info } from 'lucide-react'

interface TrendCardProps {
  type: 'delivery' | 'read'
  value?: string
  data?: any[]
}

// Hourly values from 00:00 to 23:00
const hours = Array.from({ length: 24 }, (_, i) => `${i.toString().padStart(2, '0')}:00`)

// Empty data
const emptyData = hours.map((hour) => ({
  name: hour,
  value: 0
}))

export const TrendCard: React.FC<TrendCardProps> = ({ type, value: propValue, data: propData }) => {
  const isDelivery = type === 'delivery'
  const title = isDelivery ? 'Delivery Trend' : 'Read Trend'
  const subtitle = isDelivery ? 'Delivery rate trend' : 'Read/open rate'
  
  const rateValue = propValue !== undefined ? propValue : '0.0%'
  const chartData = propData && propData.length > 0 ? propData : emptyData

  const primaryColor = isDelivery ? '#10b981' : 'var(--primary)'
  const gradientId = isDelivery ? 'colorDelivery' : 'colorRead'

  return (
    <div className="trend-card">
      <div className="trend-header">
        <div>
          <div className={`trend-title ${type}`}>
            {isDelivery ? (
              <CheckCircle2 size={16} color="#10b981" className="trend-title-icon" />
            ) : (
              <Eye size={16} color="var(--primary)" className="trend-title-icon" />
            )}
            <span>{title}</span>
          </div>
          <div className="trend-header-subtitle">{subtitle}</div>
        </div>
        <div className="trend-value">{rateValue}</div>
      </div>

      <div className="trend-chart-body">
        {(!propData || propData.length === 0) ? (
          <div className="chart-empty-container">
            <div className="empty-state chart-empty-state">
              <Info className="empty-state-icon" />
              <h3 className="empty-state-title">No Data</h3>
              <p className="empty-state-desc">No data for this trend.</p>
            </div>
          </div>
        ) : (
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart data={chartData} margin={{ top: 5, right: 0, left: 0, bottom: 0 }}>
              <defs>
                <linearGradient id="colorDelivery" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#10b981" stopOpacity={0.4}/>
                  <stop offset="95%" stopColor="#10b981" stopOpacity={0.0}/>
                </linearGradient>
                <linearGradient id="colorRead" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="var(--primary)" stopOpacity={0.4}/>
                  <stop offset="95%" stopColor="var(--primary)" stopOpacity={0.0}/>
                </linearGradient>
              </defs>
              <Area 
                type="monotone" 
                dataKey="value" 
                stroke={primaryColor} 
                strokeWidth={2}
                fillOpacity={1} 
                fill={`url(#${gradientId})`} 
                isAnimationActive={false}
              />
            </AreaChart>
          </ResponsiveContainer>
        )}
      </div>

      <div className="trend-metrics">
        {isDelivery ? (
          <>
            <div className="trend-metric-item">
              <div className="trend-metric-label">Avg Delivery</div>
              <div className="trend-metric-value">{propValue !== undefined ? propValue : '0%'}</div>
            </div>
            <div className="trend-metric-item trend-metric-border-x">
              <div className="trend-metric-label">Best Day</div>
              <div className="trend-metric-value">
                {propData && propData.length > 0 ? (propData.reduce((max: any, d: any) => d.value > max.value ? d : max, { name: '-', value: -1 }).name) : '-'}{' '}
                <span className="trend-metric-sub">
                  {propData && propData.length > 0 ? `${propData.reduce((max: any, d: any) => d.value > max.value ? d : max, { name: '-', value: -1 }).value}%` : ''}
                </span>
              </div>
            </div>
            <div className="trend-metric-item">
              <div className="trend-metric-label">Failed</div>
              <div className="trend-metric-value error-text">{propData && propData.length > 0 ? '0' : '0'}</div>
            </div>
          </>
        ) : (
          <>
            <div className="trend-metric-item">
              <div className="trend-metric-label">Avg Open Rate</div>
              <div className="trend-metric-value">{propValue !== undefined ? propValue : '0%'}</div>
            </div>
            <div className="trend-metric-item trend-metric-border-x">
              <div className="trend-metric-label">Highest Engagement</div>
              <div className="trend-metric-value">
                {propData && propData.length > 0 ? (propData.reduce((max: any, d: any) => d.value > max.value ? d : max, { name: '-', value: -1 }).name) : '-'}{' '}
                <span className="trend-metric-sub">
                  {propData && propData.length > 0 ? `${propData.reduce((max: any, d: any) => d.value > max.value ? d : max, { name: '-', value: -1 }).value}%` : ''}
                </span>
              </div>
            </div>
            <div className="trend-metric-item">
              <div className="trend-metric-label">Campaign Trend</div>
              <div className="trend-metric-value">
                {propValue !== undefined ? propValue : '0%'}{' '}
                <span className="trend-metric-sub">
                  {propData && propData.length > 0 ? 'top campaign' : ''}
                </span>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
