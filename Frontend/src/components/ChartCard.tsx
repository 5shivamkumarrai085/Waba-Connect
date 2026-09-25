import React, { useRef, useState, useMemo } from 'react'
import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  CartesianGrid
} from 'recharts'
import { motion } from 'framer-motion'
import { Image, Info, Loader2, ChevronDown } from 'lucide-react'
import toast from 'react-hot-toast'
import { buttonHoverProps } from '../utils/motion'
import { niceAxisScale } from '../utils/chartScale'

const COLOR_WHATSAPP = '#22c55e'
const COLOR_WHATSAPP_STROKE = '#16a34a'
const COLOR_EMAIL = '#3b82f6'
const COLOR_EMAIL_STROKE = '#2563eb'
const COLOR_BORDER = '#e2e8f0'
const COLOR_TEXT_MUTED = '#64748b'

interface ChartDataPoint {
  name: string
  fullDate?: string
  sent?: number
  whatsapp?: number
  email?: number
  errors?: number
  whatsappErrors?: number
  emailErrors?: number
  delivered?: number
  read?: number
}

interface ChartCardProps {
  data?: ChartDataPoint[]
  dailyData?: ChartDataPoint[]
}

type ChannelFilter = 'all' | 'whatsapp' | 'email'
type RangeFilter = '7days' | 'today' | '30days'

