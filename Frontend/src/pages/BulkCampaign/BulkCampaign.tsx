import React, { useState, useEffect, useRef } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import { UploadArea } from '../../components/UploadArea/UploadArea'
import { campaignUploadService } from '../../services/campaigns/campaignUploadService'
import type { CsvValidationData } from '../../services/campaigns/campaignUploadService'
import { campaignService } from '../../services/campaigns/campaignService'
import { templateService } from '../../services/templates/templateService'
import { WhatsAppPreview } from '../../components/WhatsAppPreview/WhatsAppPreview'
import { apiClient } from '../../services/apiClient'
import type { Template } from '../../types/templates'
import { 
  Play, 
  Clock, 
  Download, 
  Trash2, 
  Loader2, 
  CheckCircle,
  X,
  ChevronRight,
  ChevronLeft,
  UploadCloud
} from 'lucide-react'
import toast from 'react-hot-toast'
import { useConnectionStore } from '../../store/connectionStore'
import './BulkCampaign.css'

export const BulkCampaign: React.FC = () => {
  const navigate = useNavigate()

  // Wizard Step state: 1 (Details & Template), 2 (Variables & Send)
  const [step, setStep] = useState(1)

  // Form Basic Info states
  const [campaignName, setCampaignName] = useState('')
  const [isNameDuplicate, setIsNameDuplicate] = useState(false)
  const [relationType, setRelationType] = useState('Lead')

  // CSV File upload states
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [validationData, setValidationData] = useState<CsvValidationData | null>(null)
  const [isUploading, setIsUploading] = useState(false)
  const [isSampleModalOpen, setIsSampleModalOpen] = useState(false)

  // Template select states
  const [templatesList, setTemplatesList] = useState<Template[]>([])
  const [selectedTemplateId, setSelectedTemplateId] = useState<number | string>('')
  const [selectedTemplate, setSelectedTemplate] = useState<Template | null>(null)
  const [isLoadingTemplates, setIsLoadingTemplates] = useState(false)

  // Dynamic variables values state
  const [varValues, setVarValues] = useState<Record<number, string>>({})

  // Template header media upload states
  const [uploadingMedia, setUploadingMedia] = useState(false)
  const [mediaUrl, setMediaUrl] = useState('')
  const [mediaFileName, setMediaFileName] = useState('')
  const mediaFileInputRef = useRef<HTMLInputElement>(null)

  // Scheduling states
  const [sendImmediately, setSendImmediately] = useState(true)
  const [scheduledTime, setScheduledTime] = useState('')

  // Create campaign loading state
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [selectedConnectionIds, setSelectedConnectionIds] = useState<number[]>([])
  const { connections, fetchDashboard } = useConnectionStore()

  // Load connections on mount (templates load only when connection is selected)
  useEffect(() => {
    fetchDashboard()
  }, [])

  // Check name duplication on change
  useEffect(() => {
    if (!campaignName.trim()) {
      setIsNameDuplicate(false)
      return
    }
    const timer = setTimeout(async () => {
      try {
        const exists = await campaignService.checkNameExists(campaignName)
        setIsNameDuplicate(exists)
      } catch (err) {
      }
    }, 400)

    return () => clearTimeout(timer)
  }, [campaignName])

  // Handle template selection change
  const handleTemplateChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const tplId = Number(e.target.value)
    setSelectedTemplateId(tplId)
    const tpl = templatesList.find(t => t.id === tplId) || null
    setSelectedTemplate(tpl)
    setVarValues({})
    setMediaUrl('')
    setMediaFileName('')
  }

  // Handle variable inputs dynamically
  const handleVarValueChange = (pos: number, val: string) => {
    setVarValues(prev => ({
      ...prev,
      [pos]: val
    }))
  }

  // Handle local media upload for template header attachment
  const handleMediaUpload = async (file: File) => {
    setUploadingMedia(true)
    try {
      const res = await campaignService.uploadFile(file)
      setMediaUrl(res.url)
      setMediaFileName(res.fileName)
      toast.success('Media attachment uploaded successfully!')
    } catch (err) {
      toast.error('Media upload failed.')
    } finally {
      setUploadingMedia(false)
    }
  }

  // Handle CSV file validation & upload
  const handleCsvValidateAndUpload = async () => {
    if (!campaignName.trim()) {
      toast.error('Please enter a Campaign Name.')
      return
    }
    if (isNameDuplicate) {
      toast.error('Campaign name already exists.')
      return
    }
    if (!selectedFile) {
      toast.error('Please choose a CSV file.')
      return
    }

    setIsUploading(true)
    try {
      const res = await campaignUploadService.validateCsv(selectedFile)
      if (res.success && res.data) {
        setValidationData(res.data)
        toast.success(res.message)
      } else {
        setValidationData(null)
        toast.error(res.message || 'cannot upload wrong format csv file')
      }
    } catch (err) {
      setValidationData(null)
      toast.error('cannot upload wrong format csv file')
    } finally {
      setIsUploading(false)
    }
  }

  // Download sample CSV from backend
  const handleDownloadSample = () => {
    const base = apiClient.defaults.baseURL || 'http://localhost:5155/api'
    const cleanBase = base.replace(/\/api$/, '') // strip trailing api
    window.open(`${cleanBase}/api/Campaigns/csv-sample`, '_blank')
  }

  // Compile variable requests payload
  const compileVariables = () => {
    const list: any[] = []
    if (selectedTemplate && selectedTemplate.variables) {
      selectedTemplate.variables.forEach(v => {
        list.push({
          variableName: `${v.position}`,
          variableValue: varValues[v.position] || ''
        })
      })
    }
    if (mediaUrl) {
      list.push({
        variableName: 'file',
        variableValue: mediaUrl
      })
    }
    return list
  }

  // Form submit for Campaign creation
  const handleSendCampaign = async (e: React.FormEvent) => {
    e.preventDefault()

    if (!campaignName.trim() || isNameDuplicate) {
      toast.error('Please provide a unique Campaign Name.')
      return
    }
    if (!validationData) {
      toast.error('Please upload and validate a CSV file first.')
      return
    }
    if (!selectedTemplateId) {
      toast.error('Please select a Template.')
      return
    }

    // Verify all template variables are filled
    if (selectedTemplate && selectedTemplate.variables) {
      const missing = selectedTemplate.variables.some(v => !varValues[v.position]?.trim())
      if (missing) {
        toast.error('Please provide values for all variables.')
        return
      }
    }

    // Verify schedule time if later is selected
    if (!sendImmediately && !scheduledTime) {
      toast.error('Please select a date and time for scheduled campaigns.')
      return
    }

    setIsSubmitting(true)
    try {
      const basePayload = {
        name: campaignName,
        csvFileUrl: validationData.fileUrl,
        templateId: Number(selectedTemplateId),
        relationType,
        scheduleType: sendImmediately ? 'Immediate' : 'Scheduled',
        scheduledAt: sendImmediately ? null : new Date(scheduledTime).toISOString(),
        variables: compileVariables()
      }

      // Handle multi-connection: create one campaign per connection
      const connIds = selectedConnectionIds.length > 0 ? selectedConnectionIds : [undefined]
      let lastRes: any = null
      for (const connId of connIds) {
        const payload = {
          ...basePayload,
          connectionId: connId,
          name: connIds.length > 1 && connId ? `${campaignName} (${connId})` : campaignName
        }
        lastRes = await campaignUploadService.createCsvCampaign(payload)
      }

      const res = lastRes
      if (res.success) {
        toast.success(res.message)
        navigate('/campaigns/campaign')
      } else {
        toast.error(res.message)
      }
    } catch (err) {
      toast.error('Failed to create CSV campaign.')
    } finally {
      setIsSubmitting(false)
    }
  }

  // Generate live preview text replacing variables
  const getPreviewBody = () => {
    if (!selectedTemplate) return 'Please select a template to preview'
    let body = selectedTemplate.bodyText || ''
    if (selectedTemplate.variables) {
      selectedTemplate.variables.forEach(v => {
        const val = varValues[v.position] || `{{${v.position}}}`
        body = body.replace(`{{${v.position}}}`, val)
      })
    }
    return body
  }

  const getDefaultScheduleTime = () => {
    const date = new Date(Date.now() + 15 * 60 * 1000)
    const pad = (part: number) => String(part).padStart(2, '0')
    return [
      date.getFullYear(),
      pad(date.getMonth() + 1),
      pad(date.getDate())
    ].join('-') + `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  }

  const isMediaRequired = selectedTemplate && 
    selectedTemplate.headerType !== 'None' && 
    selectedTemplate.headerType !== 'Text'

  // Validation helper for Next button activation
  const isDetailsValid = campaignName.trim() !== '' && 
    !isNameDuplicate && 
    validationData !== null && 
    selectedTemplateId !== ''

  return (
    <motion.div className="bulk-campaign-container" {...pageTransitionProps}>
      <h2 className="bulk-campaign-title">Campaigns from CSV File</h2>

      {/* Modern Stepper Indicator */}
      <div className="bulk-stepper">
        <div className={`bulk-step ${step === 1 ? 'active' : ''}`} onClick={() => isDetailsValid && setStep(1)}>
          <span className="step-num">1</span>
          <span className="step-label">Campaign Details</span>
        </div>
        <div className="step-connector" />
        <div className={`bulk-step ${step === 2 ? 'active' : ''}`}>
          <span className="step-num">2</span>
          <span className="step-label">Variables & Preview</span>
        </div>
      </div>

      {step === 1 ? (
        /* STEP 1: CAMPAIGN DETAILS & CSV VALIDATION */
        <div className={`bulk-step-1-wrap ${selectedTemplate ? 'split-details-view' : 'centered-details-view'}`}>
          <div className="bulk-card details-card">
            <h3 className="bulk-card-title">Campaign</h3>

            {/* Campaign Name */}
            <div className="form-group form-group-required">
              <label className="form-label">Campaign Name</label>
              <input
                type="text"
                className={`form-control ${isNameDuplicate ? 'is-invalid' : ''}`}
                placeholder="Enter campaign name"
                value={campaignName}
                onChange={(e) => setCampaignName(e.target.value)}
                required
              />
              {isNameDuplicate && (
                <span className="error-text-warning">* same name campaign already executed</span>
              )}
            </div>

            {/* Sender Connection(s) */}
            <div className="form-group margin-top-20">
              <label className="form-label">Sender Connection(s) <span style={{ color: '#ef4444' }}>*</span></label>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', marginTop: '6px' }}>
                {connections.filter(c => c.isConnected && c.phoneNumber).map((conn) => {
                  const isSelected = selectedConnectionIds.includes(conn.id)
                  return (
                    <button
                      key={conn.id}
                      type="button"
                      style={{
                        display: 'inline-flex', alignItems: 'center', gap: '6px',
                        padding: '8px 14px', borderRadius: '24px',
                        border: isSelected ? '1.5px solid #6366f1' : '1.5px solid #e2e8f0',
                        background: isSelected ? 'linear-gradient(135deg, #eef2ff, #e0e7ff)' : '#f8fafc',
                        color: isSelected ? '#4338ca' : '#475569',
                        fontSize: '13px', fontWeight: 500, cursor: 'pointer',
                        transition: 'all 0.2s', fontFamily: 'inherit'
                      }}
                      onClick={async () => {
                        const next = isSelected ? [] : [conn.id]
                        setSelectedConnectionIds(next)
                        setSelectedTemplateId(0)
                        setTemplatesList([])
                        setIsLoadingTemplates(true)
                        try {
                          if (next.length > 0) {
                            const connTpls = await templateService.getTemplatesByConnection(conn.id)
                            setTemplatesList(connTpls)
                          }
                          // When deselected, templates stay empty — no DB fallback
                        } finally {
                          setIsLoadingTemplates(false)
                        }
                      }}
                    >
                      {isSelected && <span style={{ fontWeight: 700 }}>✓</span>}
                      <span>{conn.name}</span>
                      <span style={{ fontSize: '11px', opacity: 0.7 }}>{conn.phoneNumber}</span>
                    </button>
                  )
                })}
                {connections.filter(c => c.isConnected && c.phoneNumber).length === 0 && (
                  <span style={{ fontSize: '13px', color: '#ef4444' }}>No connected WABA numbers found</span>
                )}
              </div>
            </div>

            {/* Relation Type */}
            <div className="form-group form-group-required margin-top-20">
              <label className="form-label">Relation Type</label>
              <select
                className="form-control"
                value={relationType}
                onChange={(e) => setRelationType(e.target.value)}
                required
              >
                <option value="Lead">Lead</option>
                <option value="Customer">Customer</option>
              </select>
            </div>

            {/* CSV File Upload Area */}
            <div className="margin-top-20">
              <UploadArea
                selectedFile={selectedFile}
                onFileSelect={(file) => {
                  setSelectedFile(file)
                  setValidationData(null)
                }}
                onDownloadSampleClick={() => setIsSampleModalOpen(true)}
              />
            </div>

            {/* Validate CSV Action Button */}
            {selectedFile && !validationData && (
              <div className="bulk-validate-btn-wrapper">
                <button
                  type="button"
                  className="btn-bulk-upload"
                  onClick={handleCsvValidateAndUpload}
                  disabled={isUploading || isNameDuplicate || !campaignName}
                >
                  {isUploading ? (
                    <>
                      <Loader2 className="animate-spin" size={16} />
                      <span>Validating CSV...</span>
                    </>
                  ) : (
                    'Upload'
                  )}
                </button>
              </div>
            )}

            {/* Valid / Invalid rows note box */}
            {validationData && (
              <div className="bulk-validation-note-box fade-in">
                <CheckCircle size={16} className="note-box-icon" />
                <div className="note-box-content">
                  <span className="note-box-title">Note:</span>
                  <p className="note-box-text">
                    Out of the {validationData.totalRecords} records in your CSV file,{' '}
                    <strong className="text-green">{validationData.validCount}</strong> records are valid. The
                    campaign can be successfully sent to these {validationData.validCount} User.
                  </p>
                </div>
              </div>
            )}

            {/* Template selector dropdown */}
            {validationData && selectedConnectionIds.length > 0 ? (
              <div className="form-group form-group-required margin-top-20 fade-in">
                <label className="form-label">Template</label>
                <select
                  className="form-control"
                  value={selectedTemplateId}
                  onChange={handleTemplateChange}
                  disabled={isLoadingTemplates}
                  required
                >
                  <option value="">Nothing Selected</option>
                  {templatesList.map(t => (
                    <option key={t.id} value={t.id}>
                      {t.name} ({t.language})
                    </option>
                  ))}
                </select>
                {isLoadingTemplates && (
                  <div style={{ fontSize: '12px', color: '#6366f1', marginTop: '4px', fontWeight: 500 }}>
                    Loading approved templates...
                  </div>
                )}
              </div>
            ) : validationData && selectedConnectionIds.length === 0 ? (
              <div className="form-group margin-top-20 fade-in">
                <label className="form-label">Template</label>
                <p style={{ color: '#94a3b8', fontSize: '13px', marginTop: '4px', fontStyle: 'italic' }}>
                  Select a connection to load templates
                </p>
              </div>
            ) : null}

            {/* Details Footer Nav Button */}
            <div className="step-nav-buttons-row margin-top-20">
              <button
                type="button"
                className="btn-wizard-nav btn-wizard-cancel"
                onClick={() => {
                  setSelectedFile(null)
                  setValidationData(null)
                  setCampaignName('')
                  setSelectedTemplateId('')
                  setSelectedTemplate(null)
                }}
              >
                Cancel
              </button>

              <button
                type="button"
                className="btn-wizard-nav btn-wizard-next"
                disabled={!isDetailsValid}
                onClick={() => setStep(2)}
              >
                <span>Next</span>
                <ChevronRight size={16} />
              </button>
            </div>
          </div>

          {/* Conditional Template Preview pane in Step 1 */}
          {selectedTemplate && (
            <div className="bulk-card preview-card-step-1 fade-in">
              <h3 className="bulk-card-title">Preview</h3>
              <div className="preview-bubble-wrapper">
                <WhatsAppPreview bodyText={getPreviewBody()} />
              </div>
            </div>
          )}
        </div>
      ) : (
        /* STEP 2: VARIABLES, LIVE PREVIEW, SCHEDULING, SEND */
        <div className="bulk-wizard-layout step-2-layout">
          
          {/* Column 1: Variables Customization */}
          <div className="bulk-card">
            <h3 className="bulk-card-title">Variables</h3>

            {/* Upload Area for header attachment */}
            <div className="contacts-selection-card margin-bottom-20">
              <div className="upload-area-header">
                <span className="upload-area-label">Template Media / Document Attachment</span>
              </div>

              <div
                className="upload-dropzone"
                onClick={() => mediaFileInputRef.current?.click()}
              >
                <input
                  ref={mediaFileInputRef}
                  type="file"
                  className="hidden-input"
                  style={{ display: 'none' }}
                  onChange={async (e) => {
                    if (e.target.files && e.target.files[0]) {
                      await handleMediaUpload(e.target.files[0])
                    }
                  }}
                />

                <div className="upload-icon-wrapper">
                  {uploadingMedia ? (
                    <Loader2 className="animate-spin" size={32} />
                  ) : (
                    <UploadCloud size={44} strokeWidth={1} />
                  )}
                </div>

                <p className="upload-main-text">
                  {uploadingMedia ? 'Uploading media...' : 'Drag your file here or click in this area.'}
                </p>
                <p className="upload-sub-text">PDF, DOCX, PNG, JPG up to 10MB</p>
              </div>

              {mediaUrl && (
                <div className="upload-file-details margin-top-10">
                  <CheckCircle className="text-green" size={20} />
                  <div className="upload-file-info">
                    <span className="upload-file-name">{mediaFileName}</span>
                    <span className="upload-file-size text-green">Uploaded successfully</span>
                  </div>
                  <button
                    type="button"
                    className="upload-file-remove-btn"
                    onClick={(e) => {
                      e.stopPropagation()
                      setMediaUrl('')
                      setMediaFileName('')
                    }}
                  >
                    <Trash2 size={16} />
                  </button>
                </div>
              )}
            </div>

            {/* Template Body Variables List */}
            {selectedTemplate?.variables && selectedTemplate.variables.length > 0 ? (
              <div className="contacts-selection-card">
                {selectedTemplate.variables?.map(v => (
                  <div key={v.position} className="form-group margin-bottom-15">
                    <label className="form-label">
                      Variable {v.position} Value {v.sampleValue ? `(Sample: ${v.sampleValue})` : ''}
                    </label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder={`Enter value for {{${v.position}}}`}
                      value={varValues[v.position] || ''}
                      onChange={(e) => handleVarValueChange(v.position, e.target.value)}
                      required
                    />
                  </div>
                ))}
              </div>
            ) : (
              !isMediaRequired && (
                <div className="bulk-empty-state">
                  <CheckCircle size={28} className="text-green" />
                  <span>This template has no variables to customize.</span>
                </div>
              )
            )}

            {/* Back button to Details step */}
            <div className="margin-top-30">
              <button
                type="button"
                className="btn-wizard-nav btn-wizard-back"
                onClick={() => setStep(1)}
              >
                <ChevronLeft size={16} />
                <span>Back</span>
              </button>
            </div>
          </div>

          {/* Column 2: Live WhatsApp Message Preview & Campaign Submission */}
          <div className="step-2-right-column">
            
            {/* Live Preview Card */}
            <div className="bulk-card">
              <h3 className="bulk-card-title">Preview</h3>
              <div className="preview-bubble-wrapper">
                <WhatsAppPreview bodyText={getPreviewBody()} />
              </div>
            </div>

            {/* Scheduling & Save Campaign Card */}
            <div className="bulk-card margin-top-24">
              <h3 className="bulk-card-title">Send Campaign</h3>

              <form onSubmit={handleSendCampaign}>
                
                {/* Radio selection */}
                <div className="form-group">
                  <label className="form-label">Scheduling Options</label>
                  
                  <div className="scheduling-options-vertical margin-top-10">
                    <div 
                      className={`scheduling-card ${sendImmediately ? 'active green' : ''}`}
                      onClick={() => {
                        setSendImmediately(true)
                        setScheduledTime('')
                      }}
                    >
                      <input
                        type="radio"
                        className="scheduling-card-radio"
                        checked={sendImmediately}
                        readOnly
                      />
                      <div className="scheduling-card-content">
                        <span className="scheduling-card-title">Send immediately</span>
                        <span className="scheduling-card-subtext">Send messages right after saving</span>
                        <div className="scheduling-card-badge green">
                          <Play size={12} />
                          <span>Immediate</span>
                        </div>
                      </div>
                    </div>

                    <div 
                      className={`scheduling-card ${!sendImmediately ? 'active blue' : ''}`}
                      onClick={() => {
                        setSendImmediately(false)
                        setScheduledTime(getDefaultScheduleTime())
                      }}
                    >
                      <input
                        type="radio"
                        className="scheduling-card-radio"
                        checked={!sendImmediately}
                        readOnly
                      />
                      <div className="scheduling-card-content">
                        <span className="scheduling-card-title">Schedule for later</span>
                        <span className="scheduling-card-subtext">Choose specific date and time to send</span>
                        <div className="scheduling-card-badge blue">
                          <Clock size={12} />
                          <span>Perfect Timing</span>
                        </div>
                      </div>
                    </div>
                  </div>

                  {!sendImmediately && (
                    <div className="form-group margin-top-15 fade-in">
                      <label className="form-label">Choose Date & Time</label>
                      <input
                        type="datetime-local"
                        className="form-control"
                        value={scheduledTime}
                        onChange={(e) => setScheduledTime(e.target.value)}
                        required={!sendImmediately}
                      />
                    </div>
                  )}
                </div>

                {/* Final Recipient Counter & submit Button */}
                <div className="bulk-send-footer margin-top-20">
                  <span className="bulk-recipient-summary">
                    Sending To <strong>{validationData?.validCount || 0}</strong> Recipients
                  </span>
                  
                  <button
                    type="submit"
                    className="btn-wizard-nav btn-wizard-save"
                    disabled={isSubmitting || !validationData || !selectedTemplateId}
                  >
                    {isSubmitting ? (
                      <>
                        <Loader2 className="animate-spin" size={16} />
                        <span>Sending...</span>
                      </>
                    ) : (
                      'Send Campaign'
                    )}
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {/* Download Sample modal popup dialog */}
      {isSampleModalOpen && createPortal(
        <div className="modal-overlay-custom" onClick={() => setIsSampleModalOpen(false)}>
          <div className="modal-content-custom" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header-custom">
              <h4 className="modal-title-custom">Download Sample</h4>
              <button 
                type="button" 
                className="modal-close-btn-custom" 
                onClick={() => setIsSampleModalOpen(false)}
              >
                <X size={20} />
              </button>
            </div>

            <div className="modal-body-custom">
              
              {/* Guidelines */}
              <div className="modal-rule-box blue-alert">
                <p className="rule-text-row">
                  <strong>1. Phone Number Column Requirement:</strong> Your CSV file must include a column named Phoneno. Each record in this column should contain a valid contact number, correctly formatted with the country code, including the '+' sign.
                </p>
                <p className="rule-text-row margin-top-10">
                  <strong>2. CSV Format and Encoding:</strong> Your CSV data should follow the specified format. The first row of your CSV file must contain the column headers, as shown in the example table. Ensure that your file is encoded in UTF-8 to prevent any encoding issues.
                </p>
              </div>

              {/* Table Preview */}
              <div className="modal-table-section margin-top-20">
                <div className="table-header-row">
                  <span className="table-header-title">Campaign</span>
                  
                  <button 
                    type="button" 
                    className="btn-download-sample-modal"
                    onClick={handleDownloadSample}
                  >
                    <Download size={14} />
                    <span>Download Sample</span>
                  </button>
                </div>

                <div className="table-responsive-custom margin-top-10">
                  <table className="sample-csv-table">
                    <thead>
                      <tr>
                        <th><span className="required-star">*</span> FIRST NAME</th>
                        <th><span className="required-star">*</span> LAST NAME</th>
                        <th><span className="required-star">*</span> PHONE</th>
                        <th>EMAIL</th>
                        <th>COUNTRY</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr>
                        <td>Sample Data</td>
                        <td>Sample Data</td>
                        <td>+1 555 123 4567</td>
                        <td>abc@gmail.com</td>
                        <td>Sample Data</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>

            <div className="modal-footer-custom">
              <button 
                type="button" 
                className="btn-modal-cancel" 
                onClick={() => setIsSampleModalOpen(false)}
              >
                Cancel
              </button>
            </div>
          </div>
        </div>,
        document.body
      )}
    </motion.div>
  )
}

export default BulkCampaign
