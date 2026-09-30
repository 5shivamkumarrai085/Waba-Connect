import React, { useState, useEffect, useRef } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { Modal } from '../../components/Modal/Modal'
import { useNavigate } from 'react-router-dom'
import { UploadArea } from '../../components/UploadArea/UploadArea'
import { campaignUploadService } from '../../services/campaigns/campaignUploadService'
import type { CsvValidationData, CsvRowError } from '../../services/campaigns/campaignUploadService'
import { campaignService } from '../../services/campaigns/campaignService'
import { templateService } from '../../services/templates/templateService'
import { WhatsAppPreview } from '../../components/WhatsAppPreview/WhatsAppPreview'
import { EmailPreview } from '../../components/EmailPreview/EmailPreview'
import { ChannelCard } from '../../components/ChannelCard/ChannelCard'
import { ChoicePills } from '../../components/ChoicePills/ChoicePills'
import { AVAILABLE_CHANNELS, PLANNED_CHANNELS, toApiChannel } from '../../types/channel'
import type { MessageChannel } from '../../types/channel'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { emailTemplateService } from '../../services/email/emailTemplateService'
import type {
  EmailConnection,
  EmailTemplate,
  EmailTemplatePreview
} from '../../types/email'
import type { Template } from '../../types/templates'
import { 
  Play, 
  Clock, 
  Download, 
  Trash2, 
  Loader2, 
  CheckCircle,
  ChevronRight,
  ChevronLeft,
  UploadCloud
} from 'lucide-react'
import toast from 'react-hot-toast'
import { useConnectionStore } from '../../store/connectionStore'
import './BulkCampaign.css'
import Can from '../../components/Can/Can'
import { CsvRowErrors } from '../../components/CsvRowErrors/CsvRowErrors'

