import React, { useRef, useState } from 'react'
import { AreaChart, Area, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from 'recharts'
import { motion } from 'framer-motion'
import { Image, Info, Loader2 } from 'lucide-react'
import toast from 'react-hot-toast'
import { buttonHoverProps } from '../utils/motion'

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

const COLOR_PRIMARY = '#6366f1'
const COLOR_ERROR = '#ef4444'
const COLOR_BORDER = '#cbd5e1'
const COLOR_TEXT_MUTED = '#64748b'

export const ChartCard: React.FC<ChartCardProps> = React.memo(({ data: propData }) => {
  // Use propData if available, otherwise fallback to empty mode logic
  const data = propData && propData.length > 0 ? propData : emptyData
  const chartCardRef = useRef<HTMLDivElement>(null)
  const [isCapturing, setIsCapturing] = useState(false)

  const sentValues = data.map((d) => d.sent || 0)
  const lowestSent = sentValues.length > 0 ? Math.min(...sentValues) : 0
  const highestSent = sentValues.length > 0 ? Math.max(...sentValues) : 0
  const yAxisMax = Math.max(highestSent, 1)

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

      // 1. Get accurate SVG bounding dimensions
      const rect = svgElement.getBoundingClientRect()
      const svgWidth = Math.max(rect.width || 800, 600)
      const svgHeight = Math.max(rect.height || 300, 250)

      // 2. Clone SVG and set required XML attributes for standalone rasterization
      const clonedSvg = svgElement.cloneNode(true) as SVGElement
      clonedSvg.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
      clonedSvg.setAttribute('width', `${svgWidth}`)
      clonedSvg.setAttribute('height', `${svgHeight}`)

      // Ensure text & line styles are explicitly set inside cloned SVG
      const styleEl = document.createElement('style')
      styleEl.textContent = `
        text { font-family: Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif !important; font-size: 11px !important; fill: #64748b !important; }
        .recharts-cartesian-grid-horizontal line { stroke: #cbd5e1 !important; stroke-dasharray: 3 3 !important; }
        .recharts-xAxis line, .recharts-yAxis line { stroke: #cbd5e1 !important; }
      `
      clonedSvg.insertBefore(styleEl, clonedSvg.firstChild)

      // 3. Serialize SVG to Data URL
      const svgData = new XMLSerializer().serializeToString(clonedSvg)
      const svgDataBase64 = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svgData)

      // 4. Create image & draw to Canvas with header & white background
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

              // White background card
              ctx.fillStyle = '#ffffff'
              ctx.fillRect(0, 0, canvasWidth, canvasHeight)

              // Border outline
              ctx.strokeStyle = '#e2e8f0'
              ctx.lineWidth = 1
              ctx.strokeRect(0, 0, canvasWidth, canvasHeight)

              // Draw title "Messages Sent Overview"
              ctx.fillStyle = '#1e293b'
              ctx.font = '600 16px Inter, system-ui, sans-serif'
              ctx.fillText('Messages Sent Overview', padding, padding + 18)

              // Draw subtitle "Hourly volume trend"
              ctx.fillStyle = '#64748b'
              ctx.font = '400 12px Inter, system-ui, sans-serif'
              ctx.fillText('Hourly volume trend', padding, padding + 36)

              // Draw legend indicators at top-right
              const legendY = padding + 22
              const legendX = canvasWidth - padding - 180

              // Messages Sent legend item (purple)
              ctx.fillStyle = COLOR_PRIMARY
              ctx.beginPath()
              ctx.arc(legendX, legendY - 4, 5, 0, Math.PI * 2)
              ctx.fill()

              ctx.fillStyle = '#475569'
              ctx.font = '500 12px Inter, system-ui, sans-serif'
              ctx.fillText('Messages Sent', legendX + 10, legendY)

              // Errors legend item (red)
              ctx.fillStyle = COLOR_ERROR
              ctx.beginPath()
              ctx.arc(legendX + 110, legendY - 4, 5, 0, Math.PI * 2)
              ctx.fill()

              ctx.fillStyle = '#475569'
              ctx.fillText('Errors', legendX + 120, legendY)

              // Draw SVG Chart graph
              ctx.drawImage(img, padding, headerHeight + padding, svgWidth, svgHeight)

              // Generate PNG URL & download link
              const pngUrl = canvas.toDataURL('image/png')
              const downloadLink = document.createElement('a')
              downloadLink.href = pngUrl
              downloadLink.download = 'message-stats-chart.png'
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

      toast.success('Screenshot saved as message-stats-chart.png', { id: toastId })
    } catch (err) {
      console.error('Failed to capture chart screenshot:', err)
      toast.error('Failed to take screenshot', { id: toastId })
    } finally {
      setIsCapturing(false)
    }
  }

  return (
    <div className="dashboard-main-chart" ref={chartCardRef}>
      <div className="chart-header">
        <div className="chart-title-area">
          <h2>Messages Sent Overview</h2>
          <p>Hourly volume trend</p>
          <div className="chart-badges">
            <span className="chart-badge blue">Lowest: {lowestSent}</span>
            <span className="chart-badge gray">Highest: {highestSent}</span>
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
        {(!propData || propData.length === 0) ? (
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
              data={data}
              margin={{ top: 10, right: 10, left: -25, bottom: 0 }}
            >
              <defs>
                <linearGradient id="colorSent" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={COLOR_PRIMARY} stopOpacity={0.4}/>
                  <stop offset="95%" stopColor={COLOR_PRIMARY} stopOpacity={0.0}/>
                </linearGradient>
                <linearGradient id="colorErrors" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor={COLOR_ERROR} stopOpacity={0.3}/>
                  <stop offset="95%" stopColor={COLOR_ERROR} stopOpacity={0.0}/>
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={COLOR_BORDER} />
              <XAxis 
                dataKey="name" 
                tickLine={false} 
                axisLine={{ stroke: COLOR_BORDER }}
                tick={{ fontSize: 11, fill: COLOR_TEXT_MUTED }}
              />
              <YAxis
                domain={[0, yAxisMax]}
                allowDecimals={false}
                tickLine={false}
                axisLine={{ stroke: COLOR_BORDER }}
                tick={{ fontSize: 11, fill: COLOR_TEXT_MUTED }}
              />
              <Tooltip 
                contentStyle={{ 
                  backgroundColor: '#ffffff', 
                  border: `1px solid ${COLOR_BORDER}`,
                  borderRadius: '8px',
                  boxShadow: '0 4px 12px rgba(0,0,0,0.1)',
                  fontSize: '12px'
                }}
              />
              <Area 
                type="monotone" 
                dataKey="sent" 
                stroke={COLOR_PRIMARY} 
                strokeWidth={2.5}
                fillOpacity={1} 
                fill="url(#colorSent)" 
                dot={{ stroke: COLOR_PRIMARY, strokeWidth: 2, r: 4, fill: '#fff' }}
                activeDot={{ r: 6 }}
              />
              <Area 
                type="monotone" 
                dataKey="errors" 
                stroke={COLOR_ERROR} 
                strokeWidth={1.5}
                fillOpacity={1} 
                fill="url(#colorErrors)" 
                dot={{ stroke: COLOR_ERROR, strokeWidth: 1.5, r: 3, fill: '#fff' }}
                activeDot={{ r: 5 }}
              />
            </AreaChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  )
})

export default ChartCard



