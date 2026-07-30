import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useCampaignStore, usePreviewStore } from '../store/zustand'
import { Calendar, Info } from 'lucide-react'

import toast from 'react-hot-toast'

export const Campaign: React.FC = () => {
  const navigate = useNavigate()
  const { previewMode } = usePreviewStore()
  
  const {
    campaignName,
    relationType,
    template,
    scheduledTime,
    ignoreScheduledTime,
    setCampaignName,
    setRelationType,
    setTemplate,
    setScheduledTime,
    setIgnoreScheduledTime,
    resetForm
  } = useCampaignStore()

  const [errors, setErrors] = useState<{ campaignName?: string; relationType?: string }>({})

  // Dropdown option sets based on preview mode
  const relationTypes = previewMode 
    ? ['Transactional', 'Marketing', 'Support', 'Alerts'] 
    : []

  const templates = previewMode 
    ? ['welcome_auth_otp', 'delivery_shipment_status', 'payment_success_notification', 'marketing_promo_summer'] 
    : []

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    
    // Simple frontend validation structure
    const tempErrors: { campaignName?: string; relationType?: string } = {}
    if (!campaignName.trim()) {
      tempErrors.campaignName = 'Campaign name is required'
    }
    if (!relationType) {
      tempErrors.relationType = 'Relation type is required'
    }
    
    setErrors(tempErrors)
    
    if (Object.keys(tempErrors).length === 0) {
      toast.success(`Campaign Form Submitted! Name: ${campaignName}`)
      resetForm()
      navigate('/')
    }
  }

  const handleCancel = () => {
    resetForm()
    navigate('/')
  }

  return (
    <div className="fade-in">
      <div className="campaign-page-header">
        <h1>Create Campaign</h1>
      </div>

      <form onSubmit={handleSubmit} className="campaign-form-card">
        <div className="campaign-form-header">
          <h2>Campaign</h2>
        </div>

        <div className="campaign-form-body">
          {/* Campaign Name */}
          <div className="form-group">
            <label className="form-label required" htmlFor="campaignName">Campaign Name</label>
            <input 
              id="campaignName"
              type="text" 
              className="form-input" 
              placeholder="Enter campaign name"
              value={campaignName}
              onChange={(e) => setCampaignName(e.target.value)}
            />
            {errors.campaignName && (
              <span className="form-error">{errors.campaignName}</span>
            )}
          </div>

          {/* Relation Type */}
          <div className="form-group">
            <label className="form-label required" htmlFor="relationType">Relation Type</label>
            <select 
              id="relationType"
              className="form-select"
              value={relationType}
              onChange={(e) => setRelationType(e.target.value)}
            >
              <option value="">Nothing Selected</option>
              {relationTypes.map((type) => (
                <option key={type} value={type}>{type}</option>
              ))}
            </select>
            {errors.relationType && (
              <span className="form-error">{errors.relationType}</span>
            )}
          </div>

          {/* Template */}
          <div className="form-group">
            <label className="form-label" htmlFor="template">Template</label>
            <select 
              id="template"
              className="form-select"
              value={template}
              onChange={(e) => setTemplate(e.target.value)}
            >
              <option value="">Nothing Selected</option>
              {templates.map((temp) => (
                <option key={temp} value={temp}>{temp}</option>
              ))}
            </select>
            {!previewMode && (
              <span className="form-helper-text">
                <Info size={12} /> Dropdowns are empty because database APIs are disconnected.
              </span>
            )}
          </div>

          {/* Scheduled Send Time */}
          <div className="form-group">
            <label className="form-label" htmlFor="scheduledTime">Scheduled Send Time</label>
            <div className="datepicker-wrapper">
              <input 
                id="scheduledTime"
                type="datetime-local" 
                className="form-input" 
                disabled={ignoreScheduledTime}
                value={scheduledTime}
                onChange={(e) => setScheduledTime(e.target.value)}
              />
              <Calendar className="datepicker-icon" />
            </div>
          </div>

          {/* Toggle Ignore Scheduled Time */}
          <div className="form-group">
            <label className="form-label" htmlFor="ignoreToggle">Ignore scheduled time and send now</label>
            <div className="form-toggle-container">
              <label className="switch">
                <input 
                  id="ignoreToggle"
                  type="checkbox" 
                  checked={ignoreScheduledTime}
                  onChange={(e) => setIgnoreScheduledTime(e.target.checked)}
                />
                <span className="slider"></span>
              </label>
              <span className="form-toggle-label">
                {ignoreScheduledTime ? 'Send immediately on submit' : 'Send at scheduled date & time'}
              </span>
            </div>
          </div>
        </div>

        <div className="campaign-form-footer">
          <button type="button" className="btn btn-secondary" onClick={handleCancel}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary">
            Save Campaign
          </button>
        </div>
      </form>
    </div>
  )
}
