import React from 'react'
import { motion } from 'framer-motion'
import { Megaphone, Info, MessageSquare, Mail, ArrowUpRight } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { formatRelativeTime } from '../utils/dateHelper'

interface TopCampaign {
  id: number
  name: string
  createdAt: string
  status: string
  channel?: string
  messages: number
  delivered: number
  deliveryRate: number
  readRate: number
}

interface TopCampaignsCardProps {
  data?: TopCampaign[]
}

const STATUS_CLASS: Record<string, string> = {
  Sent: 'success',
  Completed: 'success',
  Sending: 'info',
  Running: 'info',
  Active: 'info',
  Scheduled: 'info',
  Draft: 'neutral',
  Paused: 'warning',
  Failed: 'error',
  Cancelled: 'neutral'
}

const STATUS_LABEL: Record<string, string> = {
  Sent: 'Completed',
  Completed: 'Completed',
  Sending: 'Running',
  Running: 'Running',
  Active: 'Active',
  Scheduled: 'Scheduled',
  Draft: 'Draft',
  Paused: 'Paused',
  Failed: 'Failed',
  Cancelled: 'Cancelled'
}

const formatCreatedDate = (dateStr: string): string => {
  if (!dateStr) return '-'
  try {
    const d = new Date(dateStr)
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
  } catch {
    return dateStr
  }
}

export const TopCampaignsCard: React.FC<TopCampaignsCardProps> = React.memo(({ data }) => {
  const navigate = useNavigate()
  const campaigns = data && data.length > 0 ? data : []

  const renderChannelBadges = (channelStr?: string) => {
    const raw = (channelStr || 'whatsapp').toLowerCase()
    const isWhatsApp = raw === 'whatsapp' || raw === 'all' || raw === 'both'
    const isEmail = raw === 'email' || raw === 'all' || raw === 'both'

    return (
      <div className="campaign-channel-badges">
        {isWhatsApp && (
          <span className="channel-icon-badge whatsapp" title="WhatsApp">
            <MessageSquare size={13} />
          </span>
        )}
        {isEmail && (
          <span className="channel-icon-badge email" title="Email">
            <Mail size={13} />
          </span>
        )}
      </div>
    )
  }

  return (
    <div className="table-card top-campaigns-card">
      <div>
        <div className="table-card-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <div className="table-card-title">
              <Megaphone size={18} color="var(--primary)" />
              <span>Top Campaigns</span>
            </div>
            <p className="table-card-subtitle" style={{ fontSize: '12px', color: 'var(--text-muted)', marginTop: '4px' }}>
              Best performing campaigns across all channels
            </p>
          </div>
          <button
            type="button"
            className="table-card-view-all"
            onClick={() => navigate('/campaigns')}
            style={{
              background: 'none',
              border: 'none',
              color: 'var(--primary)',
              fontSize: '12px',
              fontWeight: 600,
              cursor: 'pointer',
              display: 'flex',
              alignItems: 'center',
              gap: '4px'
            }}
          >
            View All <ArrowUpRight size={13} />
          </button>
        </div>

        <div className="table-container">
          {campaigns.length === 0 ? (
            <div className="empty-state table-empty-padding">
              <Info className="empty-state-icon" />
              <h3 className="empty-state-title">No Data Available</h3>
              <p className="empty-state-desc">No campaign records could be retrieved from the database.</p>
            </div>
          ) : (
            <table className="dashboard-table">
              <thead>
                <tr>
                  <th>CAMPAIGN</th>
                  <th>CHANNELS</th>
                  <th>MESSAGES</th>
                  <th>DELIVERED</th>
                  <th>DELIVERY RATE</th>
                  <th>STATUS</th>
                  <th>CREATED ON</th>
                </tr>
              </thead>
              <tbody>
                {campaigns.map((row, index) => {
                  const statusClass = STATUS_CLASS[row.status] || 'neutral'
                  const statusLabel = STATUS_LABEL[row.status] || row.status
                  const createdFormatted = formatCreatedDate(row.createdAt)
                  const relativeTime = formatRelativeTime(row.createdAt)

                  return (
                    <motion.tr
                      key={row.id}
                      initial={{ opacity: 0, y: 8 }}
                      animate={{ opacity: 1, y: 0 }}
                      transition={{ delay: index * 0.05, duration: 0.2, ease: 'easeOut' }}
                      whileHover={{ backgroundColor: 'var(--border-light)' }}
                    >
                      <td>
                        <div className="campaign-name-cell">{row.name}</div>
                        <div className="campaign-created-cell">Created {relativeTime}</div>
                      </td>
                      <td>{renderChannelBadges(row.channel)}</td>
                      <td style={{ fontWeight: 600 }}>{row.messages.toLocaleString()}</td>
                      <td style={{ fontWeight: 600 }}>{row.delivered.toLocaleString()}</td>
                      <td>
                        <div className="table-progress-wrapper">
                          <progress
                            className={`app-progress ${row.deliveryRate > 0 ? 'green' : 'gray'}`}
                            value={row.deliveryRate}
                            max={100}
                          />
                          <span className="table-progress-text bold">{row.deliveryRate}%</span>
                        </div>
                      </td>
                      <td>
                        <span className={`status-pill status-pill-${statusClass}`}>{statusLabel}</span>
                      </td>
                      <td className="campaign-date-cell" style={{ fontSize: '12px', color: 'var(--text-muted)' }}>
                        {createdFormatted}
                      </td>
                    </motion.tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  )
})

export default TopCampaignsCard
