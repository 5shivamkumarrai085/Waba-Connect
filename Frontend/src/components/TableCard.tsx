import React from 'react'
import { usePreviewStore } from '../store/zustand'
import { Award, CheckCircle, Info } from 'lucide-react'

interface TableCardProps {
  type: 'read-rate' | 'delivery-rate'
  data?: any[]
}

interface CampaignRow {
  id: number
  campaign: string
  messages: number
  primaryRate: string
  secondaryRate: string
  primaryPercent: number
  secondaryPercent: number
}

const readRateMockData: CampaignRow[] = [
  { id: 1, campaign: 'camp_20260615_01', messages: 1, primaryRate: '100.00%', secondaryRate: '0.00%', primaryPercent: 100, secondaryPercent: 0 },
  { id: 2, campaign: 'camp_20260617.001', messages: 1, primaryRate: '100.00%', secondaryRate: '0.00%', primaryPercent: 100, secondaryPercent: 0 },
  { id: 3, campaign: 'POC_CAMP_15', messages: 1, primaryRate: '100.00%', secondaryRate: '0.00%', primaryPercent: 100, secondaryPercent: 0 },
  { id: 4, campaign: 'Camp_20260615_001', messages: 2, primaryRate: '50.00%', secondaryRate: '50.00%', primaryPercent: 50, secondaryPercent: 50 },
  { id: 5, campaign: 'Test 12334', messages: 2, primaryRate: '50.00%', secondaryRate: '0.00%', primaryPercent: 50, secondaryPercent: 0 },
]

const deliveryRateMockData: CampaignRow[] = [
  { id: 1, campaign: 'camp_20260618_01', messages: 1, primaryRate: '100.00%', secondaryRate: '0.00%', primaryPercent: 100, secondaryPercent: 0 },
  { id: 2, campaign: 'POC_bulk_5', messages: 1, primaryRate: '100.00%', secondaryRate: '0.00%', primaryPercent: 100, secondaryPercent: 0 },
  { id: 3, campaign: 'Camp_20260615_001', messages: 2, primaryRate: '50.00%', secondaryRate: '50.00%', primaryPercent: 50, secondaryPercent: 50 },
  { id: 4, campaign: 'Test Campaign', messages: 4, primaryRate: '50.00%', secondaryRate: '0.00%', primaryPercent: 50, secondaryPercent: 0 },
  { id: 5, campaign: 'Deepak', messages: 3, primaryRate: '33.33%', secondaryRate: '33.33%', primaryPercent: 33.33, secondaryPercent: 33.33 },
]

export const TableCard: React.FC<TableCardProps> = ({ type, data: propData }) => {
  const { previewMode } = usePreviewStore()
  
  const isReadRate = type === 'read-rate'
  const title = isReadRate ? 'Top Campaigns — Highest Read Rate' : 'Top Campaigns — Highest Delivery Rate'
  const campaigns = propData && propData.length > 0 
    ? propData 
    : (previewMode ? (isReadRate ? readRateMockData : deliveryRateMockData) : [])

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
}
