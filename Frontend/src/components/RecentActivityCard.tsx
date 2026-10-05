import React from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Megaphone, UserPlus, FileText, Bot, Clock, Info, MessageSquare, Filter, Link2 } from 'lucide-react'
import { formatRelativeTime } from '../utils/dateHelper'
import Can from './Can/Can'

/** One audit event, as the dashboard summary returns it (type from RecentActivityCatalog). */
interface ActivityItem {
  type: string
  title: string
  /** Who did it — a person's name, or "System" for automatic actions. */
  subtitle: string | null
  timestamp: string
}

interface RecentActivityCardProps {
  data?: ActivityItem[]
}

const TYPE_ICON: Record<string, React.ComponentType<{ size?: number }>> = {
  campaign: Megaphone,
  contact: UserPlus,
  template: FileText,
  botflow: Bot,
  chat: MessageSquare,
  segment: Filter,
  connection: Link2
}

const TYPE_COLOR: Record<string, string> = {
  campaign: 'green',
  contact: 'blue',
  template: 'orange',
  botflow: 'purple',
  chat: 'blue',
  segment: 'purple',
  connection: 'green'
}

export const RecentActivityCard: React.FC<RecentActivityCardProps> = React.memo(({ data }) => {
  const navigate = useNavigate()
  const items = data && data.length > 0 ? data : []

  return (
    <div className="table-card recent-activity-card">
      <div>
        <div className="table-card-header recent-activity-header">
          <div className="table-card-title">
            <Clock size={18} color="var(--primary)" />
            <span>Recent Activity</span>
          </div>
          <Can permission="ActivityLog.View">
            <button type="button" className="delivery-rate-view-report" onClick={() => navigate('/audit-log?tab=audits')}>
              View All
            </button>
          </Can>
        </div>

        {items.length === 0 ? (
          <div className="empty-state table-empty-padding">
            <Info className="empty-state-icon" />
            <h3 className="empty-state-title">No Recent Activity</h3>
            <p className="empty-state-desc">Activity will show up here as you use the app.</p>
          </div>
        ) : (
          <div className="recent-activity-list">
            {items.map((item, index) => {
              const Icon = TYPE_ICON[item.type] || Info
              const colorClass = TYPE_COLOR[item.type] || 'blue'
              return (
                <motion.div
                  className="recent-activity-item"
                  key={`${item.type}-${item.timestamp}-${index}`}
                  initial={{ opacity: 0, y: 6 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: index * 0.04, duration: 0.2, ease: 'easeOut' }}
                >
                  <span className={`recent-activity-icon ${colorClass}`}>
                    <Icon size={15} aria-hidden="true" />
                  </span>
                  <div className="recent-activity-content">
                    <div className="recent-activity-title">{item.title}</div>
                    {item.subtitle && <div className="recent-activity-subtitle">{item.subtitle}</div>}
                    <div className="recent-activity-time">{formatRelativeTime(item.timestamp)}</div>
                  </div>
                </motion.div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
})

export default RecentActivityCard
