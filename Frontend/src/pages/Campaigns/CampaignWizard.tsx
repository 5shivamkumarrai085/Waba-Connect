import React, { useEffect, useState, useRef } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate, useParams } from 'react-router-dom'
import { useCampaignStore } from '../../store/campaignStore'
import { useConnectionStore } from '../../store/connectionStore'
import { campaignService } from '../../services/campaigns/campaignService'
import { contactService } from '../../services/contacts/contactService'
import { templateService } from '../../services/templates/templateService'
import { wabaService } from '../../services/waba/wabaService'
import { Stepper } from '../../components/Stepper/Stepper'
import { WhatsAppPreview } from '../../components/WhatsAppPreview/WhatsAppPreview'
import { ChannelCard } from '../../components/ChannelCard/ChannelCard'
import { ChoicePills } from '../../components/ChoicePills/ChoicePills'
import { EmailPreview } from '../../components/EmailPreview/EmailPreview'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { emailTemplateService } from '../../services/email/emailTemplateService'
import { AVAILABLE_CHANNELS, PLANNED_CHANNELS } from '../../types/channel'
import type { MessageChannel } from '../../types/channel'
import type { EmailConnection, EmailTemplate, EmailTemplatePreview } from '../../types/email'
import type { Contact, ContactStatus, ContactSource } from '../../types/contacts'
import type { Template } from '../../types/templates'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { 
  Play, 
  Clock,
  UploadCloud,
  FileText,
  Trash2,
  Loader2,
  Plus,
  Image as ImageIcon
} from 'lucide-react'
import toast from 'react-hot-toast'
import './CampaignWizard.css'
import { getErrorMessage } from '../../utils/errorHelper'

/**
 * The wizard's steps. 'Select Channel' was prepended when the email channel arrived, which
 * shifted every subsequent index by one — hence the named constants rather than the literal
 * step numbers the handlers used to compare against.
 */
const WIZARD_STEPS = ['Select Channel', 'Basic Info', 'Contact Selection', 'Variables Files', 'Scheduling']

