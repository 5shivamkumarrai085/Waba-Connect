import React from 'react'
import { Award, CheckCircle, Info } from 'lucide-react'

interface TableCardProps {
  type: 'read-rate' | 'delivery-rate'
  data?: any[]
}

export const TableCard: React.FC<TableCardProps> = React.memo(({ type, data: propData }) => {
  const isReadRate = type === 'read-rate'
  const title = isReadRate ? 'Top Campaigns — Highest Read Rate' : 'Top Campaigns — Highest Delivery Rate'
  const campaigns = propData && propData.length > 0 ? propData : []

  const footerText = isReadRate
    ? 'Most engaging campaign - Campaign performance ranking - Engagement benchmark'
    : 'System-generated emails have the highest delivery. - Marketing campaigns achieve higher read rates. - Transactional and marketing performance can be compared.'

  return (
    <div className="table-card">
      <div>
        <div className="table-card-header">
          <div className="table-card-title">
            {isReadRate ? (
              <Award size={18} color="var(--primary)" />
            ) : (
              <CheckCircle size={18} color="#10b981" />
            )}
            <span>{title}</span>
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
                {isReadRate ? (
                  <tr>
                    <th>#</th>
                    <th>Campaign</th>
                    <th>Messages</th>
                    <th>Read Rate</th>
                    <th>Delivered</th>
                  </tr>
                ) : (
                  <tr>
                    <th>#</th>
                    <th>Campaign</th>
                    <th>Messages</th>
                    <th>Delivery Rate</th>
                    <th>Read Rate</th>
                  </tr>
                )}
              </thead>
              <tbody>
                {campaigns.map((row) => (
                  <tr key={row.id}>
                    <td>{row.id}</td>
                    <td className="campaign-name-cell">{row.campaign}</td>
                    <td>{row.messages}</td>
                    
                    {isReadRate ? (
                      /* Highest Read Rate layout */
                      <>
                        <td className="rate-cell-value purple-text">{row.primaryRate}</td>
                        <td>
                          <div className="table-progress-wrapper">
                            <progress 
                              className={`app-progress ${row.secondaryPercent > 0 ? 'green' : 'gray'}`}
                              value={row.secondaryPercent}
                              max={100}
                            />
                            <span className="table-progress-text">{row.secondaryRate}</span>
                          </div>
                        </td>
                      </>
                    ) : (
                      /* Highest Delivery Rate layout */
                      <>
                        <td>
                          <div className="table-progress-wrapper">
                            <progress 
                              className="app-progress green"
                              value={row.primaryPercent}
                              max={100}
                            />
                            <span className="table-progress-text bold">{row.primaryRate}</span>
                          </div>
                        </td>
                        <td className="rate-cell-value rate-cell-value-secondary">{row.secondaryRate}</td>
                      </>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>

      <div className="table-card-footer">
        <Info className="table-card-footer-icon" size={14} />
        <span>{footerText}</span>
      </div>
    </div>
  )
})

export default TableCard
