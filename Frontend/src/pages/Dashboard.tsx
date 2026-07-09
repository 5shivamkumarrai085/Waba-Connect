import React, { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { StatCard } from '../components/StatCard'
import { ChartCard } from '../components/ChartCard'
import { TrendCard } from '../components/TrendCard'
import { TableCard } from '../components/TableCard'
import { MessageSquare, Users, Megaphone, FileText, Plus } from 'lucide-react'
import { dashboardService } from '../services/dashboard/dashboardService'
import { templateService } from '../services/templates/templateService'

export const Dashboard: React.FC = () => {
  const navigate = useNavigate()
  const [loading, setLoading] = useState(true)
  const [summary, setSummary] = useState<any>(null)
  const [metrics, setMetrics] = useState({
    messages: { total: 0, today: 0 },
    contacts: { total: 0, active: 0 },
    campaigns: { total: 0, active: 0 },
    templates: { total: 0, approved: 0 }
  })

  useEffect(() => {
    const fetchDashboardData = async () => {
      try {
        setLoading(true)
        const summaryRes = await dashboardService.getSummary()
        const templatesRes = await templateService.getTemplates()

        const sum = summaryRes?.data || {}
        setSummary(sum)
        const templates = templatesRes || []
        
        const approvedTemplates = templates.filter((t: any) => t.status === 'APPROVED').length

        setMetrics({
          messages: {
            total: sum.messagesSent || 0,
            today: sum.hourlyChartData ? sum.hourlyChartData.reduce((acc: number, val: any) => acc + (val.sent || 0), 0) : 0
          },
          contacts: {
            total: sum.totalContacts || 0,
            active: sum.totalContacts || 0 
          },
          campaigns: {
            total: sum.totalCampaigns || 0,
            active: (sum.recentCampaigns || []).filter((c: any) => c.status === 'Running' || c.status === 'Scheduled' || c.status === 'Sending').length || 0
          },
          templates: {
            total: templates.length || 0,
            approved: approvedTemplates
          }
        })
      } catch (e) {
        console.error("Failed to load dashboard data", e)
      } finally {
        setLoading(false)
      }
    }
    fetchDashboardData()
  }, [])

  const handleNewCampaignClick = () => {
    navigate('/campaigns/campaign')
  }

  if (loading) {
    return <div className="fade-in page-loader"><p className="page-loader-text">Loading Dashboard...</p></div>
  }

  return (
    <div className="fade-in">
      {/* Welcome Banner */}
      <div className="dashboard-welcome">
        <div className="dashboard-welcome-left">
          <h1>Welcome Back, super ! 👋</h1>
          <p>Here's what's happening with your WhatsApp business today.</p>
        </div>
        <div>
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

      {/* Main Hourly Chart */}
      <ChartCard data={summary?.hourlyChartData} />

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

      {/* Campaign Statistics Tables */}
      <div className="dashboard-tables-grid">
        <TableCard type="read-rate" data={summary?.topReadRateCampaigns} />
        <TableCard type="delivery-rate" data={summary?.topDeliveryRateCampaigns} />
      </div>
    </div>
  )
}
