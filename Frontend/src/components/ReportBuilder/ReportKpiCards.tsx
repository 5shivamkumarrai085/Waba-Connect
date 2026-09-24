import React from 'react'
import { CheckCheck, MessageSquare, Reply, Send, TrendingDown, TrendingUp, XCircle } from 'lucide-react'
import type { ReportKpi, ReportSummary } from '../../types/reporting'

/**
 * Icon and accent per measure.
 *
 * Keyed on the stable `key` the server sends, never on the label — a label is display text and may
 * be reworded, while the key is part of the contract. A measure the client does not recognise still
 * renders, with the neutral default, rather than disappearing from the row.
 */
const PRESENTATION: Record<string, { icon: React.ReactNode; tone: string }> = {
  total: { icon: <MessageSquare size={18} />, tone: 'primary' },
  delivered: { icon: <Send size={18} />, tone: 'info' },
  read: { icon: <CheckCheck size={18} />, tone: 'success' },
  responses: { icon: <Reply size={18} />, tone: 'warning' },
  failed: { icon: <XCircle size={18} />, tone: 'danger' },
}

const DEFAULT_PRESENTATION = { icon: <MessageSquare size={18} />, tone: 'primary' }

/**
 * A minimal inline sparkline.
 *
 * A dedicated charting library is not warranted for a 40x20 line with no axes, no tooltip and no
 * interaction — recharts is already used for the three cards beside the table, and pulling its
 * machinery in per KPI card would cost far more than this earns. Plain SVG, computed once per
 * render from whatever `trend` the server sent; a series too short to be a trend (0 or 1 point)
 * renders nothing; a flat series still draws a flat, honest line rather than being hidden.
 */
const Sparkline: React.FC<{ points: number[]; tone: string }> = ({ points, tone }) => {
  if (points.length < 2) return null

  const width = 64
  const height = 24
  const max = Math.max(...points)
  const min = Math.min(...points)
  const range = max - min || 1

  const path = points
    .map((value, index) => {
      const x = (index / (points.length - 1)) * width
      const y = height - ((value - min) / range) * height
      return `${index === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')

  return (
    <svg
      className={`report-kpi-sparkline is-${tone}`}
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      aria-hidden="true"
    >
      <path d={path} fill="none" strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

interface ReportKpiCardsProps {
  summary: ReportSummary | null
  isLoading: boolean
}

/**
 * One card per measure.
 *
 * <para>
 * The row is built from whatever the server returns — the cards are not listed here. Adding a
 * measure server-side puts a card on the page with no frontend change, and nothing on screen can
 * be a number this client made up.
 * </para>
 * <para>
 * The trend is shown only when there is a real comparison to make. With no date range there is no
 * preceding window, and the card shows the figure alone rather than an invented 0%.
 * </para>
 */
export const ReportKpiCards: React.FC<ReportKpiCardsProps> = ({ summary, isLoading }) => {
  // Skeletons match the real card's shape so the row does not jump when the numbers land.
  if (isLoading && !summary) {
    return (
      <div className="report-kpi-row">
        {Array.from({ length: 5 }).map((_, index) => (
          <div key={index} className="report-kpi-card is-loading" aria-hidden="true">
            <span className="report-kpi-icon" />
            <div className="report-kpi-body">
              <span className="report-kpi-label" />
              <span className="report-kpi-value" />
            </div>
          </div>
        ))}
      </div>
    )
  }

  const kpis = summary?.kpis ?? []
  if (kpis.length === 0) return null

  return (
    <div className="report-kpi-row">
      {kpis.map((kpi: ReportKpi) => {
        const { icon, tone } = PRESENTATION[kpi.key] ?? DEFAULT_PRESENTATION

        // A rise in failures is not good news, so the colour follows the meaning of the measure
        // rather than the sign of the number.
        const up = (kpi.changePercent ?? 0) > 0
        const favourable = kpi.key === 'failed' ? !up : up

        return (
          <article key={kpi.key} className={`report-kpi-card is-${tone}`}>
            <span className="report-kpi-icon">{icon}</span>

            <div className="report-kpi-body">
              <span className="report-kpi-label">{kpi.label}</span>

              <div className="report-kpi-figure">
                <span className="report-kpi-value">{kpi.value.toLocaleString()}</span>

                {kpi.changePercent !== null && (
                  <span className={`report-kpi-trend${favourable ? ' is-up' : ' is-down'}`}>
                    {up ? <TrendingUp size={12} /> : <TrendingDown size={12} />}
                    {Math.abs(kpi.changePercent)}%
                  </span>
                )}
              </div>

              {kpi.comparisonLabel && (
                <span className="report-kpi-compare">{kpi.comparisonLabel}</span>
              )}
            </div>

            {/* The card's own daily trend, from the same filtered window as the headline number —
                never a different series, never a placeholder shape. */}
            <Sparkline points={kpi.trend} tone={tone} />
          </article>
        )
      })}
    </div>
  )
}

export default ReportKpiCards
