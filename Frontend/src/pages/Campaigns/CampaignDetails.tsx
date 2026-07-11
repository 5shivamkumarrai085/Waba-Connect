import React, { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useCampaignStore } from '../../store/campaignStore'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { 
  Users, 
  CheckCircle, 
  Eye, 
  AlertTriangle, 
  FileSpreadsheet,
  ChevronLeft,
  ChevronRight
} from 'lucide-react'
import toast from 'react-hot-toast'
import './CampaignDetails.css'

export const CampaignDetails: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const campaignId = id ? parseInt(id, 10) : null

  const {
    selectedCampaign,
    selectedStats,
    selectedRecipients,
    currentDetailsTab,
    isLoading,
    
    loadCampaignDetails,
    toggleCampaignPause,
    setSelectedTab
  } = useCampaignStore()

  // Local table filters
  const [recipientSearch, setRecipientSearch] = useState('')
  const [pageSize, setPageSize] = useState(10)
  const [currentPage, setCurrentPage] = useState(1)

  useEffect(() => {
    if (campaignId) {
      loadCampaignDetails(campaignId)
    }
  }, [campaignId])

  if (isLoading && !selectedCampaign) {
    return (
      <div className="page-loader">
        <p className="page-loader-text">Loading campaign execution metrics...</p>
      </div>
    )
  }

  if (!selectedCampaign) {
    return (
      <div className="data-table-empty">
        <p>Campaign not found.</p>
        <button type="button" className="btn-cancel" onClick={() => navigate('/campaigns/campaign')}>
          Back to Campaigns
        </button>
      </div>
    )
  }

  // Filter recipients based on Active tab (Queue vs Executed)
  const activeRecipients = (selectedRecipients || []).filter((r) => {
    // 1. Tab check
    if (currentDetailsTab === 'queue') {
      if (!isQueuedRecipient(r.sentStatus)) return false
    } else {
      if (isQueuedRecipient(r.sentStatus)) return false
    }

    // 2. Search query check
    if (recipientSearch) {
      const q = recipientSearch.toLowerCase()
      const matches = 
        r.name.toLowerCase().includes(q) ||
        r.phone.includes(q) ||
        r.message.toLowerCase().includes(q)
      if (!matches) return false
    }

    return true
  })

  // Pagination parameters
  const totalResults = activeRecipients.length
  const startIndex = (currentPage - 1) * pageSize
  const endIndex = Math.min(totalResults, startIndex + pageSize)
  const paginatedRecipients = activeRecipients.slice(startIndex, endIndex)
  const totalPages = Math.max(1, Math.ceil(totalResults / pageSize))

  const handleExportSheet = () => {
    toast.success('Exporting execution log to CSV spreadsheet...')
  }

  const handlePauseToggle = async () => {
    if (!selectedCampaign) return
    try {
      await toggleCampaignPause(selectedCampaign.id)
      toast.success(selectedCampaign.status === 'Paused' ? 'Campaign resumed successfully.' : 'Campaign paused successfully.')
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unable to update campaign status.'
      toast.error(message)
    }
  }

  return (
    <div className="fade-in">
      {/* Top action buttons */}
      <div className="campaign-details-header">
        <button 
          type="button" 
          className="btn-toolbar btn-toolbar-refresh"
          onClick={() => navigate('/campaigns/campaign')}
        >
          Back to Campaigns
        </button>
        
        <button
          type="button"
          className={selectedCampaign.status === 'Paused' ? 'btn-resume-campaign' : 'btn-pause-campaign'}
          onClick={handlePauseToggle}
        >
          {selectedCampaign.status === 'Paused' ? 'Resume Campaign' : 'Pause Campaign'}
        </button>
      </div>

      {/* Top Metadata card */}
      <div className="campaign-details-metadata-card">
        <div className="metadata-item">
          <span className="metadata-label">Campaign Name</span>
          <span className="metadata-value">{selectedCampaign.name}</span>
        </div>
        <div className="metadata-item">
          <span className="metadata-label">Status</span>
          <div>
            <StatusBadge 
              type={getCampaignStatusBadgeType(selectedCampaign.status)} 
              text={formatCampaignStatus(selectedCampaign.status)} 
            />
          </div>
        </div>
        <div className="metadata-item">
          <span className="metadata-label">Template</span>
          <span className="metadata-value">{selectedCampaign.templateName}</span>
        </div>
        <div className="metadata-item">
          <span className="metadata-label">Scheduled At</span>
          <span className="metadata-value">{selectedCampaign.scheduledAt || 'N/A'}</span>
        </div>
      </div>

      {/* Statistics Cards Row */}
      {selectedStats && (
        <div className="campaign-stats-grid">
          {/* Total Leads Card */}
          <div className="campaign-stat-card">
            <div className="campaign-stat-header">
              <span className="campaign-stat-label">Total Lead in this Campaign</span>
              <div className="campaign-stat-icon-wrapper blue">
                <Users size={16} />
              </div>
            </div>
            <span className="campaign-stat-value">{selectedStats.totalLeads}</span>
            <span className="campaign-stat-subtext">{selectedStats.totalLeadsPercent}</span>
          </div>

          {/* Total Delivered Card */}
          <div className="campaign-stat-card">
            <div className="campaign-stat-header">
              <span className="campaign-stat-label">Total Delivered</span>
              <div className="campaign-stat-icon-wrapper green">
                <CheckCircle size={16} />
              </div>
            </div>
            <span className="campaign-stat-value">{selectedStats.deliveredPercent}</span>
            <span className="campaign-stat-subtext">{selectedStats.deliveredCount} Messages Delivered</span>
          </div>

          {/* Total Read Card */}
          <div className="campaign-stat-card">
            <div className="campaign-stat-header">
              <span className="campaign-stat-label">Total Read</span>
              <div className="campaign-stat-icon-wrapper blue">
                <Eye size={16} />
              </div>
            </div>
            <span className="campaign-stat-value">{selectedStats.readPercent}</span>
            <span className="campaign-stat-subtext">{selectedStats.readCount} Messages Read</span>
          </div>

          {/* Total Failed Card */}
          <div className="campaign-stat-card">
            <div className="campaign-stat-header">
              <span className="campaign-stat-label">Total Failed</span>
              <div className="campaign-stat-icon-wrapper red">
                <AlertTriangle size={16} />
              </div>
            </div>
            <span className="campaign-stat-value">{selectedStats.failedPercent}</span>
            <span className="campaign-stat-subtext">{selectedStats.failedCount} Messages Failed</span>
          </div>
        </div>
      )}

      {/* Bottom recipients list logs */}
      <div className="contacts-card">
        {/* Navigation tabs */}
        <div className="tabs-container">
          <button
            type="button"
            className={`tab-btn ${currentDetailsTab === 'queue' ? 'active' : ''}`}
            onClick={() => {
              setSelectedTab('queue')
              setCurrentPage(1)
            }}
          >
            Queue
          </button>
          <button
            type="button"
            className={`tab-btn ${currentDetailsTab === 'executed' ? 'active' : ''}`}
            onClick={() => {
              setSelectedTab('executed')
              setCurrentPage(1)
            }}
          >
            Executed
          </button>
        </div>

        {/* Action icons row */}
        <div className="contacts-controls-row">
          <div className="contacts-controls-left">
            <button 
              type="button" 
              className="btn-control-icon"
              onClick={handleExportSheet}
              title="Export Log"
              aria-label="Export log"
            >
              <FileSpreadsheet size={16} />
            </button>
          </div>

          <div className="contacts-controls-right">
            <SearchBar
              value={recipientSearch}
              onChange={setRecipientSearch}
              placeholder="Search..."
            />
          </div>
        </div>

        {/* Recipients table rows */}
        <div className="data-table-wrapper">
          {paginatedRecipients.length === 0 ? (
            <div className="data-table-empty">
              <p>No records found</p>
            </div>
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th>ID</th>
                  <th>Name</th>
                  <th>Phone</th>
                  <th>Message</th>
                  <th>Sent Status</th>
                </tr>
              </thead>
              <tbody>
                {paginatedRecipients.map((recipient) => (
                  <tr key={recipient.id}>
                    <td>{recipient.id}</td>
                    <td>{recipient.name}</td>
                    <td>{recipient.phone}</td>
                    <td className="body-data-cell">{recipient.message}</td>
                    <td>
                      <StatusBadge 
                        type={getRecipientStatusBadgeType(recipient.sentStatus)} 
                        text={recipient.sentStatus} 
                      />
                      {recipient.failedReason && (
                        <div className="campaign-recipient-error">{recipient.failedReason}</div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        {/* Footer info & size selector & page indicators */}
        <div className="contacts-table-footer">
          <div>
            <select
              className="contacts-pager-size-select"
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
            >
              <option value={5}>5</option>
              <option value={10}>10</option>
              <option value={20}>20</option>
            </select>
          </div>

          <div className="pager-navigation">
            <span className="contacts-pager-info">
              Showing {totalResults > 0 ? startIndex + 1 : 0} to {endIndex} of {totalResults} Results
            </span>

            {/* Pagination buttons */}
            <div className="contacts-controls-left">
              <button
                type="button"
                className="btn-control-icon"
                disabled={currentPage === 1}
                onClick={() => setCurrentPage(currentPage - 1)}
                aria-label="Previous Page"
              >
                <ChevronLeft size={16} />
              </button>
              <button
                type="button"
                className="btn-control-icon"
                disabled={currentPage === totalPages}
                onClick={() => setCurrentPage(currentPage + 1)}
                aria-label="Next Page"
              >
                <ChevronRight size={16} />
              </button>
            </div>
          </div>
        </div>

      </div>
    </div>
  )
}

const isQueuedRecipient = (status: string) => {
  return ['Pending'].includes(status)
}

const getRecipientStatusBadgeType = (status: string) => {
  if (['Sent', 'Delivered', 'Read'].includes(status)) return 'success'
  if (status === 'Failed') return 'error'
  return 'warning'
}

const getCampaignStatusBadgeType = (status: string) => {
  if (['Sent', 'Success'].includes(status)) return 'success'
  if (status === 'Paused') return 'warning'
  if (['Failed', 'Cancelled'].includes(status)) return 'error'
  return 'info'
}

const formatCampaignStatus = (status: string) => {
  if (status === 'Sent') return 'Success'
  if (status === 'Sending') return 'In Progress'
  return status
}

export default CampaignDetails
