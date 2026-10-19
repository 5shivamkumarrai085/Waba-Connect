import React, { useEffect, useState, lazy, Suspense } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { StatCard } from '../components/StatCard'
import { MessageSquare, Users, Megaphone, Send, CornerUpLeft, AlertTriangle, Plus, RefreshCw } from 'lucide-react'
import { realtimeService } from '../services/campaigns/campaignHubService'
import { getChannel } from '../types/channel'
import type { StatCardChannelSlice } from '../components/StatCard'
import { useDashboardStore } from '../store/dashboardStore'
import { Skeleton } from '../components/Skeleton'
import { useAuthStore } from '../store/authStore'
import {
  fadeSlideUp,
  staggerContainer,
  staggerChild,
  transitions,
  buttonHoverProps,
  pageTransitionProps
} from '../utils/motion'

// Lazy-loaded below-the-fold heavy charts and tables
const ChartCard = lazy(() => import('../components/ChartCard'))
const DeliveryRateCard = lazy(() => import('../components/DeliveryRateCard'))
const TopCampaignsCard = lazy(() => import('../components/TopCampaignsCard'))
const RecentActivityCard = lazy(() => import('../components/RecentActivityCard'))
const ChannelInsightsCard = lazy(() => import('../components/ChannelInsights/ChannelInsightsCard'))

const PERIOD_LABEL: Record<string, string | undefined> = {
  today: 'yesterday',
  week: 'last week',
  month: 'last month',
  all: undefined
}

/** Returns a human-friendly date-range string for the current filter, in the viewer's locale. */
const getDateRangeLabel = (filter: string): string => {
  const now = new Date()
  const fmt = (d: Date) =>
    d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })

  if (filter === 'today') {
    return fmt(now)
  }
  if (filter === 'week') {
    const start = new Date(now)
    start.setDate(now.getDate() - 6)
    return `${fmt(start)} – ${fmt(now)}`
  }
  if (filter === 'month') {
    const start = new Date(now.getFullYear(), now.getMonth(), 1)
    return `${fmt(start)} – ${fmt(now)}`
  }
  return 'All Time'
}

const FILTER_LABELS: Record<string, string> = {
  today: 'Today',
  week: 'Week',
  month: 'Month',
  all: 'All'
}

