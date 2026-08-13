import React, { useEffect, lazy, Suspense } from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { StatCard } from '../components/StatCard'
import { MessageSquare, Users, Megaphone, FileText, Plus } from 'lucide-react'
import { useDashboardStore } from '../store/dashboardStore'
import { Skeleton } from '../components/Skeleton'
import { FilterBar } from '../components/FilterBar/FilterBar'
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

const PERIOD_LABEL: Record<string, string | undefined> = {
  today: 'yesterday',
  week: 'last week',
  month: 'last month',
  all: undefined
}

export const Dashboard: React.FC = () => {
  const navigate = useNavigate()

  // Atomic store selectors to prevent unnecessary parent re-renders
  const summary = useDashboardStore(state => state.summary)
  const metrics = useDashboardStore(state => state.metrics)
  const deliveryBreakdown = useDashboardStore(state => state.deliveryBreakdown)
  const readBreakdown = useDashboardStore(state => state.readBreakdown)
  const topCampaigns = useDashboardStore(state => state.topCampaigns)
  const recentActivity = useDashboardStore(state => state.recentActivity)
  const isLoading = useDashboardStore(state => state.isLoading)
  const isBackgroundSyncing = useDashboardStore(state => state.isBackgroundSyncing)
  const loadDashboardData = useDashboardStore(state => state.loadDashboardData)
  const startPolling = useDashboardStore(state => state.startPolling)
  const dashboardTimeFilter = useDashboardStore(state => state.dashboardTimeFilter)
  const setDashboardTimeFilter = useDashboardStore(state => state.setDashboardTimeFilter)

  useEffect(() => {
    loadDashboardData()
  }, [])

  // Keep the dashboard live: re-poll on an interval matched to the active filter's cache TTL.
  useEffect(() => {
    const stopPolling = startPolling()
    return stopPolling
  }, [dashboardTimeFilter, startPolling])

  const handleNewCampaignClick = () => {
    navigate('/campaigns/campaign/create')
  }

  const periodLabel = PERIOD_LABEL[dashboardTimeFilter]
  const istHour = Number(
    new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Kolkata', hour: 'numeric', hourCycle: 'h23' }).format(new Date())
  )
  const timeGreeting = istHour < 12 ? 'Good Morning' : istHour < 17 ? 'Good Afternoon' : 'Good Evening'
  const greeting = `${timeGreeting}, RMA! 👋`

  if (isLoading) {
    return (
      <div className="fade-in">
        {/* Welcome Banner Skeleton */}
        <div className="dashboard-welcome" style={{ marginBottom: 24 }}>
          <div className="dashboard-welcome-left">
            <Skeleton variant="title" width={280} height={32} />
            <Skeleton variant="text" width={400} style={{ marginTop: 8 }} />
          </div>
        </div>

        {/* Statistical Cards Grid Skeleton */}
        <div className="stat-cards-grid">
          <Skeleton variant="stat-card" count={4} />
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
      {/* Welcome Banner */}
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
          <p>Here's what's happening with your WhatsApp business today.</p>
        </div>
        <div className="dashboard-welcome-right">
          <FilterBar
            options={['today', 'week', 'month', 'all']}
            activeOption={dashboardTimeFilter}
            onChange={setDashboardTimeFilter}
          />
          <motion.button
            className="btn btn-primary"
            onClick={handleNewCampaignClick}
            {...buttonHoverProps}
          >
            <Plus size={16} /> New Campaign
          </motion.button>
        </div>
      </motion.div>

      {/* Statistical Cards Grid — Staggered entrance */}
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
            value={metrics.messages.total}
            bottomLabel="Delivered"
            bottomValue={metrics.messages.bottom}
            colorClass="blue"
            changePercent={periodLabel ? metrics.messages.changePercent : undefined}
            periodLabel={periodLabel}
          />
        </motion.div>
        <motion.div variants={staggerChild}>
          <StatCard
            icon={<Users size={20} />}
            label="Total Contacts"
            value={metrics.contacts.total}
            bottomLabel="Active"
            bottomValue={metrics.contacts.bottom}
            colorClass="purple"
            changePercent={periodLabel ? metrics.contacts.changePercent : undefined}
            periodLabel={periodLabel}
          />
        </motion.div>
        <motion.div variants={staggerChild}>
          <StatCard
            icon={<Megaphone size={20} />}
            label="Total Campaigns"
            value={metrics.campaigns.total}
            bottomLabel="Active"
            bottomValue={metrics.campaigns.bottom}
            colorClass="green"
            changePercent={periodLabel ? metrics.campaigns.changePercent : undefined}
            periodLabel={periodLabel}
          />
        </motion.div>
        <motion.div variants={staggerChild}>
          <StatCard
            icon={<FileText size={20} />}
            label="Total Templates"
            value={metrics.templates.total}
            bottomLabel="Approved"
            bottomValue={metrics.templates.bottom}
            colorClass="orange"
            changePercent={periodLabel ? metrics.templates.changePercent : undefined}
            periodLabel={periodLabel}
          />
        </motion.div>
      </motion.div>

      {/* Below-the-fold lazy loaded sections */}
      <div className="dashboard-main-grid">
        <Suspense fallback={<Skeleton variant="chart" />}>
          <motion.div
            variants={fadeSlideUp}
            initial="hidden"
            whileInView="visible"
            viewport={{ once: true, margin: '-40px' }}
            transition={{ ...transitions.normal, delay: 0.1 }}
          >
            <ChartCard data={summary?.hourlyChartData} />
          </motion.div>
        </Suspense>

        {/* Delivery Rate is now the only occupant of this column — Quick Actions was removed.
            The column stretches so the donut card matches the chart's height rather than
            leaving a tall gap beside it. */}
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
              <DeliveryRateCard deliveryBreakdown={deliveryBreakdown} readBreakdown={readBreakdown} />
            </motion.div>
          </Suspense>
        </div>
      </div>

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
