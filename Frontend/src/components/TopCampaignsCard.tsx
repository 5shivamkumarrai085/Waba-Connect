import React from 'react'
import { motion } from 'framer-motion'
import { Megaphone, Info } from 'lucide-react'
import { formatRelativeTime } from '../utils/dateHelper'

interface TopCampaign {
  id: number
  name: string
  createdAt: string
  status: string
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
  Sending: 'info',
  Scheduled: 'info',
  Draft: 'neutral',
  Paused: 'warning',
  Failed: 'error',
  Cancelled: 'neutral'
}

const STATUS_LABEL: Record<string, string> = {
  Sent: 'Completed',
  Sending: 'Active',
  Scheduled: 'Scheduled',
  Draft: 'Draft',
  Paused: 'Paused',
  Failed: 'Failed',
  Cancelled: 'Cancelled'
}

export const TopCampaignsCard: React.FC<TopCampaignsCardProps> = React.memo(({ data }) => {
  const campaigns = data && data.length > 0 ? data : []

  return (
    <div className="table-card top-campaigns-card">
      <div>
        <div className="table-card-header">
          <div className="table-card-title">
            <Megaphone size={18} color="var(--primary)" />
            <span>Top Campaigns</span>
          </div>
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
                  <th>Campaign</th>
                  <th>Messages</th>
                  <th>Delivered</th>
                  <th>Delivery Rate</th>
                  <th>Read Rate</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {campaigns.map((row, index) => {
                  const statusClass = STATUS_CLASS[row.status] || 'neutral'
                  const statusLabel = STATUS_LABEL[row.status] || row.status
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
                        <div className="campaign-created-cell">Created {formatRelativeTime(row.createdAt)}</div>
                      </td>
                      <td>{row.messages}</td>
                      <td>{row.delivered}</td>
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
                      <td className="rate-cell-value-secondary">{row.readRate}%</td>
                      <td>
                        <span className={`status-pill status-pill-${statusClass}`}>{statusLabel}</span>
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