export const Dashboard: React.FC = () => {
  const currentUser = useAuthStore((state) => state.user)
  const navigate = useNavigate()

  // Atomic store selectors to prevent unnecessary parent re-renders
  const summary = useDashboardStore(state => state.summary)
  const metrics = useDashboardStore(state => state.metrics)
  const deliveryBreakdown = useDashboardStore(state => state.deliveryBreakdown)
  const readBreakdown = useDashboardStore(state => state.readBreakdown)
  const topCampaigns = useDashboardStore(state => state.topCampaigns)
  const recentActivity = useDashboardStore(state => state.recentActivity)
  const channelBreakdown = useDashboardStore(state => state.channelBreakdown)
  const isLoading = useDashboardStore(state => state.isLoading)
  const isBackgroundSyncing = useDashboardStore(state => state.isBackgroundSyncing)
  const loadDashboardData = useDashboardStore(state => state.loadDashboardData)
  const startPolling = useDashboardStore(state => state.startPolling)
  const dashboardTimeFilter = useDashboardStore(state => state.dashboardTimeFilter)
  const setDashboardTimeFilter = useDashboardStore(state => state.setDashboardTimeFilter)
  const loadError = useDashboardStore(state => state.loadError)
  const lastUpdatedAt = useDashboardStore(state => state.lastUpdatedAt)
  const [isLive, setIsLive] = useState(realtimeService.isConnected)

  useEffect(() => {
    void loadDashboardData()
  }, [loadDashboardData])

  // Real time: the server signals "dashboard changed" (debounced) whenever a message, reply,
  // contact or campaign changes, and the page refetches its own summary. After a reconnect it
  // reloads, because signals sent while offline are not replayed.
  useEffect(() => realtimeService.subscribeDashboard(() => void loadDashboardData(false)), [loadDashboardData])
  useEffect(() => realtimeService.onStateChange((state, { reconnected }) => {
    setIsLive(state === 'connected')
    if (reconnected) void loadDashboardData(false)
  }), [loadDashboardData])

  // Polling is the safety net, slowed right down while the live connection works.
  useEffect(() => startPolling(isLive), [dashboardTimeFilter, startPolling, isLive])

  // A tab that comes back into view catches up at once.
  useEffect(() => {
    const onVisible = () => { if (document.visibilityState === 'visible') void loadDashboardData(false) }
    document.addEventListener('visibilitychange', onVisible)
    return () => document.removeEventListener('visibilitychange', onVisible)
  }, [loadDashboardData])

  const handleNewCampaignClick = () => {
    navigate('/campaigns/campaign/create')
  }

  const periodLabel = PERIOD_LABEL[dashboardTimeFilter]
  const dateRangeLabel = getDateRangeLabel(dashboardTimeFilter)

  /**
   * The KPI headline numbers, and where each one came from per channel.
   *
   * Derived from `channelBreakdown` rather than from separate totals so the cards and the
   * Channel Performance table below cannot disagree — two independently-computed versions of
   * "delivered" is how a dashboard ends up contradicting itself on one screen.
   */
  const channelStats = React.useMemo(() => {
    const slice = (pick: (row: typeof channelBreakdown[number]) => number): StatCardChannelSlice[] =>
      channelBreakdown.map(row => ({
        key: row.channel,
        label: getChannel(row.channel)?.label ?? row.channel,
        value: pick(row),
        color: `var(${getChannel(row.channel)?.colorVar ?? '--primary'})`
      }))

    const sum = (pick: (row: typeof channelBreakdown[number]) => number) =>
      channelBreakdown.reduce((total, row) => total + pick(row), 0)

    const messages = sum(r => r.messages)

    // Rates are computed here, not read from the server, because they have to be rates *of these
    // numbers*. A percentage taken against a differently-scoped total is worse than no percentage.
    const rate = (part: number) => (messages > 0 ? Math.min(100, (part / messages) * 100).toFixed(1) : '0.0')

    return {
      messages,
      delivered: sum(r => r.delivered),
      failed: sum(r => r.failed),
      replies: sum(r => r.replies),
      rate,
      slice
    }
  }, [channelBreakdown])

  // The signed-in user's name, in the viewer's own time zone. This used to greet everyone as
  // "RMA" on India time regardless of who they were or where they sat.
  const localHour = new Date().getHours()
  const timeGreeting = localHour < 12 ? 'Good Morning' : localHour < 17 ? 'Good Afternoon' : 'Good Evening'
  const greeting = currentUser?.firstName ? `${timeGreeting}, ${currentUser.firstName}!` : `${timeGreeting}!`

  if (isLoading) {
    return (
      <div className="fade-in">
        {/* Welcome Banner Skeleton */}
        <div className="dashboard-welcome">
          <div className="dashboard-welcome-left">
            <Skeleton variant="title" width={280} height={32} />
            <Skeleton variant="text" width={400} />
          </div>
        </div>

        {/* Statistical Cards Grid Skeleton */}
        <div className="stat-cards-grid">
          <Skeleton variant="stat-card" count={6} />
        </div>

        {/* Main Hourly Chart Skeleton */}
        <Skeleton variant="chart" />

        {/* Tables Skeleton */}
        <div className="dashboard-tables-grid">
          <Skeleton variant="table" style={{ height: 380 }} />
          <Skeleton variant="table" style={{ height: 380 }} />
        </div>
      </div>
    )
  }

  return (
    <motion.div {...pageTransitionProps}>
      {/* ── Welcome Banner ──────────────────────────────────────────────────────── */}
      <motion.div
        className="dashboard-welcome"
        variants={fadeSlideUp}
        initial="hidden"
        animate="visible"
        transition={transitions.normal}
      >
        <div className="dashboard-welcome-left">
          <h1>
            {greeting}
            {isBackgroundSyncing && <span className="dashboard-sync-dot" title="Refreshing…" />}
          </h1>
          <p>Track, manage and grow your campaigns across all channels.</p>
          <div className="dashboard-hero-meta">
            <span className="dashboard-date-range">{dateRangeLabel}</span>
            <span
              className={`dashboard-live${isLive ? ' is-live' : ''}`}
              title={isLive
                ? 'Numbers update by themselves as messages, replies, contacts and campaigns change.'
                : 'Live updates are reconnecting; the numbers refresh on a timer meanwhile.'}
            >
              <span className="dashboard-live-dot" aria-hidden="true" />
              {isLive ? 'Live' : 'Auto-refresh'}
              {lastUpdatedAt && (
                <span className="dashboard-live-time">
                  · updated <time dateTime={new Date(lastUpdatedAt).toISOString()}>
                    {new Date(lastUpdatedAt).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                  </time>
                </span>
              )}
            </span>
          </div>
        </div>
        <div className="dashboard-welcome-right">
          <div className="dashboard-filter-tabs">
            {(['today', 'week', 'month', 'all'] as const).map(opt => (
              <button
                key={opt}
                type="button"
                className={`dashboard-filter-tab${dashboardTimeFilter === opt ? ' active' : ''}`}
                onClick={() => setDashboardTimeFilter(opt)}
              >
                {FILTER_LABELS[opt]}
              </button>
            ))}
          </div>
          <motion.button
            className="btn btn-primary"
            onClick={handleNewCampaignClick}
            {...buttonHoverProps}
          >
            <Plus size={16} /> New Campaign
          </motion.button>
        </div>
      </motion.div>

      {loadError && (
        <div className="dashboard-error" role="alert">
          <AlertTriangle size={16} aria-hidden="true" />
          <span>
            {summary ? 'The latest numbers could not be loaded; showing the last ones received.' : 'The dashboard could not be loaded.'}
            {' '}<span className="dashboard-error-detail">{loadError}</span>
          </span>
          <button type="button" className="btn btn-secondary" onClick={() => void loadDashboardData(false)}>
            <RefreshCw size={14} aria-hidden="true" /> Try Again
          </button>
        </div>
      )}

      {/* ── KPI strip: one row on desktop (see .stat-cards-grid) ──────────────── */}
      <motion.div
        className="stat-cards-grid"
        variants={staggerContainer(0.08, 0.1)}
        initial="hidden"
        animate="visible"
      >
        <motion.div variants={staggerChild}>
          <StatCard
            icon={<MessageSquare size={20} />}
            label="Total Messages"
            value={channelStats.messages}
            footnote="Across all channels"
            channels={channelStats.slice(r => r.messages)}
            colorClass="blue"
            changePercent={periodLabel ? metrics.messages.changePercent : undefined}
            periodLabel={periodLabel}
          />
        </motion.div>
        <motion.div variants={staggerChild}>
          <StatCard
            icon={<Send size={20} />}
            label="Delivered"
            value={channelStats.delivered}
            footnote={`${channelStats.rate(channelStats.delivered)}% delivery rate`}
            channels={channelStats.slice(r => r.delivered)}
            colorClass="green"
          />
        </motion.div>
        <motion.div variants={staggerChild}>
          <StatCard
            icon={<CornerUpLeft size={20} />}
            label="Replies"
            value={channelStats.replies}
            footnote={`${channelStats.rate(channelStats.replies)}% reply rate`}
            channels={channelStats.slice(r => r.replies)}
            colorClass="purple"
          />
        </motion.div>
        <motion.div variants={staggerChild}>
          <StatCard
            icon={<AlertTriangle size={20} />}
            label="Failed"
            value={channelStats.failed}
            footnote={`${channelStats.rate(channelStats.failed)}% failure rate`}
            channels={channelStats.slice(r => r.failed)}
            colorClass="red"
          />
        </motion.div>
        <motion.div variants={staggerChild}>
          {/* No channel split: a contact is a person, not a channel, and the same person is
              reachable on both. Splitting them would double-count. */}
          <StatCard
            icon={<Users size={20} />}
            label="Active Contacts"
            value={metrics.contacts.bottom}
            footnote="Engaged this period"
            colorClass="blue"
          />
        </motion.div>
        <motion.div variants={staggerChild}>
          <StatCard
            icon={<Megaphone size={20} />}
            label="Active Campaigns"
            value={metrics.campaigns.bottom}
            footnote="Sending or scheduled"
            colorClass="orange"
          />
        </motion.div>
      </motion.div>

      {/* ── Message Volume Trend + Channel Distribution (side-by-side) ────────── */}
      <div className="dashboard-main-grid">
        <Suspense fallback={<Skeleton variant="chart" />}>
          <motion.div
            className="dashboard-chart-col"
            variants={fadeSlideUp}
            initial="hidden"
            whileInView="visible"
            viewport={{ once: true, margin: '-40px' }}
            transition={{ ...transitions.normal, delay: 0.1 }}
          >
            <ChartCard data={summary?.hourlyChartData} dailyData={summary?.dailyChartData} />
          </motion.div>
        </Suspense>

        <div className="dashboard-side-column">
          <Suspense fallback={<Skeleton variant="chart" style={{ height: 260 }} />}>
            <motion.div
              className="dashboard-side-column-item"
              variants={fadeSlideUp}
              initial="hidden"
              whileInView="visible"
              viewport={{ once: true, margin: '-40px' }}
              transition={{ ...transitions.normal, delay: 0.1 }}
            >
              <DeliveryRateCard
                deliveryBreakdown={deliveryBreakdown}
                readBreakdown={readBreakdown}
                channelBreakdown={channelBreakdown}
              />
            </motion.div>
          </Suspense>
        </div>
      </div>

      {/* ── Channel Performance ─────────────────────────────────────────────────── */}
      <Suspense fallback={<Skeleton variant="table" style={{ height: 300 }} />}>
        <motion.div
          className="dashboard-channel-row"
          variants={fadeSlideUp}
          initial="hidden"
          whileInView="visible"
          viewport={{ once: true, margin: '-40px' }}
          transition={{ ...transitions.normal, delay: 0.1 }}
        >
          <ChannelInsightsCard data={channelBreakdown} />
        </motion.div>
      </Suspense>

      {/* ── Top Campaigns + Recent Activity ─────────────────────────────────────── */}
      <Suspense fallback={
        <div className="dashboard-tables-grid">
          <Skeleton variant="table" style={{ height: 380 }} />
          <Skeleton variant="table" style={{ height: 380 }} />
        </div>
      }>
        <motion.div
          className="dashboard-tables-grid"
          variants={staggerContainer(0.1, 0)}
          initial="hidden"
          whileInView="visible"
          viewport={{ once: true, margin: '-40px' }}
        >
          <motion.div variants={staggerChild} className="dashboard-tables-grid-main">
            <TopCampaignsCard data={topCampaigns} />
          </motion.div>
          <motion.div variants={staggerChild}>
            <RecentActivityCard data={recentActivity} />
          </motion.div>
        </motion.div>
      </Suspense>
    </motion.div>
  )
}
