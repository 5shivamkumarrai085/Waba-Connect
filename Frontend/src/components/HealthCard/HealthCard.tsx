import React, { useState } from 'react'
import { Heart, RefreshCw, Shield, Briefcase, Layers, MessageSquare } from 'lucide-react'
import type { WabaHealthModel } from '../../types/waba'
import './HealthCard.css'

interface HealthCardProps {
  healthInfo: WabaHealthModel | null
  onRefresh: () => Promise<void>
}

export const HealthCard: React.FC<HealthCardProps> = ({ healthInfo, onRefresh }) => {
  const [refreshing, setRefreshing] = useState(false)

  if (!healthInfo) return null

  const handleRefresh = async () => {
    setRefreshing(true)
    await onRefresh()
    // Simulate short loader delay for UI feel
    setTimeout(() => {
      setRefreshing(false)
    }, 400)
  }

  const { lastChecked, wabaId, wabaStatus, businessId, businessStatus, appId, appStatus } = healthInfo

  return (
    <div className="health-card">
      <div className="health-card-header">
        <div className="health-card-icon-box">
          <Heart size={18} />
        </div>
        <div className="health-card-title-area">
          <h3 className="health-card-title">Overall Health</h3>
          <span className="health-card-subtitle">Last checked {lastChecked}</span>
        </div>
      </div>

      <div className="health-grid">
        {/* Box 1: WhatsApp Business ID */}
        <div className="health-item-box">
          <div className="health-item-top">
            <div className="health-item-icon-wrapper">
              <Shield size={16} />
            </div>
            <div className="health-item-info">
              <span className="health-item-name">WhatsApp Business ID</span>
              <span className="health-item-id">{wabaId}</span>
            </div>
          </div>
          <div className="health-item-bottom">
            <span className="health-item-status-label">Status</span>
            <span className={`health-status-badge ${(wabaStatus || '').toLowerCase()}`}>
              {wabaStatus}
            </span>
          </div>
        </div>

        {/* Box 2: WABA */}
        <div className="health-item-box">
          <div className="health-item-top">
            <div className="health-item-icon-wrapper">
              <MessageSquare size={16} />
            </div>
            <div className="health-item-info">
              <span className="health-item-name">WABA</span>
              <span className="health-item-id">ID: {wabaId}</span>
            </div>
          </div>
          <div className="health-item-bottom">
            <span className="health-item-status-label">Can Send Messages</span>
            <span className={`health-status-badge ${(wabaStatus || '').toLowerCase()}`}>
              {wabaStatus}
            </span>
          </div>
        </div>

        {/* Box 3: BUSINESS */}
        <div className="health-item-box">
          <div className="health-item-top">
            <div className="health-item-icon-wrapper">
              <Briefcase size={16} />
            </div>
            <div className="health-item-info">
              <span className="health-item-name">BUSINESS</span>
              <span className="health-item-id">ID: {businessId}</span>
            </div>
          </div>
          <div className="health-item-bottom">
            <span className="health-item-status-label">Can Send Messages</span>
            <span className={`health-status-badge ${(businessStatus || '').toLowerCase()}`}>
              {businessStatus}
            </span>
          </div>
        </div>

        {/* Box 4: APP */}
        <div className="health-item-box">
          <div className="health-item-top">
            <div className="health-item-icon-wrapper">
              <Layers size={16} />
            </div>
            <div className="health-item-info">
              <span className="health-item-name">APP</span>
              <span className="health-item-id">ID: {appId}</span>
            </div>
          </div>
          <div className="health-item-bottom">
            <span className="health-item-status-label">Can Send Messages</span>
            <span className={`health-status-badge ${(appStatus || '').toLowerCase()}`}>
              {appStatus}
            </span>
          </div>
        </div>
      </div>

      <div className="health-card-footer">
        <button 
          type="button" 
          className="health-refresh-btn"
          onClick={handleRefresh}
          disabled={refreshing}
        >
          <RefreshCw size={14} className={refreshing ? 'spin-anim' : ''} />
          <span>Refresh health status</span>
        </button>
      </div>
    </div>
  )
}
export default HealthCard