export const BulkCampaign: React.FC = () => {
  const navigate = useNavigate()

  // Wizard Step state: 1 (Details & Template), 2 (Variables & Send)
  const [step, setStep] = useState(1)

  // ── Channel ───────────────────────────────────────────────────────────────────────────────
  // WhatsApp by default, so this screen opens exactly as it always has.
  const [channel, setChannel] = useState<MessageChannel>('whatsapp')
  const isEmailChannel = channel === 'email'

  const [emailConnections, setEmailConnections] = useState<EmailConnection[]>([])
  const [emailTemplates, setEmailTemplates] = useState<EmailTemplate[]>([])
  const [isLoadingEmailOptions, setIsLoadingEmailOptions] = useState(false)
  const [senderIdentityId, setSenderIdentityId] = useState<number | null>(null)
  const [emailTemplateId, setEmailTemplateId] = useState<number | null>(null)
  const [replyToOverride, setReplyToOverride] = useState('')
  const [emailPreview, setEmailPreview] = useState<EmailTemplatePreview | null>(null)
  const [isPreviewLoading, setIsPreviewLoading] = useState(false)
  const [emailVarValues, setEmailVarValues] = useState<Record<string, string>>({})

  // Form Basic Info states
  const [campaignName, setCampaignName] = useState('')
  const [isNameDuplicate, setIsNameDuplicate] = useState(false)
  const [relationType, setRelationType] = useState('Lead')

  // CSV File upload states
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [validationData, setValidationData] = useState<CsvValidationData | null>(null)
  const [csvErrors, setCsvErrors] = useState<CsvRowError[]>([])
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
  // Email connections load up front, because the channel card's "configured" badge is derived
  // from them (loading them only after Email was picked made the card always read "No verified
  // sender yet"). Templates still load the first time Email is picked.
  useEffect(() => {
    let isMounted = true
    emailConnectionService.getConnections()
      .then(conns => { if (isMounted) setEmailConnections(conns) })
      .catch(() => { /* the card simply shows as not configured */ })
    return () => { isMounted = false }
  }, [])

  useEffect(() => {
    if (!isEmailChannel) return
    if (emailTemplates.length > 0) return

    let isMounted = true
    setIsLoadingEmailOptions(true)

    // enabledOnly: offering a disabled template would produce a campaign the dispatcher
    // refuses, after the operator has already uploaded a file and finished the form.
    emailTemplateService.getTemplates(true)
      .then(tpls => { if (isMounted) setEmailTemplates(tpls) })
      .catch(() => { /* the template picker shows its empty state */ })
      .finally(() => {
        if (isMounted) setIsLoadingEmailOptions(false)
      })

    return () => { isMounted = false }
  }, [isEmailChannel])

  // Every usable sender across every active email connection, flattened for the picker.
  const emailSenders = React.useMemo(
    () =>
      emailConnections
        .filter(c => c.isActive)
        .flatMap(c => c.senders.filter(sender => sender.isActive).map(sender => ({ sender, connection: c }))),
    [emailConnections]
  )

  const selectedEmailSender = emailSenders.find(s => s.sender.id === senderIdentityId)
  const selectedEmailTemplate = emailTemplates.find(t => t.id === emailTemplateId)

  // The preview is rendered server-side, through the same renderer the send path uses, so what
  // the operator approves is what actually goes out. Debounced because the variable inputs fire
  // on every keystroke.
  useEffect(() => {
    if (!isEmailChannel || !emailTemplateId) {
      setEmailPreview(null)
      return
    }

    let isMounted = true
    setIsPreviewLoading(true)

    const timer = setTimeout(async () => {
      const preview = await emailTemplateService.previewTemplate(emailTemplateId, emailVarValues)
      if (!isMounted) return
      setEmailPreview(preview)
      setIsPreviewLoading(false)
    }, 350)

    return () => {
      isMounted = false
      clearTimeout(timer)
    }
  }, [isEmailChannel, emailTemplateId, emailVarValues])

  // Switching channel resets what belongs to the other one. Without this, a template chosen for
  // WhatsApp stays selected behind an email form and is silently submitted.
  const handleChannelChange = (next: MessageChannel) => {
    if (next === channel) return
    setChannel(next)
    setStep(1)
    setSelectedTemplateId('')
    setSelectedTemplate(null)
    setTemplatesList([])
    setSelectedConnectionIds([])
    setEmailTemplateId(null)
    setSenderIdentityId(null)
    setVarValues({})
    setEmailVarValues({})
    setMediaUrl('')
    setMediaFileName('')
    // The uploaded file was judged against the old channel's column rules, so its preview no
    // longer describes what would be sent. Re-validating is the honest thing to do.
    setValidationData(null)
    setCsvErrors([])
  }

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
      const res = await campaignUploadService.validateCsv(selectedFile, toApiChannel(channel))
      if (res.success && res.data) {
        setValidationData(res.data)
        setCsvErrors(res.data.errors || [])
        if (res.data.validCount === 0) {
          toast.error('No valid records found in the CSV file. See the row errors below.')
        } else {
          toast.success(res.message)
        }
      } else {
        setValidationData(null)
        setCsvErrors([])
        toast.error(res.message || 'cannot upload wrong format csv file')
      }
    } catch (err) {
      setValidationData(null)
      setCsvErrors([])
      toast.error('cannot upload wrong format csv file')
    } finally {
      setIsUploading(false)
    }
  }

  // Download sample CSV from backend
  const [isDownloadingSample, setIsDownloadingSample] = useState(false)

  const handleDownloadSample = async () => {
    setIsDownloadingSample(true)
    try {
      await campaignUploadService.downloadCsvSample()
    } catch {
      toast.error('Could not download the sample file. Please try again.')
    } finally {
      setIsDownloadingSample(false)
    }
  }

  // Compile variable requests payload
  const compileVariables = () => {
    const list: any[] = []

    // Email placeholders are named ({{first_name}}), not positional ({{1}}), and carry no media
    // attachment — so this branch cannot be folded into the WhatsApp one below.
    if (isEmailChannel) {
      Object.entries(emailVarValues).forEach(([name, value]) => {
        list.push({ variableName: name, variableValue: value })
      })
      return list
    }

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
    if (!validationData || validationData.validCount === 0) {
      toast.error('Please upload a CSV file with at least one valid record.')
      return
    }
    if (isEmailChannel) {
      if (!emailTemplateId) {
        toast.error('Please select an Email Template.')
        return
      }
      if (!senderIdentityId) {
        toast.error('Please select a Sender Email.')
        return
      }
    } else if (!selectedTemplateId) {
      toast.error('Please select a Template.')
      return
    }

    // Verify all template variables are filled
    if (!isEmailChannel && selectedTemplate && selectedTemplate.variables) {
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
        channel: toApiChannel(channel),

        // Exactly one of these identifies the content, per channel. The server applies the same
        // rule and ignores the other, so sending 0 for the unused one is harmless.
        templateId: isEmailChannel ? 0 : Number(selectedTemplateId),
        emailTemplateId: isEmailChannel ? emailTemplateId : null,
        senderIdentityId: isEmailChannel ? senderIdentityId : null,
        replyToOverride: isEmailChannel && replyToOverride.trim() ? replyToOverride.trim() : null,

        relationType,
        scheduleType: sendImmediately ? 'Immediate' : 'Scheduled',
        scheduledAt: sendImmediately ? null : new Date(scheduledTime).toISOString(),
        variables: compileVariables()
      }

      // Handle multi-connection: create one campaign per connection. Email sends once — the
      // chosen sender already determines which connection carries it, so there is nothing to fan
      // out across.
      const connIds = isEmailChannel
        ? [undefined]
        : selectedConnectionIds.length > 0 ? selectedConnectionIds : [undefined]
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
  /**
   * Everything still stopping this campaign from moving to step 2, named.
   *
   * One list, used for both the button's disabled state and the message beside it, so the
   * button can never be dead for a reason the screen does not give. Each entry names the field
   * as the form labels it.
   */
  const outstanding: string[] = []

  if (campaignName.trim() === '') outstanding.push('a campaign name')
  else if (isNameDuplicate) outstanding.push('a campaign name that is not already taken')

  if (validationData === null) outstanding.push('an uploaded CSV file')

  if (isEmailChannel) {
    // The sender is what the message is addressed from, and the server refuses one that cannot
    // send — so it is required here rather than discovered at dispatch.
    if (senderIdentityId === null) {
      outstanding.push(
        emailSenders.length === 0
          ? 'an email sender — add one under Connections → Email'
          : 'a Sender Email')
    }
    if (emailTemplateId === null) outstanding.push('an Email Template')
  } else if (selectedTemplateId === '') {
    outstanding.push('a Template')
  }

  const isDetailsValid = outstanding.length === 0

  return (
    <motion.div className="bulk-campaign-container" {...pageTransitionProps}>
      {/* Page hero — matches the host's banner treatment. Presentational only. */}
      <div className="omni-page-hero">
        <h2 className="bulk-campaign-title">Campaigns from CSV File</h2>
        <p>Upload a CSV to create and send a campaign to many recipients at once.</p>
      </div>

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
            {/* Channel first, exactly as in the campaign wizard: every field below it means
                something different depending on this answer, so asking it afterwards would
                invalidate what the operator had already filled in. */}
            <h3 className="bulk-card-title">Channel</h3>
            <div className="bulk-channel-grid">
              {AVAILABLE_CHANNELS.map((definition) => (
                <ChannelCard
                  key={definition.key}
                  channel={definition}
                  selected={channel === definition.key}
                  onSelect={(key) => handleChannelChange(key as MessageChannel)}
                  configured={
                    definition.key === 'email'
                      ? emailSenders.some(s => s.sender.canSend)
                      : connections.some(c => c.isConnected && c.phoneNumber)
                  }
                  unconfiguredHint={
                    definition.key === 'email'
                      ? 'No verified sender yet'
                      : 'No connected number yet'
                  }
                />
              ))}
            </div>

            {/* Listed but disabled. This answers "does this product do SMS?" without pretending
                that it does. */}
            <div className="bulk-channel-soon">
              <span className="bulk-channel-soon-label">Coming Soon</span>
              <div className="bulk-channel-grid">
                {PLANNED_CHANNELS.map((definition) => (
                  <ChannelCard
                    key={definition.key}
                    channel={definition}
                    selected={false}
                    onSelect={() => {}}
                  />
                ))}
              </div>
            </div>

            <h3 className="bulk-card-title margin-top-24">Campaign</h3>

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

            {/* Sender — an email connection's verified identity, or a WABA number. */}
            {isEmailChannel ? (
              <>
                <div className="form-group form-group-required margin-top-20">
                  <label className="form-label">Sender Email</label>
                  <ChoicePills
                    ariaLabel="Sender email"
                    options={emailSenders.map(({ sender, connection }) => ({
                      value: sender.id,
                      label: sender.displayName,
                      badge: connection.connectionName,
                      hint: sender.emailAddress,
                      // A sender whose sending domain is not verified is shown rather than
                      // hidden, with the reason attached: "where did my sender go" is a worse
                      // experience than "here is why you cannot use it yet".
                      disabled: !sender.canSend,
                      disabledReason: sender.canSend
                        ? null
                        : 'This sender cannot send yet. Verify its domain under Connections.'
                    }))}
                    selected={senderIdentityId === null ? [] : [senderIdentityId]}
                    onChange={(next) => setSenderIdentityId(next.length > 0 ? Number(next[0]) : null)}
                    emptyMessage={
                      isLoadingEmailOptions
                        ? 'Loading senders…'
                        : 'No verified sender yet. Add one under Connections → Email.'
                    }
                  />
                </div>

                {/* From Name and From Email are the sender's own, shown read-only rather than as
                    editable fields: a mail server only sends from the addresses its account may use, so an
                    editable box here would only invite a value the send path must reject. */}
                {selectedEmailSender && (
                  <div className="bulk-sender-summary">
                    <div>
                      <span className="bulk-sender-label">From Name</span>
                      <span className="bulk-sender-value">{selectedEmailSender.sender.displayName}</span>
                    </div>
                    <div>
                      <span className="bulk-sender-label">From Email</span>
                      <span className="bulk-sender-value">{selectedEmailSender.sender.emailAddress}</span>
                    </div>
                  </div>
                )}

                <div className="form-group margin-top-20">
                  <label className="form-label">Reply-To (optional)</label>
                  <input
                    type="email"
                    className="form-control"
                    placeholder={
                      selectedEmailSender?.sender.replyTo || 'Replies go to the sender address'
                    }
                    value={replyToOverride}
                    onChange={(e) => setReplyToOverride(e.target.value)}
                  />
                </div>
              </>
            ) : (
            <div className="form-group margin-top-20">
              <label className="form-label">Sender Connection(s) <span style={{ color: '#dc2626' }}>*</span></label>
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
                        border: isSelected ? '1.5px solid #3b82f6' : '1.5px solid #e2e8f0',
                        background: isSelected ? 'linear-gradient(135deg, #eef2ff, #dbeafe)' : '#f8fafc',
                        color: isSelected ? '#1d4ed8' : '#475569',
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
                      {conn.nickname && (
                        <span style={{
                          fontSize: '10px', fontWeight: 700, letterSpacing: '0.03em',
                          padding: '2px 6px', borderRadius: '4px',
                          backgroundColor: 'rgba(255,255,255,0.6)', border: '1px solid rgba(99,102,241,0.25)',
                          color: isSelected ? '#1d4ed8' : '#475569'
                        }}>
                          {conn.nickname}
                        </span>
                      )}
                      <span style={{ fontSize: '11px', opacity: 0.7 }}>{conn.phoneNumber}</span>
                    </button>
                  )
                })}
                {connections.filter(c => c.isConnected && c.phoneNumber).length === 0 && (
                  <span style={{ fontSize: '13px', color: '#dc2626' }}>No connected WABA numbers found</span>
                )}
              </div>
            </div>
            )}

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

            {/* Summary + row-level errors: exactly which row/column failed and why, instead of
                a single opaque toast. Valid rows still proceed regardless. Shared with the
                contacts importer so the two report failures identically. */}
            {(validationData || csvErrors.length > 0) && (
              <CsvRowErrors
                summary={validationData && (
                  <>
                    Out of the {validationData.totalRecords} records in your CSV file,{' '}
                    <strong className="csv-summary-strong">{validationData.validCount}</strong> records are valid. The
                    campaign can be successfully sent to these {validationData.validCount} User.
                  </>
                )}
                summaryIsWarning={validationData?.validCount === 0}
                errors={csvErrors}
              />
            )}

            {/* Template selector dropdown */}
            {isEmailChannel ? (
              validationData ? (
                <div className="form-group form-group-required margin-top-20 fade-in">
                  <label className="form-label">Email Template</label>
                  <select
                    className="form-control"
                    value={emailTemplateId ?? ''}
                    onChange={(e) => {
                      const next = e.target.value === '' ? null : Number(e.target.value)
                      setEmailTemplateId(next)
                      // Values belong to the template that declared them; carrying them across
                      // would leave inputs labelled for placeholders the new body never uses.
                      setEmailVarValues({})
                    }}
                    disabled={isLoadingEmailOptions}
                    required
                  >
                    <option value="">Nothing Selected</option>
                    {emailTemplates.map(t => (
                      <option key={t.id} value={t.id}>
                        {t.name}{t.language ? ` (${t.language})` : ''}
                      </option>
                    ))}
                  </select>
                  {isLoadingEmailOptions && (
                    <div className="bulk-inline-hint">Loading email templates…</div>
                  )}
                  {!isLoadingEmailOptions && emailTemplates.length === 0 && (
                    <div className="bulk-inline-hint">
                      No enabled email templates. Create one under Setup → Email Templates.
                    </div>
                  )}
                </div>
              ) : null
            ) : validationData && selectedConnectionIds.length > 0 ? (
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
                  <div style={{ fontSize: '12px', color: '#3b82f6', marginTop: '4px', fontWeight: 500 }}>
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
                title={isDetailsValid ? undefined : `Still needed: ${outstanding.join(', ')}`}
              >
                <span>Next</span>
                <ChevronRight size={16} />
              </button>
            </div>

            {/* Beside the button, not only in a tooltip: the field in question is often scrolled
                off the top of the form by the time the operator reaches Next. */}
            {!isDetailsValid && (
              <p className="bulk-inline-hint bulk-outstanding">
                Still needed: {outstanding.join(', ')}.
              </p>
            )}
          </div>

          {/* Conditional Template Preview pane in Step 1 */}
          {(isEmailChannel ? selectedEmailTemplate : selectedTemplate) && (
            <div className="bulk-card preview-card-step-1 fade-in">
              <h3 className="bulk-card-title">Preview</h3>
              <div className="preview-bubble-wrapper">
                {isEmailChannel ? (
                  <EmailPreview
                    fromName={selectedEmailSender?.sender.displayName}
                    fromAddress={selectedEmailSender?.sender.emailAddress}
                    replyTo={replyToOverride || selectedEmailSender?.sender.replyTo}
                    subject={emailPreview?.subject ?? selectedEmailTemplate?.subject}
                    bodyHtml={emailPreview?.bodyHtml ?? selectedEmailTemplate?.bodyHtml}
                    isLoading={isPreviewLoading}
                  />
                ) : (
                  <WhatsAppPreview bodyText={getPreviewBody()} />
                )}
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

            {/* Upload Area for header attachment — a WhatsApp template header. Email
                attachments are a different concept entirely and are not offered here. */}
            <div
              className="contacts-selection-card margin-bottom-20"
              hidden={isEmailChannel}
            >
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
            {isEmailChannel ? (
              // Driven by what the body actually references, extracted server-side — the
              // template's declared variable list goes stale the moment somebody edits the body.
              (selectedEmailTemplate?.detectedVariables ?? []).length > 0 ? (
                <div className="contacts-selection-card">
                  {selectedEmailTemplate!.detectedVariables.map(name => (
                    <div key={name} className="form-group margin-bottom-15">
                      <label className="form-label">{name}</label>
                      <input
                        type="text"
                        className="form-control"
                        placeholder={`Enter value for {{${name}}}`}
                        value={emailVarValues[name] ?? ''}
                        onChange={(e) =>
                          setEmailVarValues(prev => ({ ...prev, [name]: e.target.value }))
                        }
                      />
                    </div>
                  ))}
                  {(emailPreview?.unresolvedVariables ?? []).length > 0 && (
                    // Surfaced rather than hidden: an unresolved placeholder ships to the
                    // recipient as literal braces, and the operator should see that before
                    // sending, not after.
                    <p className="bulk-inline-hint">
                      Still unresolved: {emailPreview!.unresolvedVariables.join(', ')}
                    </p>
                  )}
                </div>
              ) : (
                <div className="bulk-empty-state">
                  <CheckCircle size={28} className="text-green" />
                  <span>This template has no variables to customize.</span>
                </div>
              )
            ) : selectedTemplate?.variables && selectedTemplate.variables.length > 0 ? (
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

          {/* Column 2: Live message preview & campaign submission */}
          <div className="step-2-right-column">

            {/* Live Preview Card */}
            <div className="bulk-card">
              <h3 className="bulk-card-title">Preview</h3>
              <div className="preview-bubble-wrapper">
                {isEmailChannel ? (
                  <EmailPreview
                    fromName={selectedEmailSender?.sender.displayName}
                    fromAddress={selectedEmailSender?.sender.emailAddress}
                    replyTo={replyToOverride || selectedEmailSender?.sender.replyTo}
                    subject={emailPreview?.subject ?? selectedEmailTemplate?.subject}
                    bodyHtml={emailPreview?.bodyHtml ?? selectedEmailTemplate?.bodyHtml}
                    isLoading={isPreviewLoading}
                  />
                ) : (
                  <WhatsAppPreview bodyText={getPreviewBody()} />
                )}
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
                  
                  {/* Disabled rather than hidden: the wizard is walkable without the grant, and
                      a footer with a recipient count but no button reads as a broken page. */}
                  <Can permission="BulkCampaign.Create" mode="disable">
                    <button
                      type="submit"
                      className="btn-wizard-nav btn-wizard-save"
                      // Per channel. selectedTemplateId is the WhatsApp template and is always '' on the
            // email path, so this gate disabled the button permanently for every email campaign
            // — handleSendCampaign and isDetailsValid were both made channel-aware and this was
            // missed. Checking emailTemplateId/senderIdentityId mirrors what the handler itself
            // validates, so the button is enabled exactly when submitting would succeed.
            disabled={
              isSubmitting || !validationData ||
              (isEmailChannel ? !emailTemplateId || !senderIdentityId : !selectedTemplateId)
            }
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
                  </Can>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {/* Download Sample modal popup dialog */}
      <Modal
        isOpen={isSampleModalOpen}
        onClose={() => setIsSampleModalOpen(false)}
        title="Download Sample"
        size="lg"
        footer={
          <button
            type="button"
            className="oc-dialog-btn oc-dialog-btn-secondary"
            onClick={() => setIsSampleModalOpen(false)}
          >
            Cancel
          </button>
        }
      >
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
                    disabled={isDownloadingSample}
                  >
                    <Download size={14} />
                    <span>{isDownloadingSample ? 'Preparing…' : 'Download Sample'}</span>
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
                        <td>+15551234567</td>
                        <td>abc@gmail.com</td>
                        <td>Sample Data</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
      </Modal>
    </motion.div>
  )
}

export default BulkCampaign
