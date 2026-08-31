import React, { useRef, useState } from 'react'
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, LabelList } from 'recharts'
import { motion } from 'framer-motion'
import { Image, Info, Loader2 } from 'lucide-react'
import toast from 'react-hot-toast'
import { buttonHoverProps } from '../utils/motion'
import { niceAxisScale } from '../utils/chartScale'

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

const COLOR_PRIMARY = '#3b82f6'
const COLOR_ERROR = '#dc2626'
const COLOR_BORDER = '#cbd5e1'
const COLOR_TEXT_MUTED = '#64748b'

export const ChartCard: React.FC<ChartCardProps> = React.memo(({ data: propData }) => {
  // Use propData if available, otherwise fallback to empty mode logic
  const data = propData && propData.length > 0 ? propData : emptyData
  const chartCardRef = useRef<HTMLDivElement>(null)
  const [isCapturing, setIsCapturing] = useState(false)

  const sentValues = data.map((d) => d.sent || 0)
  const errorValues = data.map((d) => d.errors || 0)
  const lowestSent = sentValues.length > 0 ? Math.min(...sentValues) : 0
  const highestSent = sentValues.length > 0 ? Math.max(...sentValues) : 0

  // Both series must be inside the domain. This used to be derived from `sent` alone, so any
  // hour where errors outnumbered messages sent drew the error line off the top of the plot.
  const highestPlotted = Math.max(highestSent, ...(errorValues.length > 0 ? errorValues : [0]))
  const { max: yAxisMax, ticks: yAxisTicks } = niceAxisScale(highestPlotted)

  // Point labels are readable at a low peak and turn into a wall of digits at a high one, where
  // the axis carries the reading anyway. 24 hourly buckets of a few dozen messages is the case
  // this dashboard actually shows.
  const showPointLabels = highestPlotted > 0 && highestPlotted <= 20

  /**
   * Draws a value label only where there is a value.
   *
   * Labelling all 24 buckets meant 23 zeros framing the one number that mattered — and on a
   * quiet day the zeros sat on the axis line, directly on top of the hour labels. A quiet hour
   * is already legible from the flat line; the label is for the peaks.
   */
  const renderPointLabel = (fill: string) => (props: any) => {
    const { x, y, value } = props
    if (!value) return null
    return (
      <text x={x} y={y - 8} fill={fill} fontSize={10} fontWeight={600} textAnchor="middle">
        {value}
      </text>
    )
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
            <LineChart
              data={data}
              /* bottom margin gives the 24 hour labels a band of their own — with 0 they were
                 pressed against the plot area and collided with anything drawn near the axis. */
              margin={{ top: 24, right: 14, left: -18, bottom: 8 }}
            >
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={COLOR_BORDER} />
              <XAxis
                dataKey="name"
                tickLine={false}
                axisLine={{ stroke: COLOR_BORDER }}
                /* dy pushes the labels clear of the axis line so they read as a separate band. */
                tick={{ fontSize: 10, fill: COLOR_TEXT_MUTED, dy: 4 }}
                /* interval=0 + minTickGap=0 renders all 24 hours. This was interval={2}, which
                   thinned the labels to every third hour (00, 03, 06 …) even though the data has
                   always carried 24 points. */
                interval={0}
                minTickGap={0}
                /* The buckets are "HH:00"; the axis shows just the hour so 24 labels fit, while
                   the tooltip keeps the full label. The API contract is unchanged. */
                tickFormatter={(value) => String(value).slice(0, 2)}
                /* No edge padding, so hour 00 sits on the first gridline and 23 on the last. */
                padding={{ left: 0, right: 0 }}
              />
              <YAxis
                domain={[0, yAxisMax]}
                ticks={yAxisTicks}
                allowDecimals={false}
                tickLine={false}
                axisLine={{ stroke: COLOR_BORDER }}
                tick={{ fontSize: 11, fill: COLOR_TEXT_MUTED }}
                width={40}
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
              <Line
                type="monotone"
                dataKey="sent"
                name="Messages Sent"
                stroke={COLOR_PRIMARY}
                strokeWidth={2.5}
                dot={{ r: 2.5, fill: COLOR_PRIMARY, strokeWidth: 0 }}
                activeDot={{ r: 5, stroke: '#fff', strokeWidth: 2 }}
                isAnimationActive={false}
              >
                {showPointLabels && (
                  <LabelList dataKey="sent" content={renderPointLabel(COLOR_PRIMARY)} />
                )}
              </Line>
              <Line
                type="monotone"
                dataKey="errors"
                name="Errors"
                stroke={COLOR_ERROR}
                strokeWidth={1.5}
                dot={{ r: 2, fill: COLOR_ERROR, strokeWidth: 0 }}
                activeDot={{ r: 4, stroke: '#fff', strokeWidth: 2 }}
                isAnimationActive={false}
              />
              {/* The errors series is deliberately unlabelled. It sits on or near zero for most
                  of the day, so labels below it landed on the hour labels — that was the
                  overlapping axis. Error counts are readable from the tooltip and the legend. */}
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  )
})

export default ChartCard