const STEP_CHANNEL = 0
const STEP_BASIC_INFO = 1
const STEP_CONTACTS = 2
const STEP_VARIABLES = 3
const STEP_SCHEDULING = 4
const LAST_STEP = STEP_SCHEDULING

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

  // Derived once, right after the form is available: several effects and handlers below branch
  // on it, and declaring it further down left them referencing it before initialisation.
  const isEmailChannel = wizardForm.channel === 'email'

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
  const [isLoadingTemplates, setIsLoadingTemplates] = useState(false)

  // Email-channel options. Loaded only once the email channel is actually selected — a WhatsApp
  // campaign should not pay for two requests it will never use.
  const [emailConnections, setEmailConnections] = useState<EmailConnection[]>([])
  const [emailTemplates, setEmailTemplates] = useState<EmailTemplate[]>([])
  const [isRecheckingSender, setIsRecheckingSender] = useState(false)
  const [isLoadingEmailOptions, setIsLoadingEmailOptions] = useState(false)

  // The preview is rendered server-side, by the same renderer the send path uses. A client-side
  // approximation would eventually disagree with what actually gets sent, which is the one thing
  // a preview must not do.
  const [emailPreview, setEmailPreview] = useState<EmailTemplatePreview | null>(null)
  const [isPreviewLoading, setIsPreviewLoading] = useState(false)

  // Live duplicate-name check — mirrors BulkCampaign.tsx's debounced pattern so the
  // warning appears while the user is typing/pausing, not only after clicking Next.
  const [isNameDuplicate, setIsNameDuplicate] = useState(false)

  useEffect(() => {
    if (activeStep === LAST_STEP) {
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
        const [cts, stats, srcs] = await Promise.all([
          contactService.getContacts(),
          contactService.getContactStatuses(),
          contactService.getContactSources(),
          wabaService.getDashboard()
        ])

        if (!isMounted) return
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
          // In edit mode, load templates dynamically from connection
          let editTemplates: Template[] = []
          if (details.campaign && (details.campaign as any).connectionId) {
            editTemplates = await templateService.getTemplatesByConnection((details.campaign as any).connectionId)
          } else {
            // Fallback: try all connected connections
            const connList = useConnectionStore.getState().connections
            const connectedIds = connList.filter((c: any) => c.isConnected && c.phoneNumber).map((c: any) => c.id)
            for (const cid of connectedIds) {
              const ct = await templateService.getTemplatesByConnection(cid)
              editTemplates = [...editTemplates, ...ct]
            }
          }
          setTemplatesList(editTemplates)
          const template = editTemplates.find((t: Template) => t.name === details.campaign.templateName)

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
            relationType: details.campaign.relationType
              ? details.campaign.relationType.split(',').map(s => s.trim()).filter(Boolean)
              : [],
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

  // Live duplicate-name check, debounced 400ms after the user stops typing.
  // checkNameExists(name, campaignId) excludes the campaign's own current name in
  // edit mode, so renaming back to what it already was doesn't false-positive.
  useEffect(() => {
    if (!wizardForm.name.trim()) {
      setIsNameDuplicate(false)
      return
    }
    const timer = setTimeout(async () => {
      try {
        const exists = await campaignService.checkNameExists(wizardForm.name, campaignId || undefined)
        setIsNameDuplicate(exists)
      } catch (err) {
      }
    }, 400)

    return () => clearTimeout(timer)
  }, [wizardForm.name, campaignId])

  // Loads the email channel's options the first time it is selected.
  useEffect(() => {
    if (!isEmailChannel) return
    if (emailConnections.length > 0 || emailTemplates.length > 0) return

    let isMounted = true
    setIsLoadingEmailOptions(true)

    Promise.all([
      emailConnectionService.getConnections(),
      // enabledOnly, because offering a disabled template would produce a campaign that is
      // refused at dispatch — after the operator has already finished the wizard.
      emailTemplateService.getTemplates(true)
    ])
      .then(([connections, templates]) => {
        if (!isMounted) return
        setEmailConnections(connections)
        setEmailTemplates(templates)
      })
      .finally(() => {
        if (isMounted) setIsLoadingEmailOptions(false)
      })

    return () => {
      isMounted = false
    }
  }, [isEmailChannel])

  /**
   * Asks the server to re-read the selected sender's verification state from SES.
   *
   * Verifying an address in the AWS console and then coming back here is the ordinary order of
   * events, and without this the operator's only options were to wait out the server's staleness
   * window or re-add the sender.
   */
  const handleRecheckSender = async () => {
    if (!selectedEmailSender) return

    const connectionId = selectedEmailSender.connection.id
    const senderId = selectedEmailSender.sender.id

    setIsRecheckingSender(true)
    try {
      const { senders, message } = await emailConnectionService.refreshSender(connectionId, senderId)

      // An empty list means the call failed, not that the connection lost its senders — replacing
      // good state with it would empty the dropdown the operator is looking at.
      if (senders.length > 0) {
        setEmailConnections(prev =>
          prev.map(c => (c.id === connectionId ? { ...c, senders } : c))
        )
      }

      const refreshed = senders.find(s => s.id === senderId)
      if (refreshed?.canSend) toast.success(`${refreshed.emailAddress} is verified.`)
      else toast.error(message)
    } finally {
      setIsRecheckingSender(false)
    }
  }

  // Every sender across every usable email connection, flattened for the Sender Email picker.
  const emailSenders = React.useMemo(
    () =>
      emailConnections
        .filter(c => c.isActive)
        .flatMap(c =>
          c.senders
            .filter(sender => sender.isActive)
            .map(sender => ({ sender, connection: c }))
        ),
    [emailConnections]
  )

  const selectedEmailSender = emailSenders.find(s => s.sender.id === wizardForm.senderIdentityId)
  const selectedEmailTemplate = emailTemplates.find(t => t.id === wizardForm.emailTemplateId)

  // Re-renders the preview whenever the template or the campaign's own variable values change.
  // Debounced, because the variable inputs fire on every keystroke and each change is a request.
  useEffect(() => {
    if (!isEmailChannel || !wizardForm.emailTemplateId) {
      setEmailPreview(null)
      return
    }

    const templateId = wizardForm.emailTemplateId
    const values = Object.fromEntries(
      (wizardForm.variables ?? [])
        .filter(v => v.variableName !== 'file')
        .map(v => [v.variableName, v.variableValue])
    )

    let isMounted = true
    setIsPreviewLoading(true)

    const timer = setTimeout(async () => {
      const preview = await emailTemplateService.previewTemplate(templateId, values)
      if (!isMounted) return
      setEmailPreview(preview)
      setIsPreviewLoading(false)
    }, 350)

    return () => {
      isMounted = false
      clearTimeout(timer)
    }
  }, [isEmailChannel, wizardForm.emailTemplateId, wizardForm.variables])

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
  const steps = WIZARD_STEPS

  // Step 2 Filtered contacts logic
  const filteredContacts = contactsList.filter((c) => {
    if (wizardForm.relationType && wizardForm.relationType.length > 0) {
      const selectedTypesLower = wizardForm.relationType.map(rt => rt.toLowerCase().trim())
      const contactType = (c.type || (c as any).relationType || '').toLowerCase().trim()
      if (!selectedTypesLower.includes(contactType)) return false
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

  /**
   * Uploads one email attachment and appends it to the campaign.
   *
   * Appends rather than replaces, which is the whole difference from the WhatsApp path: a
   * WhatsApp template carries exactly one media header, while an email can carry several.
   */
  const handleEmailAttachmentUpload = async (file: File) => {
    setUploading(true)
    try {
      const result = await campaignService.uploadFile(file)

      setWizardForm({
        attachments: [
          ...(wizardForm.attachments ?? []),
          {
            url: result.url,
            fileName: result.fileName || file.name,
            contentType: file.type || null,
            sizeBytes: file.size
          }
        ]
      })

      toast.success(`${file.name} attached.`)
    } catch (err) {
      toast.error(getErrorMessage(err, `Could not attach ${file.name}.`))
    } finally {
      setUploading(false)
    }
  }

  // Handlers
  const handleNext = async () => {
    if (activeStep === STEP_CHANNEL) {
      // Nothing to validate beyond the channel being usable: the cards only allow available
      // channels, and whether a channel is configured is surfaced on the card itself rather than
      // blocking here — an operator may legitimately want to set up the connection next.
      if (!wizardForm.channel) {
        toast.error('Please choose a channel for this campaign.')
        return
      }
    }

    if (activeStep === STEP_BASIC_INFO) {
      if (!wizardForm.name || wizardForm.relationType.length === 0) {
        toast.error('Please complete all required fields (*).')
        return
      }

      if (isNameDuplicate) {
        toast.error('same name campaign already executed')
        return
      }

      if (isEmailChannel) {
        if (!wizardForm.senderIdentityId) {
          toast.error('Please choose the sender email for this campaign.')
          return
        }

        if (!wizardForm.emailTemplateId) {
          toast.error('Please choose an email template.')
          return
        }

        // Mirrors the server's send gate, so an unverified sender is refused here — where the
        // operator is looking at it — rather than failing later in a background worker.
        if (selectedEmailSender && !selectedEmailSender.sender.canSend) {
          toast.error(
            'This sender cannot send yet. Verify its domain under Connections before using it.',
            { duration: 5000 }
          )
          return
        }
      } else {
        if (!wizardForm.templateName) {
          toast.error('Please complete all required fields (*).')
          return
        }

        // The WhatsApp daily-limit check. Deliberately not run for email, which has its own
        // per-connection rate limit enforced server-side by the dispatch worker.
        try {
          const connIds: number[] = wizardForm.connectionIds ?? []
          const selectedConnId = connIds.length > 0 ? connIds[0] : undefined
          const checkResult = await wabaService.checkLimitFast(selectedConnId)
          if (checkResult.limitReached) {
            toast.error(`${checkResult.message} Cannot create campaign for this connection.`, { duration: 4000 })
            return
          }
        } catch (err) {
          console.error('Error checking connection limit:', err)
        }
      }
    }

    if (activeStep === STEP_VARIABLES) {
      setCooldownActive(true)
    }

    setActiveStep(activeStep + 1)
  }

  const handlePrevious = () => {
    setActiveStep(activeStep - 1)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    if (activeStep < LAST_STEP) {
      await handleNext()
      return
    }

    if (cooldownActive) {
      console.warn('Prevented auto-submit cooldown click')
      return
    }

    if (!wizardForm.name || wizardForm.relationType.length === 0) {
      toast.error('Please complete campaign name and relation type.')
      return
    }

    // Each channel has its own template reference, and exactly one of them must be set.
    if (isEmailChannel ? !wizardForm.emailTemplateId : !wizardForm.templateId) {
      toast.error('Please choose a template.')
      return
    }

    if (isEmailChannel && !wizardForm.senderIdentityId) {
      toast.error('Please choose the sender email for this campaign.')
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
    <motion.div {...pageTransitionProps}>
      {/* Page Title with horizontal badges row */}
      <div className="wizard-title-row">
        <h2 className="wizard-title">{isEditMode ? 'Edit Campaign' : 'Create Campaign'}</h2>
        <div className="wizard-badges-row">
          <div className="wizard-badge blue">
            <span>Channel: {isEmailChannel ? 'Email' : 'WhatsApp'}</span>
          </div>
          <div className="wizard-badge purple">
            <span>Recipients: {finalRecipientsCount}</span>
          </div>
          <div className="wizard-badge purple">
            <span>
              Template:{' '}
              {(isEmailChannel ? selectedEmailTemplate?.name : wizardForm.templateName) || 'not selected'}
            </span>
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
              
              {/* Step 1: Select Channel */}
              {activeStep === STEP_CHANNEL && (
                <div className="fade-in">
                  <div className="form-group margin-top-20">
                    <h3 className="upload-main-text">Select Communication Channel</h3>
                    <p className="upload-sub-text">Choose the channel for this campaign</p>
                  </div>

                  <div className="channel-card-grid">
                    {AVAILABLE_CHANNELS.map((channel) => {
                      // "Already Configured" is derived from live connection state rather than
                      // hardcoded: it has to mean something, or an operator trusts it and then
                      // finds the campaign cannot send.
                      const configured =
                        channel.key === 'whatsapp'
                          ? connections.some(c => c.isConnected && c.phoneNumber)
                          : emailConnections.some(c => c.isActive && c.senders.some(sender => sender.canSend))

                      return (
                        <ChannelCard
                          key={channel.key}
                          channel={channel}
                          selected={wizardForm.channel === channel.key}
                          configured={configured}
                          unconfiguredHint={
                            channel.key === 'email'
                              ? 'No verified sender yet'
                              : 'No connected number yet'
                          }
                          onSelect={(key) => {
                            // Switching channel clears the other channel's template and sender.
                            // Carrying them over would leave a campaign referencing a template
                            // from a channel it is no longer on.
                            setWizardForm({
                              channel: key as MessageChannel,
                              templateId: 0,
                              templateName: '',
                              emailTemplateId: undefined,
                              emailTemplateName: '',
                              senderIdentityId: undefined,
                              connectionIds: []
                            })
                            setTemplatesList([])
                          }}
                        />
                      )
                    })}
                  </div>

                  {PLANNED_CHANNELS.length > 0 && (
                    <div className="channel-planned-section">
                      <p className="upload-sub-text channel-planned-title">Coming Soon</p>
                      <div className="channel-card-grid">
                        {PLANNED_CHANNELS.map((channel) => (
                          <ChannelCard
                            key={channel.key}
                            channel={channel}
                            selected={false}
                            onSelect={() => undefined}
                          />
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              )}

              {/* Step 2: Basic Info */}
              {activeStep === STEP_BASIC_INFO && (
                <div className="fade-in">
                  <div className="form-group margin-top-20">
                    <h3 className="upload-main-text">Basic Information</h3>
                    <p className="upload-sub-text">Enter campaign details and select template</p>
                  </div>

                  <div className="form-group form-group-required margin-top-20">
                    <label className="form-label">Campaign Name</label>
                    <input
                      type="text"
                      className={`form-control ${isNameDuplicate ? 'is-invalid' : ''}`}
                      placeholder="Enter campaign name"
                      value={wizardForm.name}
                      onChange={(e) => setWizardForm({ name: e.target.value })}
                      required
                    />
                    {isNameDuplicate && (
                      <span className="error-text-warning">* same name campaign already executed</span>
                    )}
                  </div>

                  {isEmailChannel ? (
                    <>
                      <div className="form-group form-group-required margin-top-20">
                        <label className="form-label">
                          Sender Email <span className="form-label-required">*</span>
                          <span
                            className="form-label-hint"
                            title="Campaigns are sent from this verified address. Add more senders under Connections."
                          >
                            &#9432;
                          </span>
                        </label>

                        <div className="wizard-inline-field">
                          <select
                            className="form-control"
                            value={wizardForm.senderIdentityId ?? ''}
                            onChange={(e) => {
                              const senderId = e.target.value ? parseInt(e.target.value, 10) : undefined
                              const match = emailSenders.find(entry => entry.sender.id === senderId)

                              // The connection is derived from the sender rather than asked for
                              // separately: a sender belongs to exactly one email connection, so
                              // making the operator pick both would only let them disagree.
                              setWizardForm({
                                senderIdentityId: senderId,
                                connectionIds: match?.connection.connectionId
                                  ? [match.connection.connectionId]
                                  : [],
                                replyToOverride: match?.sender.replyTo ?? wizardForm.replyToOverride
                              })
                            }}
                            disabled={isLoadingEmailOptions}
                            required
                          >
                            <option value="">
                              {isLoadingEmailOptions ? 'Loading senders...' : 'Select sender email'}
                            </option>
                            {emailSenders.map(({ sender, connection }) => (
                              <option key={sender.id} value={sender.id} disabled={!sender.canSend}>
                                {sender.emailAddress}
                                {sender.canSend ? '' : ' - not verified'}
                                {connection.connectionName ? ` (${connection.connectionName})` : ''}
                              </option>
                            ))}
                          </select>

                          <button
                            type="button"
                            className="btn-toolbar-tertiary wizard-inline-action"
                            onClick={() => navigate('/connections/new-email')}
                          >
                            <Plus size={14} />
                            Add New Sender
                          </button>
                        </div>

                        {!isLoadingEmailOptions && emailSenders.length === 0 && (
                          <span className="error-text-warning">
                            * No email senders configured. Add an email connection first.
                          </span>
                        )}

                        {selectedEmailSender && !selectedEmailSender.sender.canSend && (
                          <span className="error-text-warning wizard-sender-warning">
                            {/* Names the address, not the domain. An address verified individually
                                in SES needs no verified domain, so blaming the domain sent people
                                off to fix something that was not broken. */}
                            * {selectedEmailSender.sender.emailAddress} is not verified in SES yet,
                            so it cannot send. Verify it in SES, then re-check.
                            <button
                              type="button"
                              className="btn-toolbar-tertiary wizard-inline-action"
                              onClick={handleRecheckSender}
                              disabled={isRecheckingSender}
                            >
                              {isRecheckingSender ? 'Checking…' : 'Re-check'}
                            </button>
                          </span>
                        )}
                      </div>

                      <div className="contacts-filter-row">
                        <div className="form-group">
                          <label className="form-label">
                            Reply To (Optional)
                            <span
                              className="form-label-hint"
                              title="Replies from recipients are delivered to this address."
                            >
                              &#9432;
                            </span>
                          </label>
                          <input
                            type="email"
                            className="form-control"
                            placeholder="support@example.com"
                            value={wizardForm.replyToOverride ?? ''}
                            onChange={(e) => setWizardForm({ replyToOverride: e.target.value })}
                          />
                        </div>

                        <div className="form-group form-group-required">
                          <label className="form-label">
                            Template <span className="form-label-required">*</span>
                          </label>
                          <select
                            className="form-control"
                            value={wizardForm.emailTemplateId ?? ''}
                            onChange={(e) => {
                              const templateId = e.target.value ? parseInt(e.target.value, 10) : undefined
                              const template = emailTemplates.find(t => t.id === templateId)
                              setWizardForm({
                                emailTemplateId: templateId,
                                emailTemplateName: template?.name ?? ''
                              })
                            }}
                            disabled={isLoadingEmailOptions}
                            required
                          >
                            <option value="">
                              {isLoadingEmailOptions ? 'Loading templates...' : 'Select Template'}
                            </option>
                            {emailTemplates.map(template => (
                              <option key={template.id} value={template.id}>
                                {template.name}
                                {template.language ? ` (${template.language})` : ''}
                              </option>
                            ))}
                          </select>

                          {!isLoadingEmailOptions && emailTemplates.length === 0 && (
                            <span className="error-text-warning">
                              * No enabled email templates. Create one under Setup, Email Templates.
                            </span>
                          )}
                        </div>
                      </div>
                    </>
                  ) : (
                    <div className="form-group margin-top-20">
                      <label className="form-label">
                        Sender Connection(s) <span className="form-label-required">*</span>
                      </label>

                      <ChoicePills
                        ariaLabel="Sender connection"
                        emptyMessage="No connected WABA numbers found"
                        selected={wizardForm.connectionIds ?? []}
                        options={connections
                          .filter(c => c.isConnected && c.phoneNumber)
                          .map(conn => ({
                            value: conn.id,
                            label: conn.name,
                            badge: conn.nickname,
                            hint: conn.phoneNumber
                          }))}
                        onChange={async (next) => {
                          const selectedIds = next as number[]

                          // Changing connection invalidates the template list: templates are
                          // per-WABA, so one from the previous connection would be rejected by
                          // Meta on send.
                          setWizardForm({ connectionIds: selectedIds, templateId: 0, templateName: '' })
                          setTemplatesList([])
                          setIsLoadingTemplates(true)

                          try {
                            if (selectedIds.length > 0) {
                              const connectionTemplates = await templateService.getTemplatesByConnection(
                                selectedIds[0]
                              )
                              setTemplatesList(connectionTemplates)
                            }
                            // Deselecting leaves the list empty. There is deliberately no
                            // database-wide fallback, which would offer templates the selected
                            // connection cannot actually send.
                          } finally {
                            setIsLoadingTemplates(false)
                          }
                        }}
                      />
                    </div>
                  )}

                  <div className="contacts-filter-row">
                    <div className="form-group form-group-required">
                      <label className="form-label">
                        Relation Type <span className="form-label-required">*</span>
                      </label>

                      <ChoicePills
                        multiple
                        ariaLabel="Relation type"
                        selected={wizardForm.relationType}
                        options={(['Lead', 'Customer', 'Vendor'] as const).map(rel => ({
                          value: rel,
                          label: rel
                        }))}
                        onChange={(next) => setWizardForm({ relationType: next as string[] })}
                      />
                    </div>

                    {/*
                      The WhatsApp template picker. The email channel selects its template in the
                      block above, alongside its sender, because for email the template and the
                      From address are read together — the preview is meaningless without both.
                    */}
                    {!isEmailChannel && (
                      (wizardForm.connectionIds?.length ?? 0) > 0 ? (
                        <div className="form-group form-group-required">
                          <label className="form-label">
                            Template <span className="form-label-required">*</span>
                          </label>
                          <select
                            className="form-control"
                            value={wizardForm.templateName}
                            onChange={(e) => {
                              const t = templatesList.find(x => x.name === e.target.value)
                              setWizardForm({ templateName: e.target.value, templateId: t?.id || 0 })
                            }}
                            disabled={isLoadingTemplates}
                            required
                          >
                            <option value="">Select Template</option>
                            {templatesList
                              .filter(t => t.status?.toLowerCase() === 'approved')
                              .map(t => (
                                <option key={t.id} value={t.name}>{t.name}</option>
                              ))}
                          </select>
                          {isLoadingTemplates && (
                            <span className="form-hint-loading">Loading approved templates...</span>
                          )}
                        </div>
                      ) : (
                        <div className="form-group">
                          <label className="form-label">Template</label>
                          <p className="form-hint-muted">Select a connection to load templates</p>
                        </div>
                      )
                    )}
                  </div>
                </div>
              )}

              {/* Step 3: Contact Selection */}
              {activeStep === STEP_CONTACTS && (
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
                                  {/*
                                    The column follows the channel, because it is the address the
                                    campaign will actually use. Showing Phone for an email
                                    campaign would hide the one field that decides whether a
                                    recipient is reachable.
                                  */}
                                  <th>{isEmailChannel ? 'Email' : 'Phone'}</th>
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
                                    <td>
                                      {isEmailChannel ? (
                                        c.email ? (
                                          c.email
                                        ) : (
                                          // Called out rather than left blank: this contact will
                                          // be skipped, and the operator should see that before
                                          // the campaign reports it as a failure.
                                          <span className="contacts-missing-value">No email address</span>
                                        )
                                      ) : (
                                        c.phone
                                      )}
                                    </td>
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

              {/* Step 4: Variables & Files */}
              {activeStep === STEP_VARIABLES && (
                <div className="fade-in">
                  <div className="form-group">
                    <h3 className="upload-main-text">Variables and Files</h3>
                    <p className="upload-sub-text">
                      {isEmailChannel
                        ? 'Customize your email with variables and document attachments.'
                        : 'Customize your message with variables and media template attachments.'}
                    </p>
                  </div>

                  {isEmailChannel ? (
                    <>
                      <div className="contacts-selection-card margin-top-20">
                        <div className="upload-area-header">
                          <span className="upload-area-label">Template Variables (Optional)</span>
                        </div>
                        <p className="upload-sub-text">
                          These are replaced with each contact&apos;s own data while sending.
                        </p>

                        {/*
                          Read from the template's content, not from a hardcoded list. The server
                          extracts them with the same renderer that performs the substitution, so
                          the inputs shown here cannot drift from the fields the send actually
                          resolves — which is exactly what a declared variable list does the
                          moment somebody edits a body.
                        */}
                        {!selectedEmailTemplate ? (
                          <p className="form-hint-muted">Choose a template to see its variables.</p>
                        ) : selectedEmailTemplate.detectedVariables.length === 0 ? (
                          <p className="form-hint-muted">
                            This template has no variables. Nothing to fill in here.
                          </p>
                        ) : (
                          <>
                            <div className="variable-chip-row">
                              {selectedEmailTemplate.detectedVariables.map(name => (
                                <span key={name} className="variable-chip">
                                  {`{{${name}}}`}
                                </span>
                              ))}
                            </div>

                            <div className="variable-input-grid">
                              {selectedEmailTemplate.detectedVariables.map(name => {
                                const current =
                                  wizardForm.variables?.find(v => v.variableName === name)?.variableValue ?? ''

                                // Contact fields the server fills in per recipient. Offering an
                                // input for them would imply one value for everyone, which is
                                // the opposite of what a merge field is for.
                                const isContactField = [
                                  'name', 'first_name', 'last_name', 'contact_name',
                                  'email', 'phone', 'contact_phone'
                                ].includes(name.toLowerCase())

                                return (
                                  <div className="form-group" key={name}>
                                    <label className="form-label">{`{{${name}}}`}</label>
                                    <input
                                      type="text"
                                      className="form-control"
                                      placeholder={
                                        isContactField
                                          ? 'Filled from each contact automatically'
                                          : `Value for ${name}`
                                      }
                                      value={current}
                                      disabled={isContactField}
                                      onChange={(e) => {
                                        const others = (wizardForm.variables ?? []).filter(
                                          v => v.variableName !== name
                                        )
                                        setWizardForm({
                                          variables: [
                                            ...others,
                                            { variableName: name, variableValue: e.target.value }
                                          ]
                                        })
                                      }}
                                    />
                                  </div>
                                )
                              })}
                            </div>
                          </>
                        )}
                      </div>

                      <div className="contacts-selection-card margin-top-20">
                        <div className="upload-area-header">
                          <span className="upload-area-label">Document Attachments (Optional)</span>
                        </div>

                        <div
                          className={`upload-dropzone ${dragActive ? 'drag-active' : ''}`}
                          onDragEnter={(e) => { e.preventDefault(); setDragActive(true); }}
                          onDragOver={(e) => { e.preventDefault(); setDragActive(true); }}
                          onDragLeave={() => setDragActive(false)}
                          onDrop={async (e) => {
                            e.preventDefault()
                            setDragActive(false)
                            for (const file of Array.from(e.dataTransfer.files ?? [])) {
                              await handleEmailAttachmentUpload(file)
                            }
                          }}
                          onClick={() => fileInputRef.current?.click()}
                        >
                          <input
                            ref={fileInputRef}
                            type="file"
                            /* Several at once, unlike the WhatsApp media header, which is one
                               file by protocol. */
                            multiple
                            style={{ display: 'none' }}
                            onChange={async (e) => {
                              for (const file of Array.from(e.target.files ?? [])) {
                                await handleEmailAttachmentUpload(file)
                              }
                              e.target.value = ''
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
                          <p className="upload-sub-text">PDF, DOCX, PNG, JPG, up to 10MB each</p>
                        </div>

                        {(wizardForm.attachments?.length ?? 0) > 0 && (
                          <div className="attachment-list">
                            <span className="upload-area-label">
                              Attached Files ({wizardForm.attachments?.length})
                            </span>

                            {wizardForm.attachments?.map(attachment => (
                              <div className="upload-file-details" key={attachment.url}>
                                {attachment.contentType?.startsWith('image/') ? (
                                  <ImageIcon className="upload-file-icon" size={24} />
                                ) : (
                                  <FileText className="upload-file-icon" size={24} />
                                )}

                                <div className="upload-file-info">
                                  <span className="upload-file-name">{attachment.fileName}</span>
                                  <span className="upload-file-size">
                                    {formatFileSize(attachment.sizeBytes)}
                                  </span>
                                </div>

                                <button
                                  type="button"
                                  className="upload-file-remove-btn"
                                  onClick={() =>
                                    setWizardForm({
                                      attachments: (wizardForm.attachments ?? []).filter(
                                        a => a.url !== attachment.url
                                      )
                                    })
                                  }
                                  aria-label={`Remove ${attachment.fileName}`}
                                >
                                  <Trash2 size={16} />
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    </>
                  ) : (
                    <>
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
                    </>
                  )}
                </div>
              )}

              {/* Step 5: Scheduling */}
              {activeStep === STEP_SCHEDULING && (
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
            {isEmailChannel ? (
              <EmailPreview
                fromName={selectedEmailSender?.sender.displayName}
                fromAddress={selectedEmailSender?.sender.emailAddress}
                replyTo={wizardForm.replyToOverride || selectedEmailSender?.sender.replyTo}
                subject={emailPreview?.subject ?? selectedEmailTemplate?.subject}
                bodyHtml={emailPreview?.bodyHtml ?? selectedEmailTemplate?.bodyHtml}
                attachments={wizardForm.attachments ?? []}
                isLoading={isPreviewLoading}
              />
            ) : (
              <WhatsAppPreview bodyText={getPreviewBody()} />
            )}
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
              {activeStep === LAST_STEP ? (
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
    </motion.div>
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

/**
 * Human-readable file size. Binary units, matching what an operating system shows for the same
 * file — a size the operator can reconcile with what they see in their file browser.
 */
const formatFileSize = (bytes?: number | null): string => {
  if (bytes === null || bytes === undefined || bytes < 0) return 'Unknown size'
  if (bytes < 1024) return `${bytes} B`

  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unitIndex = 0

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }

  return `${value.toFixed(value >= 10 || unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`
}

const getDefaultScheduleTime = () => {
  const date = new Date(Date.now() + 15 * 60 * 1000)
  return toDateTimeLocalValue(date.toISOString())
}

export default CampaignWizard
