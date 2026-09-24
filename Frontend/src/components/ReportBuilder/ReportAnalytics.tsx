import React, { useMemo } from 'react'
import {
  Area,
  AreaChart,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { ReportSummary } from '../../types/reporting'

/**
 * The chart palette.
 *
 * Read from the app's own CSS custom properties rather than written here, so these charts follow
 * the product's theme — including a change of brand colour or a switch to dark mode — instead of
 * carrying a second, hardcoded palette that slowly drifts away from everything around it.
 */
const readPalette = (): string[] => {
  if (typeof window === 'undefined') return []
  const styles = getComputedStyle(document.documentElement)

  return [
    '--primary',
    '--chart-2',
    '--chart-3',
    '--chart-4',
    '--chart-5',
  ]
    .map((token) => styles.getPropertyValue(token).trim())
    .filter(Boolean)
}

/** Fallbacks used only when a token is absent, kept in the same family as the brand blue. */
const FALLBACK_PALETTE = ['#2563eb', '#0ea5e9', '#22c55e', '#f59e0b', '#a855f7']

interface ReportAnalyticsProps {
  summary: ReportSummary | null
  isLoading: boolean
}

/** Compact thousands formatting for axis ticks and legend counts. */
const compact = (value: number): string =>
  value >= 1000 ? `${(value / 1000).toFixed(value >= 10_000 ? 0 : 1)}K` : String(value)

const shortDate = (iso: string): string => {
  const date = new Date(iso)
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleDateString(undefined, { month: 'short', day: '2-digit' })
}

/**
 * The three analytics cards beside the report: activity over time, the split by message type, and
 * the campaigns that account for the most of it.
 *
 * <para>
 * Every figure comes from the summary the server computed for the current filters. Nothing is
 * sampled from the rows on screen, which are only one page, and nothing is invented — a card with
 * no data says so rather than drawing an empty axis.
 * </para>
 */
/**
 * Loading placeholders shaped like the thing that is coming.
 *
 * Each of the three cards previously rendered the words "Loading…" inside a 28px-tall block, while
 * the content that replaced it is 150-190px tall. So every report run shoved the column down by
 * roughly 150px per card at the moment the data landed — the exact layout shift a skeleton exists
 * to prevent, caused by the loading state itself.
 *
 * These mirror the real geometry instead: the same heights, the same axis gutter, the same donut
 * radius, the same number of list rows. They are aria-hidden because the live region that announces
 * "loading" belongs to the control that started the run, not to three decorative shapes.
 */
const ActivitySkeleton: React.FC = () => (
  // 190px to match the AreaChart's ResponsiveContainer, and a left gutter matching the Y axis width.
  <div className="report-skeleton-chart" aria-hidden="true">
    <div className="report-skeleton-axis">
      {Array.from({ length: 4 }).map((_, i) => (
        <span key={i} className="skeleton-box variant-text skeleton-pulse" />
      ))}
    </div>
    <div className="report-skeleton-plot">
      {/* Varying heights: a flat row of identical bars reads as a rendered chart with no data,
          which is a different and more alarming message than "still loading". */}
      {[38, 62, 45, 78, 55, 88, 48, 70, 58, 82].map((h, i) => (
        <span key={i} className="skeleton-box skeleton-pulse" style={{ height: `${h}%` }} />
      ))}
    </div>
  </div>
)

const DonutSkeleton: React.FC = () => (
  <div className="report-donut-row" aria-hidden="true">
    <div className="report-skeleton-donut">
      {/* 132px across, matching outerRadius 66, so the ring lands exactly where the real one does. */}
      <span className="skeleton-box variant-circle skeleton-pulse" />
    </div>
    <ul className="report-skeleton-legend">
      {Array.from({ length: 3 }).map((_, i) => (
        <li key={i}>
          <span className="skeleton-box variant-circle skeleton-pulse" />
          <span className="skeleton-box variant-text skeleton-pulse" />
        </li>
      ))}
    </ul>
  </div>
)

const CampaignsSkeleton: React.FC = () => (
  <ol className="report-skeleton-campaigns" aria-hidden="true">
    {Array.from({ length: 5 }).map((_, i) => (
      <li key={i}>
        <span className="skeleton-box variant-circle skeleton-pulse" />
        <div>
          <span className="skeleton-box variant-text skeleton-pulse" />
          <span className="skeleton-box skeleton-pulse" />
        </div>
      </li>
    ))}
  </ol>
)

export const ReportAnalytics: React.FC<ReportAnalyticsProps> = ({ summary, isLoading }) => {
  const palette = useMemo(() => {
    const fromTheme = readPalette()
    return fromTheme.length >= 5 ? fromTheme : FALLBACK_PALETTE
  }, [])

  const activity = summary?.activity ?? []
  const byType = summary?.byType ?? []
  const topCampaigns = summary?.topCampaigns ?? []
  const busiest = Math.max(1, ...topCampaigns.map((c) => c.count))

  const activityData = useMemo(
    () => activity.map((point) => ({ label: shortDate(point.date), count: point.count })),
    [activity]
  )

  return (
    <aside className="report-analytics-col">
      {/* ── Message activity ─────────────────────────────────────────────── */}
      <section className="reporting-section-card report-analytics-card">
        <div className="report-analytics-head">
          <h3 className="report-analytics-title">Message Activity</h3>
          {summary && <span className="report-analytics-meta">{compact(summary.total)} total</span>}
        </div>

        {isLoading ? (
          <ActivitySkeleton />
        ) : activityData.length === 0 ? (
          <div className="report-analytics-empty">No activity in this period.</div>
        ) : (
          <div className="report-analytics-chart">
            <ResponsiveContainer width="100%" height={190}>
              <AreaChart data={activityData} margin={{ top: 6, right: 6, bottom: 0, left: -18 }}>
                <defs>
                  {/* A fill that fades out, so the line stays the thing being read. */}
                  <linearGradient id="reportActivityFill" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor={palette[0]} stopOpacity={0.28} />
                    <stop offset="100%" stopColor={palette[0]} stopOpacity={0} />
                  </linearGradient>
                </defs>

                <XAxis
                  dataKey="label"
                  tickLine={false}
                  axisLine={false}
                  tick={{ fontSize: 11, fill: 'var(--text-light)' }}
                  interval="preserveStartEnd"
                  minTickGap={18}
                />
                <YAxis
                  tickLine={false}
                  axisLine={false}
                  width={46}
                  tick={{ fontSize: 11, fill: 'var(--text-light)' }}
                  tickFormatter={compact}
                />
                <Tooltip
                  cursor={{ stroke: palette[0], strokeOpacity: 0.3 }}
                  contentStyle={{
                    borderRadius: 10,
                    border: '1px solid var(--border-color)',
                    background: 'var(--bg-secondary)',
                    fontSize: 12,
                  }}
                  labelStyle={{ color: 'var(--text-muted)' }}
                  formatter={(value) => [Number(value ?? 0).toLocaleString(), 'Messages']}
                />
                <Area
                  type="monotone"
                  dataKey="count"
                  stroke={palette[0]}
                  strokeWidth={2}
                  fill="url(#reportActivityFill)"
                />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        )}
      </section>

      {/* ── Message by type ──────────────────────────────────────────────── */}
      <section className="reporting-section-card report-analytics-card">
        <div className="report-analytics-head">
          <h3 className="report-analytics-title">Message by Type</h3>
        </div>

        {isLoading ? (
          <DonutSkeleton />
        ) : byType.length === 0 ? (
          <div className="report-analytics-empty">Nothing to break down yet.</div>
        ) : (
          <div className="report-donut-row">
            <div className="report-donut">
              <ResponsiveContainer width="100%" height={150}>
                <PieChart>
                  <Pie
                    data={byType}
                    dataKey="count"
                    nameKey="type"
                    innerRadius={44}
                    outerRadius={66}
                    paddingAngle={2}
                    stroke="none"
                  >
                    {byType.map((slice, index) => (
                      <Cell key={slice.type} fill={palette[index % palette.length]} />
                    ))}
                  </Pie>
                  <Tooltip
                    contentStyle={{
                      borderRadius: 10,
                      border: '1px solid var(--border-color)',
                      background: 'var(--bg-secondary)',
                      fontSize: 12,
                    }}
                    formatter={(value, name) => [Number(value ?? 0).toLocaleString(), String(name)]}
                  />
                </PieChart>
              </ResponsiveContainer>

              {/* The total sits in the hole rather than in a caption — it is what the ring sums to. */}
              <div className="report-donut-centre">
                <strong>{compact(summary?.total ?? 0)}</strong>
                <span>Total</span>
              </div>
            </div>

            <ul className="report-donut-legend">
              {byType.map((slice, index) => (
                <li key={slice.type}>
                  <span
                    className="report-donut-dot"
                    style={{ backgroundColor: palette[index % palette.length] }}
                  />
                  <span className="report-donut-label">{slice.type}</span>
                  <span className="report-donut-value">
                    {slice.percent}% ({slice.count.toLocaleString()})
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </section>

      {/* ── Top campaigns ────────────────────────────────────────────────── */}
      <section className="reporting-section-card report-analytics-card">
        <div className="report-analytics-head">
          <h3 className="report-analytics-title">Top Campaigns</h3>
        </div>

        {isLoading ? (
          <CampaignsSkeleton />
        ) : topCampaigns.length === 0 ? (
          <div className="report-analytics-empty">No campaign messages in this period.</div>
        ) : (
          <ol className="report-top-campaigns">
            {topCampaigns.map((campaign, index) => (
              <li key={campaign.campaignId}>
                <span className="report-top-rank">{index + 1}</span>

                <div className="report-top-body">
                  <div className="report-top-line">
                    <span className="report-top-name" title={campaign.name}>
                      {campaign.name}
                    </span>
                    <span className="report-top-count">{campaign.count.toLocaleString()}</span>
                  </div>

                  {/* Bar length is relative to the busiest campaign, so the list reads as a
                      ranking rather than as a share of some invisible whole. */}
                  <div className="report-top-bar">
                    <span style={{ width: `${Math.max(4, (campaign.count / busiest) * 100)}%` }} />
                  </div>
                </div>
              </li>
            ))}
          </ol>
        )}
      </section>
    </aside>
  )
}

export default ReportAnalytics
