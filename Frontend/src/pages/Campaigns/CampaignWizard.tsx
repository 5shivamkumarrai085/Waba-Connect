import React, { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useCampaignStore } from '../../store/campaignStore'
import { contactService } from '../../services/contacts/contactService'
import { templateService } from '../../services/templates/templateService'
import { Stepper } from '../../components/Stepper/Stepper'
import { WhatsAppPreview } from '../../components/WhatsAppPreview/WhatsAppPreview'
import type { Contact, ContactStatus, ContactSource } from '../../types/contacts'
import type { Template } from '../../types/templates'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { 
  CheckCircle, 
  Play, 
  Clock
} from 'lucide-react'
import toast from 'react-hot-toast'
import './CampaignWizard.css'

export const CampaignWizard: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const campaignId = id ? parseInt(id, 10) : null
  const isEditMode = campaignId !== null

  const {
    wizardForm,
    activeStep,
    isLoading,
    
    setWizardForm,
    setActiveStep,
    resetWizard,
    createCampaign,
    updateCampaign,
    campaigns
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

  useEffect(() => {
    // Load initial option values
    const fetchWizardOptions = async () => {
      try {
        const [tpls, cts, stats, srcs] = await Promise.all([
          templateService.getTemplates(),
          contactService.getContacts(),
          contactService.getContactStatuses(),
          contactService.getContactSources()
        ])
        setTemplatesList(tpls)
        setContactsList(cts)
        setStatuses(stats)
        setSources(srcs)
      } catch (err) {
        console.error('Error fetching wizard options:', err)
      }
    }

    fetchWizardOptions()

    if (isEditMode && campaignId) {
      // Find the editing campaign properties
      const existing = campaigns.find(c => c.id === campaignId)
      if (existing) {
        setWizardForm({
          name: existing.name,
          relationType: existing.relationType,
          templateName: existing.templateName,
          templateId: templatesList.find(t => t.name === existing.templateName)?.id || 0,
          selectedContactIds: Array.from({ length: existing.total }, (_, i) => i + 1),
          selectAllContacts: existing.total === 8,
          sendImmediately: existing.scheduledAt === 'Immediately' || !existing.scheduledAt,
          scheduledTime: existing.scheduledAt !== 'Immediately' ? existing.scheduledAt : ''
        })
      }
    } else {
      resetWizard()
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

  // Handlers
  const handleNext = () => {
    if (activeStep === 0) {
      if (!wizardForm.name || !wizardForm.relationType || !wizardForm.templateName) {
        toast.error('Please complete all required fields (*).')
        return
      }
    }
    setActiveStep(activeStep + 1)
  }

  const handlePrevious = () => {
    setActiveStep(activeStep - 1)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
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
      toast.error(err?.response?.data?.message || 'Error creating/saving campaign.')
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
  const finalRecipientsCount = wizardForm.selectAllContacts ? contactsList.length : wizardForm.selectedContactIds.length

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
                        <option value="Csv_campaign">Csv_campaign</option>
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
                        <span className="contacts-count-val">{contactsList.length}</span>
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
                    <p className="upload-sub-text">customize your message with variables and media. use @ for merge fields</p>
                  </div>

                  {/* Render dynamic inputs if variables template is selected */}
                  {wizardForm.templateName === 'test_valid_var_template' ? (
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
                  ) : (
                    <div className="page-loader margin-top-20">
                      <CheckCircle size={32} color="#10b981" />
                      <p className="upload-main-text margin-top-20">No customization needed</p>
                      <p className="upload-sub-text margin-zero">This template doesn't require variables or files</p>
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
                      onClick={() => setWizardForm({ sendImmediately: true })}
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
                      onClick={() => setWizardForm({ sendImmediately: false })}
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
                <>
                  <button
                    type="submit"
                    form="campaign-wizard-form"
                    className="btn-wizard-nav btn-wizard-save"
                    disabled={isLoading}
                  >
                    {isEditMode ? 'Save Changes' : 'Create Campaign'}
                  </button>
                  <button
                    type="button"
                    className="btn-wizard-nav btn-wizard-next"
                    disabled
                  >
                    Next
                  </button>
                </>
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
export default CampaignWizard
