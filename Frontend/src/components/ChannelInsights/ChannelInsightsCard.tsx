import React from 'react'
import { useNavigate } from 'react-router-dom'
import { PieChart, Pie, Cell, ResponsiveContainer } from 'recharts'
import { Info, ArrowUpRight, Radio } from 'lucide-react'
import { getChannel, normalizeChannel } from '../../types/channel'
import './ChannelInsights.css'

/**
 * One channel's row of `summary.channelBreakdown`, as the dashboard endpoint returns it.
 *
 * Every channel is present even with no traffic, so a zero row here means "nothing sent",
 * never "no such channel" — which is why the table renders zero rows rather than filtering
 * them out.
 */
export interface ChannelBreakdownRow {
  channel: string
  messages: number
  delivered: number
  failed: number
  read: number
  pending: number
  suppressed: number
  /** Inbound messages on this channel in the period. */
  replies: number
  deliveryRate: number
  /** Replies as a share of what was sent. */
  replyRate: number
  sharePercent: number
}

interface ChannelInsightsCardProps {
  data?: ChannelBreakdownRow[]
}

/**
 * Channel Distribution + Channel Performance (reference image 14).
 *
 * The two live in one card because they are two readings of the same numbers: the donut answers
 * "where is the volume going" and the table answers "is each channel healthy". Splitting them
 * into separate cards made the second look like it was about something else.
 */
export const ChannelInsightsCard: React.FC<ChannelInsightsCardProps> = React.memo(({ data }) => {
  const navigate = useNavigate()

  const rows = (data ?? []).map((row) => {
    const definition = getChannel(row.channel)
    return {
      ...row,
      key: normalizeChannel(row.channel),
      label: definition?.label ?? row.channel,
      // Resolved through the token rather than a literal so the donut and the table agree with
      // the channel colours used by the wizard, the inbox and the connections list.
      color: `var(${definition?.colorVar ?? '--primary'})`,
    }
  })

  const totalMessages = rows.reduce((sum, row) => sum + row.messages, 0)

  // Recharts renders nothing for an all-zero dataset, and an empty donut with a populated legend
  // reads as a broken chart rather than as an idle account.
  const hasTraffic = totalMessages > 0

  return (
    <div className="table-card channel-insights-card">
      <div className="table-card-header">
        <div className="table-card-title">
          <Radio size={18} color="var(--primary)" />
          <span>Channel Performance</span>
        </div>
        <button className="delivery-rate-view-report" onClick={() => navigate('/reporting')}>
          View Report <ArrowUpRight size={13} />
        </button>
      </div>

      {!hasTraffic ? (
        <div className="empty-state table-empty-padding">
          <Info className="empty-state-icon" />
          <h3 className="empty-state-title">No Channel Activity</h3>
          <p className="empty-state-desc">
            Send a campaign on any channel and its volume will break down here.
          </p>
        </div>
      ) : (
        <div className="channel-insights-body">
          <div className="channel-insights-donut-wrap">
            <div className="channel-insights-donut">
              <ResponsiveContainer width="100%" height={180}>
                <PieChart>
                  <Pie
                    data={rows}
                    dataKey="messages"
                    nameKey="label"
                    innerRadius={58}
                    outerRadius={80}
                    paddingAngle={2}
                    startAngle={90}
                    endAngle={-270}
                    isAnimationActive={false}
                  >
                    {rows.map((row) => (
                      <Cell key={row.key} fill={row.color} stroke="none" />
                    ))}
                  </Pie>
                </PieChart>
              </ResponsiveContainer>
              <div className="channel-insights-center">
                <div className="channel-insights-center-value">{totalMessages.toLocaleString()}</div>
                <div className="channel-insights-center-label">Messages</div>
              </div>
            </div>

            <div className="channel-insights-legend">
              {rows.map((row) => (
                <div className="channel-insights-legend-item" key={row.key}>
                  <span className="channel-insights-legend-dot" style={{ backgroundColor: row.color }} />
                  <span className="channel-insights-legend-name">{row.label}</span>
                  <span className="channel-insights-legend-value">{row.messages.toLocaleString()}</span>
                  <span className="channel-insights-legend-percent">({row.sharePercent}%)</span>
                </div>
              ))}
            </div>
          </div>

          <div className="channel-insights-table-wrap">
            <table className="channel-insights-table">
              <thead>
                <tr>
                  <th>Channel</th>
                  <th className="num">Sent</th>
                  <th className="num">Delivered</th>
                  <th className="num">Failed</th>
                  <th>Delivery Rate</th>
                  <th className="num">Replies</th>
                  <th>Reply Rate</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.key}>
                    <td>
                      <span className="channel-insights-channel">
                        <span
                          className="channel-insights-legend-dot"
                          style={{ backgroundColor: row.color }}
                        />
                        {row.label}
                      </span>
                    </td>
                    <td className="num">{row.messages.toLocaleString()}</td>
                    <td className="num">{row.delivered.toLocaleString()}</td>
                    <td className="num">{row.failed.toLocaleString()}</td>
                    <td>
                      <div className="channel-insights-rate">
                        <div className="channel-insights-rate-track">
                          <div
                            className="channel-insights-rate-fill"
                            style={{ width: `${row.deliveryRate}%`, backgroundColor: row.color }}
                          />
                        </div>
                        <span className="channel-insights-rate-value">{row.deliveryRate}%</span>
                      </div>
                    </td>
                    <td className="num">{row.replies.toLocaleString()}</td>
                    <td>
                      <div className="channel-insights-rate">
                        <div className="channel-insights-rate-track">
                          <div
                            className="channel-insights-rate-fill"
                            style={{ width: `${Math.min(row.replyRate, 100)}%`, backgroundColor: row.color }}
                          />
                        </div>
                        <span className="channel-insights-rate-value">{row.replyRate}%</span>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
})

export default ChannelInsightsCard
