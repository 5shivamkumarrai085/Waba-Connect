import React, { useEffect, useState, useRef } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useCampaignStore } from '../../store/campaignStore'
import { useConnectionStore } from '../../store/connectionStore'
import { campaignService } from '../../services/campaigns/campaignService'
import { contactService } from '../../services/contacts/contactService'
import { templateService } from '../../services/templates/templateService'
import { Stepper } from '../../components/Stepper/Stepper'
import { WhatsAppPreview } from '../../components/WhatsAppPreview/WhatsAppPreview'
import type { Contact, ContactStatus, ContactSource } from '../../types/contacts'
import type { Template } from '../../types/templates'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { 
  Play, 
  Clock,
  UploadCloud,
  FileText,
  Trash2,
  Loader2
} from 'lucide-react'
import toast from 'react-hot-toast'
import './CampaignWizard.css'
import { getErrorMessage } from '../../utils/errorHelper'

export const CampaignWizard: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const campaignId = id ? parseInt(id, 10) : null
  const isEditMode = campaignId !== null

  const { connections, fetchDashboard: fetchConnectionDashboard } = useConnectionStore()

  const {
    wizardForm,
    activeStep,
    isLoading,
    
    setWizardForm,
    setActiveStep,
    resetWizard,
    createCampaign,
    updateCampaign
  } = useCampaignStore()

  // Form selections options
  const [templatesList, setTemplatesList] = useState<Template[]>([])
  const [contactsList, setContactsList] = useState<Contact[]>([])
  const [statuses, setStatuses] = useState<ContactStatus[]>([])
  const [sources, setSources] = useState<ContactSource[]>([])

  // Local search in Step 2 Contact checklist
  const [contactSearch, setContactSearch] = useState('')

  // Variable inputs in Step 3
  const [var1, setVar1] = useState('')
  const [var2, setVar2] = useState('')

  // File Upload states in Step 3
  const [uploading, setUploading] = useState(false)
  const [fileUrl, setFileUrl] = useState('')
  const [fileName, setFileName] = useState('')
  const [dragActive, setDragActive] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  // Double click prevent
  const [cooldownActive, setCooldownActive] = useState(false)

  useEffect(() => {
    if (activeStep === 3) {
      setCooldownActive(true)
      const timer = setTimeout(() => {
        setCooldownActive(false)
      }, 800)
      return () => clearTimeout(timer)
    }
  }, [activeStep])

  useEffect(() => {
    let isMounted = true

    const fetchWizardOptions = async () => {
      // Clear previous state immediately to prevent stale data
      resetWizard()
      setVar1('')
      setVar2('')
      setFileUrl('')
      setFileName('')
      setActiveStep(0)

      try {
        fetchConnectionDashboard()
        const [tpls, cts, stats, srcs] = await Promise.all([
          templateService.getTemplates(),
          contactService.getContacts(),
          contactService.getContactStatuses(),
          contactService.getContactSources()
        ])

        if (!isMounted) return

        setTemplatesList(tpls)
        setContactsList(cts.filter((c: any) => c.active !== false))
        setStatuses(stats)
        setSources(srcs)

        if (isEditMode && campaignId) {
          const details = await campaignService.getCampaignDetails(campaignId)
          if (!isMounted) return

          if (details.campaign.isDeleted) {
            toast.error('Deleted campaigns cannot be edited or rescheduled.')
            navigate('/campaigns/campaign')
            return
          }

          const template = tpls.find(t => t.name === details.campaign.templateName)

          const vars = (details as any).variables || []
          const v1 = vars.find((v: any) => v.variableName === '1')?.variableValue || ''
          const v2 = vars.find((v: any) => v.variableName === '2')?.variableValue || ''
          const fUrl = vars.find((v: any) => v.variableName === 'file')?.variableValue || ''
          const fName = fUrl ? fUrl.substring(fUrl.lastIndexOf('/') + 1).split('_').slice(1).join('_') : ''

          setVar1(v1)
          setVar2(v2)
          setFileUrl(fUrl)
          setFileName(fName)

          setWizardForm({
            name: details.campaign.name,
            relationType: details.campaign.relationType,
            templateName: details.campaign.templateName,
            templateId: template?.id || 0,
            selectedContactIds: details.recipients.map(recipient => recipient.contactId),
            selectAllContacts: false,
            sendImmediately: !details.campaign.scheduledAt,
            scheduledTime: details.campaign.scheduledAt ? toDateTimeLocalValue(details.campaign.scheduledAt) : '',
            variables: vars
          })
          setActiveStep(0)
        } else {
          // State already cleared at the start of fetchWizardOptions
        }
      } catch (err) {
      }
    }

    fetchWizardOptions()

    return () => {
      isMounted = false
    }
  }, [isEditMode, campaignId])

  // Get active template body for live preview
  const selectedTemplate = templatesList.find(t => t.name === wizardForm.templateName)
  
  // Format body text substituting variables dynamically if selected
  const getPreviewBody = () => {
    if (!selectedTemplate) return ''
    let body = selectedTemplate.bodyText || ''
    if (selectedTemplate.name === 'test_valid_var_template') {
      body = body.replace('{{1}}', var1 || '{{1}}').replace('{{2}}', var2 || '{{2}}')
    }
    return body
  }

  // Multi-step configurations
  const steps = ['Basic Info', 'Contact Selection', 'Variables Files', 'Scheduling']

  // Step 2 Filtered contacts logic
  const filteredContacts = contactsList.filter((c) => {
    if (wizardForm.relationType && wizardForm.relationType !== 'All') {
      const selectedRel = wizardForm.relationType.toLowerCase().trim()
      const contactType = (c.type || (c as any).relationType || '').toLowerCase().trim()
      if (contactType !== selectedRel) return false
    }
    if (wizardForm.contactsFilterStatus !== 'All') {
      if (c.status !== wizardForm.contactsFilterStatus) return false
    }
    if (wizardForm.contactsFilterSource !== 'All') {
      if (c.source !== wizardForm.contactsFilterSource) return false
    }
    if (contactSearch) {
      const q = contactSearch.toLowerCase()
      const matchesSearch = 
        (c.name || '').toLowerCase().includes(q) ||
        (c.firstName || '').toLowerCase().includes(q) ||
        (c.lastName || '').toLowerCase().includes(q) ||
        c.phone.includes(q)
      if (!matchesSearch) return false
    }
    return true
  })

  // Sync local variable states to store's wizardForm.variables
  useEffect(() => {
    const newVars = []
    if (wizardForm.templateName === 'test_valid_var_template') {
      newVars.push({ variableName: '1', variableValue: var1 })
      newVars.push({ variableName: '2', variableValue: var2 })
    }
    if (fileUrl) {
      newVars.push({ variableName: 'file', variableValue: fileUrl })
    }
    setWizardForm({ variables: newVars })
  }, [var1, var2, fileUrl, wizardForm.templateName])

  const handleFileUpload = async (file: File) => {
    setUploading(true)
    try {
      const res = await campaignService.uploadFile(file)
      setFileUrl(res.url)
      setFileName(res.fileName)
      toast.success('File uploaded successfully!')
    } catch (err) {
      toast.error('File upload failed.')
    } finally {
      setUploading(false)
    }
  }

  // Handlers
  const handleNext = async () => {
    if (activeStep === 0) {
      if (!wizardForm.name || !wizardForm.relationType || !wizardForm.templateName) {
        toast.error('Please complete all required fields (*).')
        return
      }

      const exists = await campaignService.checkNameExists(wizardForm.name, campaignId || undefined)
      if (exists) {
        toast.error('same name campaign already executed')
        return
      }
    }
    if (activeStep === 2) {
      setCooldownActive(true)
    }
    setActiveStep(activeStep + 1)
  }

  const handlePrevious = () => {
    setActiveStep(activeStep - 1)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (activeStep < 3) {
      await handleNext()
      return
    }

    if (cooldownActive) {
      console.warn('Prevented auto-submit cooldown click')
      return
    }

    if (!wizardForm.name || !wizardForm.relationType || !wizardForm.templateId) {
      toast.error('Please complete campaign name, relation type, and template.')
      return
    }

    if (finalRecipientsCount === 0) {
      toast.error('Please select at least one contact.')
      return
    }

    if (!wizardForm.sendImmediately) {
      if (!wizardForm.scheduledTime) {
        toast.error('Please choose a schedule date and time.')
        return
      }

      if (new Date(wizardForm.scheduledTime) <= new Date()) {
        toast.error('Scheduled time must be in the future.')
        return
      }
    }

    try {
      if (isEditMode && campaignId) {
        await updateCampaign(campaignId)
        toast.success('Campaign updated successfully!')
      } else {
        await createCampaign()
        toast.success('Campaign created successfully!')
      }
      resetWizard()
      navigate('/campaigns/campaign')
    } catch (err: any) {
      toast.error(getErrorMessage(err, 'Error creating/saving campaign.'))
    }
  }

  const toggleContactSelection = (id: number) => {
    const selected = wizardForm.selectedContactIds
    if (selected.includes(id)) {
      setWizardForm({ selectedContactIds: selected.filter(x => x !== id) })
    } else {
      setWizardForm({ selectedContactIds: [...selected, id] })
    }
  }

  const toggleSelectAllListed = (checked: boolean) => {
    if (checked) {
      setWizardForm({ selectedContactIds: filteredContacts.map(c => c.id) })
    } else {
      setWizardForm({ selectedContactIds: [] })
    }
  }

  // Count final recipients count based on selection states
  const finalRecipientsCount = wizardForm.selectAllContacts ? filteredContacts.length : wizardForm.selectedContactIds.length

  return (
    <div className="fade-in">
      {/* Page Title with horizontal badges row */}
      <div className="wizard-title-row">
        <h2 className="wizard-title">{isEditMode ? 'Edit Campaign' : 'Create Campaign'}</h2>
        <div className="wizard-badges-row">
          <div className="wizard-badge purple">
            <span>Recipients: {finalRecipientsCount}</span>
          </div>
          <div className="wizard-badge purple">
            <span>Template: {wizardForm.templateName || 'not selected'}</span>
          </div>
          <div className="wizard-badge yellow">
            <span>Status: draft</span>
          </div>
          <div className="wizard-badge green">
            <span>Send Time: {wizardForm.sendImmediately ? 'immediately' : 'scheduled'}</span>
          </div>
        </div>
      </div>

      {/* 2 Column layout grid */}
      <div className="wizard-columns-container">
        {/* Left Form card */}
        <div className="wizard-form-card">
          <Stepper
            steps={steps}
            activeStep={activeStep}
          />

          <form id="campaign-wizard-form" onSubmit={handleSubmit}>
            <div className="wizard-body">
              
              {/* Step 1: Basic Info */}
              {activeStep === 0 && (
                <div className="fade-in">
                  <div className="form-group margin-top-20">
                    <h3 className="upload-main-text">Basic Information</h3>
                    <p className="upload-sub-text">Enter campaign details and select template</p>
                  </div>

                  <div className="form-group form-group-required margin-top-20">
                    <label className="form-label">Campaign Name</label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder="Enter campaign name"
                      value={wizardForm.name}
                      onChange={(e) => setWizardForm({ name: e.target.value })}
                      required
                    />
                  </div>

                  <div className="form-group margin-top-20">
                    <label className="form-label">Sender Connection(s) <span style={{ color: '#ef4444' }}>*</span></label>
                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px', marginTop: '6px' }}>
                      {connections.filter(c => c.isConnected && c.phoneNumber).map((conn) => {
                        const connIds: number[] = (wizardForm as any).connectionIds || []
                        const isSelected = connIds.includes(conn.id)
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
                            onClick={() => {
                              const prev: number[] = (wizardForm as any).connectionIds || []
                              const next = isSelected ? prev.filter((id: number) => id !== conn.id) : [...prev, conn.id]
                              setWizardForm({ connectionIds: next } as any)
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

                  <div className="contacts-filter-row">
                    <div className="form-group form-group-required">
                      <label className="form-label">Relation Type</label>
                      <select
                        className="form-control"
                        value={wizardForm.relationType}
                        onChange={(e) => setWizardForm({ relationType: e.target.value })}
                        required
                      >
                        <option value="">Select Relation Type</option>
                        <option value="Lead">Lead</option>
                        <option value="Customer">Customer</option>
                        <option value="Vendor">Vendor</option>
                      </select>
                    </div>

                    <div className="form-group form-group-required">
                      <label className="form-label">Template</label>
                      <select
                        className="form-control"
                        value={wizardForm.templateName}
                        onChange={(e) => {
                          const t = templatesList.find(x => x.name === e.target.value)
                          setWizardForm({ templateName: e.target.value, templateId: t?.id || 0 })
                        }}
                        required
                      >
                        <option value="">Select Template</option>
                        {templatesList
                          .filter(t => t.status?.toLowerCase() === 'approved')
                          .map(t => (
                          <option key={t.id} value={t.name}>{t.name}</option>
                        ))}
                      </select>
                    </div>
                  </div>
                </div>
              )}

              {/* Step 2: Contact Selection */}
              {activeStep === 1 && (
                <div className="fade-in">
                  <div className="form-group">
                    <h3 className="upload-main-text">Contact Selection</h3>
                    <p className="upload-sub-text">Choose your target audience</p>
                  </div>

                  {/* Select all contacts panel card */}
                  <div className="contacts-selection-card margin-top-20">
                    <div className="contacts-controls-row">
                      <div className="contacts-controls-left">
                        <input
                          type="checkbox"
                          id="select-all-contacts"
                          checked={wizardForm.selectAllContacts}
                          onChange={(e) => setWizardForm({ selectAllContacts: e.target.checked })}
                        />
                        <div className="wizard-label-spacer">
                          <label htmlFor="select-all-contacts" className="upload-main-text">Select all contacts</label>
                          <p className="upload-sub-text margin-zero">Automatically include all matching contacts</p>
                        </div>
                      </div>
                      <div className="contacts-controls-right">
                        <span className="contacts-count-val">{filteredContacts.length}</span>
                        <span className="upload-sub-text">Contacts</span>
                      </div>
                    </div>
                  </div>

                  {/* Display list filters only if select all is unchecked */}
                  {!wizardForm.selectAllContacts && (
                    <div className="fade-in">
                      <div className="contacts-filter-row">
                        <div className="form-group">
                          <label className="form-label">Filter by status</label>
                          <select
                            className="form-control"
                            value={wizardForm.contactsFilterStatus}
                            onChange={(e) => setWizardForm({ contactsFilterStatus: e.target.value })}
                          >
                            <option value="All">All Statuses</option>
                            {statuses.map(s => (
                              <option key={s.id} value={s.name}>{s.name}</option>
                            ))}
                          </select>
                        </div>

                        <div className="form-group">
                          <label className="form-label">Filter By Source</label>
                          <select
                            className="form-control"
                            value={wizardForm.contactsFilterSource}
                            onChange={(e) => setWizardForm({ contactsFilterSource: e.target.value })}
                          >
                            <option value="All">All Sources</option>
                            {sources.map(s => (
                              <option key={s.id} value={s.name}>{s.name}</option>
                            ))}
                          </select>
                        </div>
                      </div>

                      {/* Contacts list table check */}
                      <div className="contacts-selection-card">
                        <div className="contacts-controls-row">
                          <span className="contacts-count-label">{wizardForm.selectedContactIds.length} Selected</span>
                          <SearchBar
                            value={contactSearch}
                            onChange={setContactSearch}
                            placeholder="Search Contacts"
                          />
                        </div>

                        <div className="data-table-wrapper margin-top-20">
                          {filteredContacts.length === 0 ? (
                            <div className="data-table-empty">
                              <p>No Contacts Found</p>
                            </div>
                          ) : (
                            <table className="data-table">
                              <thead>
                                <tr>
                                  <th className="checkbox-cell">
                                    <input
                                      type="checkbox"
                                      onChange={(e) => toggleSelectAllListed(e.target.checked)}
                                      checked={filteredContacts.length > 0 && filteredContacts.every(c => wizardForm.selectedContactIds.includes(c.id))}
                                    />
                                  </th>
                                  <th>Name</th>
                                  <th>Phone</th>
                                </tr>
                              </thead>
                              <tbody>
                                {filteredContacts.map(c => (
                                  <tr key={c.id}>
                                    <td className="checkbox-cell">
                                      <input
                                        type="checkbox"
                                        checked={wizardForm.selectedContactIds.includes(c.id)}
                                        onChange={() => toggleContactSelection(c.id)}
                                      />
                                    </td>
                                    <td>{c.name || `${c.firstName || ''} ${c.lastName || ''}`.trim()}</td>
                                    <td>{c.phone}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          )}
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              )}

              {/* Step 3: Variables & Files */}
              {activeStep === 2 && (
                <div className="fade-in">
                  <div className="form-group">
                    <h3 className="upload-main-text">Variables and Files</h3>
                    <p className="upload-sub-text">Customize your message with variables and media template attachments.</p>
                  </div>

                  {/* Upload Area Component */}
                  <div className="contacts-selection-card margin-top-20">
                    <div className="upload-area-header">
                      <span className="upload-area-label">Template Media / Document Attachment</span>
                    </div>

                    <div
                      className={`upload-dropzone ${dragActive ? 'drag-active' : ''}`}
                      onDragEnter={(e) => { e.preventDefault(); setDragActive(true); }}
                      onDragOver={(e) => { e.preventDefault(); setDragActive(true); }}
                      onDragLeave={() => setDragActive(false)}
                      onDrop={async (e) => {
                        e.preventDefault();
                        setDragActive(false);
                        if (e.dataTransfer.files && e.dataTransfer.files[0]) {
                          await handleFileUpload(e.dataTransfer.files[0]);
                        }
                      }}
                      onClick={() => fileInputRef.current?.click()}
                    >
                      <input
                        ref={fileInputRef}
                        type="file"
                        className="hidden-input"
                        style={{ display: 'none' }}
                        onChange={async (e) => {
                          if (e.target.files && e.target.files[0]) {
                            await handleFileUpload(e.target.files[0]);
                          }
                        }}
                      />

                      <div className="upload-icon-wrapper">
                        {uploading ? (
                          <Loader2 className="animate-spin" size={44} strokeWidth={1} />
                        ) : (
                          <UploadCloud size={44} strokeWidth={1} />
                        )}
                      </div>

                      <p className="upload-main-text">
                        {uploading ? 'Uploading file...' : 'Drag your file here or click in this area.'}
                      </p>
                      <p className="upload-sub-text">PDF, DOCX, PNG, JPG up to 10MB</p>
                    </div>

                    {fileUrl && (
                      <div className="upload-file-details">
                        <FileText className="upload-file-icon" size={24} />
                        <div className="upload-file-info">
                          <span className="upload-file-name">{fileName}</span>
                          <span className="upload-file-size">Stored in WABA Campaigns Cloud</span>
                        </div>
                        <button
                          type="button"
                          className="upload-file-remove-btn"
                          onClick={(e) => {
                            e.stopPropagation();
                            setFileUrl('');
                            setFileName('');
                          }}
                          aria-label="Remove attachment"
                        >
                          <Trash2 size={16} />
                        </button>
                      </div>
                    )}
                  </div>

                  {/* Render dynamic inputs if variables template is selected */}
                  {wizardForm.templateName === 'test_valid_var_template' && (
                    <div className="contacts-selection-card margin-top-20">
                      <div className="form-group margin-top-20">
                        <label className="form-label">Variable 1 Value ({"{{1}}"})</label>
                        <input
                          type="text"
                          className="form-control"
                          placeholder="e.g. Tushar"
                          value={var1}
                          onChange={(e) => setVar1(e.target.value)}
                        />
                      </div>
                      <div className="form-group margin-top-20">
                        <label className="form-label">Variable 2 Value ({"{{2}}"})</label>
                        <input
                          type="text"
                          className="form-control"
                          placeholder="e.g. 10052"
                          value={var2}
                          onChange={(e) => setVar2(e.target.value)}
                        />
                      </div>
                    </div>
                  )}
                </div>
              )}

              {/* Step 4: Scheduling */}
              {activeStep === 3 && (
                <div className="fade-in">
                  <div className="form-group">
                    <h3 className="upload-main-text">Scheduling</h3>
                    <p className="upload-sub-text">Choose when to send your campaign</p>
                  </div>

                  {/* Radio card components */}
                  <div className="scheduling-cards-row margin-top-20">
                    {/* Card 1: Immediately */}
                    <div 
                      className={`scheduling-card ${wizardForm.sendImmediately ? 'active green' : ''}`}
                      onClick={() => setWizardForm({ sendImmediately: true, scheduledTime: '' })}
                    >
                      <input
                        type="radio"
                        className="scheduling-card-radio"
                        checked={wizardForm.sendImmediately}
                        readOnly
                      />
                      <div className="scheduling-card-content">
                        <span className="scheduling-card-title">Send Immediately</span>
                        <span className="scheduling-card-subtext">Campaign will start immediately after creation</span>
                        <div className="scheduling-card-badge green">
                          <Play size={12} />
                          <span>Instant Delivery</span>
                        </div>
                      </div>
                    </div>

                    {/* Card 2: Schedule for later */}
                    <div 
                      className={`scheduling-card ${!wizardForm.sendImmediately ? 'active blue' : ''}`}
                      onClick={() => setWizardForm({ sendImmediately: false, scheduledTime: wizardForm.scheduledTime || getDefaultScheduleTime() })}
                    >
                      <input
                        type="radio"
                        className="scheduling-card-radio"
                        checked={!wizardForm.sendImmediately}
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

                  {/* Render datepicker if scheduled is active */}
                  {!wizardForm.sendImmediately && (
                    <div className="form-group margin-top-20 fade-in">
                      <label className="form-label">Choose Date & Time</label>
                      <input
                        type="datetime-local"
                        className="form-control"
                        value={wizardForm.scheduledTime}
                        onChange={(e) => setWizardForm({ scheduledTime: e.target.value })}
                        required={!wizardForm.sendImmediately}
                      />
                    </div>
                  )}
                </div>
              )}

            </div>
          </form>
        </div>

        {/* Right Preview Card column */}
        <div className="wizard-right-column">
          <div className="preview-section">
            <h3 className="wizard-live-preview-title">Live Preview</h3>
            <WhatsAppPreview bodyText={getPreviewBody()} />
          </div>

          {/* Stepper buttons footer navigation matching original screenshot */}
          <div className="campaign-wizard-footer">
            <button
              type="button"
              className="btn-wizard-nav btn-wizard-prev"
              onClick={handlePrevious}
              disabled={activeStep === 0}
            >
              Previous
            </button>

            <span className="step-indicator-text">
              step <span className="step-number-active">{activeStep + 1}</span> of {steps.length}
            </span>

            <div className="campaign-wizard-right-actions">
              {activeStep === 3 ? (
                <button
                  type="submit"
                  form="campaign-wizard-form"
                  className="btn-wizard-nav btn-wizard-save"
                  disabled={isLoading || cooldownActive}
                >
                  {isEditMode ? 'Save Changes' : 'Create Campaign'}
                </button>
              ) : (
                <button
                  type="button"
                  className="btn-wizard-nav btn-wizard-next"
                  onClick={handleNext}
                >
                  Next
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

const toDateTimeLocalValue = (value: string) => {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''

  const pad = (part: number) => String(part).padStart(2, '0')
  return [
    date.getFullYear(),
    pad(date.getMonth() + 1),
    pad(date.getDate())
  ].join('-') + `T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const getDefaultScheduleTime = () => {
  const date = new Date(Date.now() + 15 * 60 * 1000)
  return toDateTimeLocalValue(date.toISOString())
}

export default CampaignWizard