export const ChartCard: React.FC<ChartCardProps> = React.memo(({ data: hourlyData, dailyData }) => {
  const chartCardRef = useRef<HTMLDivElement>(null)
  const [isCapturing, setIsCapturing] = useState(false)
  const [channelFilter, setChannelFilter] = useState<ChannelFilter>('all')
  const [rangeFilter, setRangeFilter] = useState<RangeFilter>('7days')

  // Select dataset based on range filter
  const activeDataset: ChartDataPoint[] = useMemo(() => {
    if (rangeFilter === 'today') {
      if (hourlyData && hourlyData.length > 0) return hourlyData
      return Array.from({ length: 24 }, (_, i) => ({
        name: `${i.toString().padStart(2, '0')}:00`,
        fullDate: `Today at ${i.toString().padStart(2, '0')}:00`,
        sent: 0,
        whatsapp: 0,
        email: 0,
        errors: 0
      }))
    }

    if (dailyData && dailyData.length > 0) {
      return dailyData
    }

    if (hourlyData && hourlyData.length > 0) {
      return hourlyData
    }

    // Default 7 days placeholder if empty
    const days = ['Sep 1', 'Sep 2', 'Sep 3', 'Sep 4', 'Sep 5', 'Sep 6', 'Sep 7']
    return days.map(day => ({
      name: day,
      fullDate: `${day}, ${new Date().getFullYear()}`,
      sent: 0,
      whatsapp: 0,
      email: 0,
      errors: 0
    }))
  }, [rangeFilter, hourlyData, dailyData])

  // Compute highest plotted value for Y-axis scale based on channel filter
  const highestPlotted = useMemo(() => {
    return activeDataset.reduce((max, d) => {
      let val = 0
      if (channelFilter === 'all') {
        val = Math.max(d.sent || 0, (d.whatsapp || 0) + (d.email || 0), d.whatsapp || 0, d.email || 0)
      } else if (channelFilter === 'whatsapp') {
        val = d.whatsapp || 0
      } else if (channelFilter === 'email') {
        val = d.email || 0
      }
      return Math.max(max, val)
    }, 0)
  }, [activeDataset, channelFilter])

  const { max: yAxisMax, ticks: yAxisTicks } = niceAxisScale(highestPlotted)

  const formatYAxisTick = (val: number) => {
    if (val >= 1000) {
      return `${(val / 1000).toFixed(val % 1000 === 0 ? 0 : 1)}K`
    }
    return `${val}`
  }

  const handleDownloadScreenshot = async () => {
    if (!chartCardRef.current || isCapturing) return
    setIsCapturing(true)
    const toastId = toast.loading('Capturing screenshot...')

    try {
      const container = chartCardRef.current
      const svgElement = container.querySelector('.recharts-surface') as SVGElement | null

      if (!svgElement) {
        toast.error('Chart area not ready', { id: toastId })
        return
      }

      const rect = svgElement.getBoundingClientRect()
      const svgWidth = Math.max(rect.width || 800, 600)
      const svgHeight = Math.max(rect.height || 300, 250)

      const clonedSvg = svgElement.cloneNode(true) as SVGElement
      clonedSvg.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
      clonedSvg.setAttribute('width', `${svgWidth}`)
      clonedSvg.setAttribute('height', `${svgHeight}`)

      const styleEl = document.createElement('style')
      styleEl.textContent = `
        text { font-family: Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important; font-size: 11px !important; fill: #64748b !important; }
        .recharts-cartesian-grid-horizontal line { stroke: #e2e8f0 !important; stroke-dasharray: 3 3 !important; }
        .recharts-xAxis line, .recharts-yAxis line { stroke: #cbd5e1 !important; }
      `
      clonedSvg.insertBefore(styleEl, clonedSvg.firstChild)

      const svgData = new XMLSerializer().serializeToString(clonedSvg)
      const svgDataBase64 = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svgData)

      const img = new window.Image()

      await new Promise<void>((resolve, reject) => {
        img.onload = () => {
          try {
            const padding = 20
            const headerHeight = 60
            const canvasWidth = svgWidth + padding * 2
            const canvasHeight = svgHeight + headerHeight + padding * 2
            const scale = 2

            const canvas = document.createElement('canvas')
            canvas.width = canvasWidth * scale
            canvas.height = canvasHeight * scale
            const ctx = canvas.getContext('2d')

            if (ctx) {
              ctx.scale(scale, scale)

              ctx.fillStyle = '#ffffff'
              ctx.fillRect(0, 0, canvasWidth, canvasHeight)

              ctx.strokeStyle = '#e2e8f0'
              ctx.lineWidth = 1
              ctx.strokeRect(0, 0, canvasWidth, canvasHeight)

              ctx.fillStyle = '#1e293b'
              ctx.font = '600 16px Inter, system-ui, sans-serif'
              ctx.fillText('Message Volume Trend', padding, padding + 18)

              ctx.fillStyle = '#64748b'
              ctx.font = '400 12px Inter, system-ui, sans-serif'
              ctx.fillText('Total messages sent across all channels', padding, padding + 36)

              ctx.drawImage(img, padding, headerHeight + padding, svgWidth, svgHeight)

              const pngUrl = canvas.toDataURL('image/png')
              const downloadLink = document.createElement('a')
              downloadLink.href = pngUrl
              downloadLink.download = 'message-volume-trend.png'
              document.body.appendChild(downloadLink)
              downloadLink.click()
              document.body.removeChild(downloadLink)
            }
            resolve()
          } catch (err) {
            reject(err)
          }
        }
        img.onerror = (e) => reject(e)
        img.src = svgDataBase64
      })

      toast.success('Screenshot saved as message-volume-trend.png', { id: toastId })
    } catch (err) {
      console.error('Failed to capture chart screenshot:', err)
      toast.error('Failed to take screenshot', { id: toastId })
    } finally {
      setIsCapturing(false)
    }
  }

  // Custom Tooltip component matching reference image 3
  const CustomTooltip = ({ active, payload, label }: any) => {
    if (active && payload && payload.length) {
      const dataPoint = payload[0]?.payload as ChartDataPoint
      const displayDate = dataPoint?.fullDate || label

      return (
        <div className="chart-tooltip-box">
          <div className="chart-tooltip-title">{displayDate}</div>
          <div className="chart-tooltip-content">
            {(channelFilter === 'all' || channelFilter === 'whatsapp') && (
              <div className="chart-tooltip-row">
                <div className="chart-tooltip-label">
                  <span className="chart-tooltip-dot" style={{ backgroundColor: COLOR_WHATSAPP }} />
                  <span>WhatsApp</span>
                </div>
                <span className="chart-tooltip-value">
                  {(dataPoint?.whatsapp ?? 0).toLocaleString()}
                </span>
              </div>
            )}
            {(channelFilter === 'all' || channelFilter === 'email') && (
              <div className="chart-tooltip-row">
                <div className="chart-tooltip-label">
                  <span className="chart-tooltip-dot" style={{ backgroundColor: COLOR_EMAIL }} />
                  <span>Email</span>
                </div>
                <span className="chart-tooltip-value">
                  {(dataPoint?.email ?? 0).toLocaleString()}
                </span>
              </div>
            )}
          </div>
        </div>
      )
    }
    return null
  }

  const showWhatsApp = channelFilter === 'all' || channelFilter === 'whatsapp'
  const showEmail = channelFilter === 'all' || channelFilter === 'email'

  return (
    <div className="dashboard-main-chart" ref={chartCardRef}>
      <div className="chart-header">
        <div className="chart-title-area">
          <h2>Message Volume Trend</h2>
          <p>Total messages sent across all channels</p>
        </div>

        <div className="chart-actions">
          {/* Legend */}
          <div className="chart-legend">
            {showWhatsApp && (
              <div className="legend-item">
                <span className="legend-color" style={{ backgroundColor: COLOR_WHATSAPP }}></span>
                <span>WhatsApp</span>
              </div>
            )}
            {showEmail && (
              <div className="legend-item">
                <span className="legend-color" style={{ backgroundColor: COLOR_EMAIL }}></span>
                <span>Email</span>
              </div>
            )}
          </div>

          {/* Channel Dropdown */}
          <div className="chart-select-wrapper">
            <select
              className="chart-select-dropdown"
              value={channelFilter}
              onChange={(e) => setChannelFilter(e.target.value as ChannelFilter)}
              aria-label="Filter by channel"
            >
              <option value="all">All Channels</option>
              <option value="whatsapp">WhatsApp</option>
              <option value="email">Email</option>
            </select>
            <ChevronDown size={14} className="chart-select-chevron" />
          </div>

          {/* Range Dropdown */}
          <div className="chart-select-wrapper">
            <select
              className="chart-select-dropdown"
              value={rangeFilter}
              onChange={(e) => setRangeFilter(e.target.value as RangeFilter)}
              aria-label="Filter by time range"
            >
              <option value="7days">Last 7 Days</option>
              <option value="today">Today (Hourly)</option>
              <option value="30days">Last 30 Days</option>
            </select>
            <ChevronDown size={14} className="chart-select-chevron" />
          </div>

          {/* Image download button */}
          <motion.button
            className="btn btn-secondary btn-chart-image"
            onClick={handleDownloadScreenshot}
            disabled={isCapturing}
            title="Download screenshot of chart"
            {...buttonHoverProps}
          >
            {isCapturing ? <Loader2 size={14} className="animate-spin" /> : <Image size={14} />}
            Image
          </motion.button>
        </div>
      </div>

      <div className="chart-body" style={{ minHeight: '300px' }}>
        {activeDataset.length === 0 ? (
          <div className="chart-empty-container">
            <div className="empty-state chart-empty-state">
              <Info className="empty-state-icon" />
              <h3 className="empty-state-title">No Data Available</h3>
              <p className="empty-state-desc">There are no messages recorded for this period.</p>
            </div>
          </div>
        ) : (
          <ResponsiveContainer width="100%" height={300}>
            <AreaChart
              data={activeDataset}
              margin={{ top: 20, right: 14, left: -14, bottom: 8 }}
            >
              <defs>
                <linearGradient id="colorWhatsApp" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={COLOR_WHATSAPP} stopOpacity={0.25} />
                  <stop offset="95%" stopColor={COLOR_WHATSAPP} stopOpacity={0.0} />
                </linearGradient>
                <linearGradient id="colorEmail" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={COLOR_EMAIL} stopOpacity={0.25} />
                  <stop offset="95%" stopColor={COLOR_EMAIL} stopOpacity={0.0} />
                </linearGradient>
              </defs>

              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={COLOR_BORDER} />

              <XAxis
                dataKey="name"
                tickLine={false}
                axisLine={{ stroke: COLOR_BORDER }}
                tick={{ fontSize: 11, fill: COLOR_TEXT_MUTED, dy: 6 }}
                padding={{ left: 16, right: 16 }}
              />

              <YAxis
                domain={[0, yAxisMax]}
                ticks={yAxisTicks}
                tickFormatter={formatYAxisTick}
                allowDecimals={false}
                tickLine={false}
                axisLine={{ stroke: COLOR_BORDER }}
                tick={{ fontSize: 11, fill: COLOR_TEXT_MUTED }}
                width={36}
              />

              <Tooltip content={<CustomTooltip />} />

              {showWhatsApp && (
                <Area
                  type="monotone"
                  dataKey="whatsapp"
                  name="WhatsApp"
                  stroke={COLOR_WHATSAPP_STROKE}
                  strokeWidth={2.5}
                  fillOpacity={1}
                  fill="url(#colorWhatsApp)"
                  dot={{ r: 3, fill: COLOR_WHATSAPP_STROKE, strokeWidth: 0 }}
                  activeDot={{ r: 6, stroke: '#fff', strokeWidth: 2, fill: COLOR_WHATSAPP_STROKE }}
                  isAnimationActive={false}
                />
              )}

              {showEmail && (
                <Area
                  type="monotone"
                  dataKey="email"
                  name="Email"
                  stroke={COLOR_EMAIL_STROKE}
                  strokeWidth={2.5}
                  fillOpacity={1}
                  fill="url(#colorEmail)"
                  dot={{ r: 3, fill: COLOR_EMAIL_STROKE, strokeWidth: 0 }}
                  activeDot={{ r: 6, stroke: '#fff', strokeWidth: 2, fill: COLOR_EMAIL_STROKE }}
                  isAnimationActive={false}
                />
              )}
            </AreaChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  )
})

export default ChartCard
