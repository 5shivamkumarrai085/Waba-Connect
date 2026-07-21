import React, { useEffect, lazy, Suspense } from 'react'
import { useNavigate } from 'react-router-dom'
import { StatCard } from '../components/StatCard'
import { MessageSquare, Users, Megaphone, FileText, Plus } from 'lucide-react'
import { useDashboardStore } from '../store/dashboardStore'
import { Skeleton } from '../components/Skeleton'
import { FilterBar } from '../components/FilterBar/FilterBar'

// Lazy-loaded below-the-fold heavy charts and tables
const ChartCard = lazy(() => import('../components/ChartCard'))
const TrendCard = lazy(() => import('../components/TrendCard'))
const TableCard = lazy(() => import('../components/TableCard'))

export const Dashboard: React.FC = () => {
  const navigate = useNavigate()
  
  // Atomic store selectors to prevent unnecessary parent re-renders
  const summary = useDashboardStore(state => state.summary)
  const metrics = useDashboardStore(state => state.metrics)
  const isLoading = useDashboardStore(state => state.isLoading)
  const loadDashboardData = useDashboardStore(state => state.loadDashboardData)
  const dashboardTimeFilter = useDashboardStore(state => state.dashboardTimeFilter)
  const setDashboardTimeFilter = useDashboardStore(state => state.setDashboardTimeFilter)

  useEffect(() => {
    loadDashboardData()
  }, [])

  const handleNewCampaignClick = () => {
    navigate('/campaigns/campaign')
  }

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

        {/* Trends Sub-charts Skeleton */}
        <div className="dashboard-trends-grid">
          <Skeleton variant="chart" style={{ height: 260 }} />
          <Skeleton variant="chart" style={{ height: 260 }} />
        </div>

        {/* Campaign Statistics Tables Skeleton */}
        <div className="dashboard-tables-grid">
          <Skeleton variant="table" style={{ height: 380 }} />
          <Skeleton variant="table" style={{ height: 380 }} />
        </div>
      </div>
    )
  }

  return (
    <div className="fade-in">
      {/* Welcome Banner */}
      <div className="dashboard-welcome">
        <div className="dashboard-welcome-left">
          <h1>Welcome Back, super ! 👋</h1>
          <p>Here's what's happening with your WhatsApp business today.</p>
        </div>
        <div className="dashboard-welcome-right">
          <FilterBar
            options={['today', 'week', 'month', 'all']}
            activeOption={dashboardTimeFilter}
            onChange={setDashboardTimeFilter}
          />
          <button className="btn btn-primary" onClick={handleNewCampaignClick}>
            <Plus size={16} /> New Campaign
          </button>
        </div>
      </div>

      {/* Statistical Cards Grid */}
      <div className="stat-cards-grid">
        <StatCard
          icon={<MessageSquare size={20} />}
          label="Total Messages"
          value={metrics.messages.total}
          bottomLabel="Today"
          bottomValue={metrics.messages.today}
          colorClass="blue"
        />
        <StatCard
          icon={<Users size={20} />}
          label="Total Contacts"
          value={metrics.contacts.total}
          bottomLabel="Active"
          bottomValue={metrics.contacts.active}
          colorClass="purple"
        />
        <StatCard
          icon={<Megaphone size={20} />}
          label="Total Campaigns"
          value={metrics.campaigns.total}
          bottomLabel="Active"
          bottomValue={metrics.campaigns.active}
          colorClass="green"
        />
        <StatCard
          icon={<FileText size={20} />}
          label="Total Templates"
          value={metrics.templates.total}
          bottomLabel="Approved"
          bottomValue={metrics.templates.approved}
          colorClass="orange"
        />
      </div>

      {/* Below-the-fold lazy loaded sections */}
      <Suspense fallback={<Skeleton variant="chart" />}>
        {/* Main Hourly Chart */}
        <ChartCard data={summary?.hourlyChartData} />
      </Suspense>

      <Suspense fallback={
        <div className="dashboard-trends-grid">
          <Skeleton variant="chart" style={{ height: 260 }} />
          <Skeleton variant="chart" style={{ height: 260 }} />
        </div>
      }>
        {/* Trends Sub-charts (Delivery & Read trends) */}
        <div className="dashboard-trends-grid">
          <TrendCard 
            type="delivery" 
            value={summary?.overallDeliveryRate !== undefined ? `${summary.overallDeliveryRate}%` : undefined} 
            data={summary?.deliveryTrend} 
          />
          <TrendCard 
            type="read" 
            value={summary?.overallReadRate !== undefined ? `${summary.overallReadRate}%` : undefined} 
            data={summary?.readTrend} 
          />
        </div>
      </Suspense>

      <Suspense fallback={
        <div className="dashboard-tables-grid">
          <Skeleton variant="table" style={{ height: 380 }} />
          <Skeleton variant="table" style={{ height: 380 }} />
        </div>
      }>
        {/* Campaign Statistics Tables */}
        <div className="dashboard-tables-grid">
          <TableCard type="read-rate" data={summary?.topReadRateCampaigns} />
          <TableCard type="delivery-rate" data={summary?.topDeliveryRateCampaigns} />
        </div>
      </Suspense>
    </div>
  )
}
