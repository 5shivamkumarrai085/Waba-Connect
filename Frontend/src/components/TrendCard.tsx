import React from 'react'
import { AreaChart, Area, ResponsiveContainer } from 'recharts'
import { usePreviewStore } from '../store/zustand'
import { CheckCircle2, Eye, Info } from 'lucide-react'

interface TrendCardProps {
  type: 'delivery' | 'read'
  value?: string
  data?: any[]
}

// Hourly values from 00:00 to 23:00
const hours = Array.from({ length: 24 }, (_, i) => `${i.toString().padStart(2, '0')}:00`)

// Mock data generator for delivery (peaks at 15:00 with 25%)
const deliveryMockData = hours.map((hour) => ({
  name: hour,
  value: hour === '15:00' ? 25 : 0
}))

// Mock data generator for read (peaks at 15:00 with 100%)
const readMockData = hours.map((hour) => ({
  name: hour,
  value: hour === '15:00' ? 100 : 0
}))

// Empty data
const emptyData = hours.map((hour) => ({
  name: hour,
  value: 0
}))

export const TrendCard: React.FC<TrendCardProps> = ({ type, value: propValue, data: propData }) => {
  const { previewMode } = usePreviewStore()
  
  const isDelivery = type === 'delivery'
  const title = isDelivery ? 'Delivery Trend' : 'Read Trend'
  const subtitle = isDelivery ? 'Delivery rate trend' : 'Read/open rate'
  
  // Dynamic metrics depending on props or preview mode
  const rateValue = propValue !== undefined 
    ? propValue 
    : (previewMode ? (isDelivery ? '25.0%' : '100.0%') : '0.0%')
    
  const chartData = propData && propData.length > 0 
    ? propData 
    : (previewMode ? (isDelivery ? deliveryMockData : readMockData) : emptyData)

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
        {!previewMode ? (
          <div className="chart-empty-container">
            <span className="trend-empty-text">
              <Info size={12} /> Empty
            </span>
          </div>
        ) : (
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart
              data={chartData}
              margin={{ top: 10, right: 0, left: 0, bottom: 0 }}
            >
              <defs>
                <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={primaryColor} stopOpacity={0.3}/>
                  <stop offset="95%" stopColor={primaryColor} stopOpacity={0.0}/>
                </linearGradient>
              </defs>
              <Area 
                type="monotone" 
                dataKey="value" 
                stroke={primaryColor} 
                strokeWidth={2}
                fillOpacity={1} 
                fill={`url(#${gradientId})`}
                dot={{ stroke: primaryColor, strokeWidth: 1.5, r: 3, fill: '#fff' }}
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
              <div className="trend-metric-value">{propValue !== undefined ? propValue : (previewMode ? '25%' : '0%')}</div>
            </div>
            <div className="trend-metric-item trend-metric-border-x">
              <div className="trend-metric-label">Best Day</div>
              <div className="trend-metric-value">
                {propData && propData.length > 0 ? (propData.reduce((max: any, d: any) => d.value > max.value ? d : max, { name: '-', value: -1 }).name) : (previewMode ? '15:00' : '-')}{' '}
                <span className="trend-metric-sub">
                  {propData && propData.length > 0 ? `${propData.reduce((max: any, d: any) => d.value > max.value ? d : max, { name: '-', value: -1 }).value}%` : (previewMode ? '25%' : '')}
                </span>
              </div>
            </div>
            <div className="trend-metric-item">
              <div className="trend-metric-label">Failed</div>
              <div className="trend-metric-value error-text">{propData && propData.length > 0 ? '0' : (previewMode ? '3' : '0')}</div>
            </div>
          </>
        ) : (
          <>
            <div className="trend-metric-item">
              <div className="trend-metric-label">Avg Open Rate</div>
              <div className="trend-metric-value">{propValue !== undefined ? propValue : (previewMode ? '100%' : '0%')}</div>
            </div>
            <div className="trend-metric-item trend-metric-border-x">
              <div className="trend-metric-label">Highest Engagement</div>
              <div className="trend-metric-value">
                {propData && propData.length > 0 ? (propData.reduce((max: any, d: any) => d.value > max.value ? d : max, { name: '-', value: -1 }).name) : (previewMode ? '15:00' : '-')}{' '}
                <span className="trend-metric-sub">
                  {propData && propData.length > 0 ? `${propData.reduce((max: any, d: any) => d.value > max.value ? d : max, { name: '-', value: -1 }).value}%` : (previewMode ? '100%' : '')}
                </span>
              </div>
            </div>
            <div className="trend-metric-item">
              <div className="trend-metric-label">Campaign Trend</div>
              <div className="trend-metric-value">
                {propValue !== undefined ? propValue : (previewMode ? '100%' : '0%')}{' '}
                <span className="trend-metric-sub">
                  {propData && propData.length > 0 ? 'top campaign' : (previewMode ? 'top campaign' : '')}
                </span>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
