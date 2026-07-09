import React from 'react'
import { AreaChart, Area, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from 'recharts'
import { Image, Info } from 'lucide-react'

// Hours from 00:00 to 23:00
const hours = Array.from({ length: 24 }, (_, i) => {
  const hr = i.toString().padStart(2, '0')
  return `${hr}:00`
})

// Generate empty state data
const emptyData = hours.map((hour) => ({
  name: hour,
  sent: 0,
  errors: 0
}))

interface ChartCardProps {
  data?: any[]
}

export const ChartCard: React.FC<ChartCardProps> = ({ data: propData }) => {
  // Use propData if available, otherwise fallback to empty mode logic
  const data = propData && propData.length > 0 ? propData : emptyData

  return (
    <div className="dashboard-main-chart">
      <div className="chart-header">
        <div className="chart-title-area">
          <h2>Messages Sent Per Day</h2>
          <p>Daily volume trend</p>
          <div className="chart-badges">
            <span className="chart-badge blue">Lowest: ~0</span>
            <span className="chart-badge gray">Highest: ~0</span>
          </div>
        </div>
        <div className="chart-actions">
          <div className="chart-legend">
            <div className="legend-item">
              <span className="legend-color sent"></span>
              <span>Messages Sent</span>
            </div>
            <div className="legend-item">
              <span className="legend-color error"></span>
              <span>Errors</span>
            </div>
          </div>
          <button 
            className="btn btn-secondary btn-chart-image"
          >
            <Image size={14} /> Image
          </button>
        </div>
      </div>

      <div className="chart-body">
        {(!propData || propData.length === 0) ? (
          <div className="chart-empty-container">
            <div className="empty-state chart-empty-state">
              <Info className="empty-state-icon" />
              <h3 className="empty-state-title">No Data Available</h3>
              <p className="empty-state-desc">There are no messages recorded for this period.</p>
            </div>
          </div>
        ) : (
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart
              data={data}
              margin={{ top: 10, right: 10, left: -25, bottom: 0 }}
            >
              <defs>
                <linearGradient id="colorSent" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="var(--primary)" stopOpacity={0.4}/>
                  <stop offset="95%" stopColor="var(--primary)" stopOpacity={0.0}/>
                </linearGradient>
                <linearGradient id="colorErrors" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="var(--error)" stopOpacity={0.3}/>
                  <stop offset="95%" stopColor="var(--error)" stopOpacity={0.0}/>
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="var(--border-light)" />
              <XAxis 
                dataKey="name" 
                tickLine={false} 
                axisLine={{ stroke: 'var(--border-color)' }}
                tick={{ fontSize: 11, fill: 'var(--text-muted)' }}
              />
              <YAxis 
                domain={[0, 4]} 
                ticks={[0, 1, 2, 3, 4]}
                tickLine={false}
                axisLine={{ stroke: 'var(--border-color)' }}
                tick={{ fontSize: 11, fill: 'var(--text-muted)' }}
              />
              <Tooltip 
                contentStyle={{ 
                  backgroundColor: 'var(--bg-secondary)', 
                  border: '1px solid var(--border-color)',
                  borderRadius: 'var(--radius-md)',
                  boxShadow: 'var(--shadow-md)',
                  fontSize: '12px'
                }}
              />
              <Area 
                type="monotone" 
                dataKey="sent" 
                stroke="var(--primary)" 
                strokeWidth={2.5}
                fillOpacity={1} 
                fill="url(#colorSent)" 
                dot={{ stroke: 'var(--primary)', strokeWidth: 2, r: 4, fill: '#fff' }}
                activeDot={{ r: 6 }}
              />
              <Area 
                type="monotone" 
                dataKey="errors" 
                stroke="var(--error)" 
                strokeWidth={1.5}
                fillOpacity={1} 
                fill="url(#colorErrors)" 
                dot={{ stroke: 'var(--error)', strokeWidth: 1.5, r: 3, fill: '#fff' }}
                activeDot={{ r: 5 }}
              />
            </AreaChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  )
}
