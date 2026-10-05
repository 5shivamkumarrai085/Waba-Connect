import React, { useEffect, useState, useRef } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { Link, useNavigate, useParams } from 'react-router-dom'
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
import { segmentService, type Segment } from '../../services/segments/segmentService'
import { PreflightPanel } from './PreflightPanel'
import { AudienceContactPicker } from './AudienceContactPicker'
import { AbTestEditor } from './AbTestEditor'
import { emptyAbTest } from './abTestForm'
import { FollowUpEditor } from './FollowUpEditor'
import { AVAILABLE_CHANNELS, PLANNED_CHANNELS } from '../../types/channel'
import type { MessageChannel } from '../../types/channel'
import type { EmailConnection, EmailTemplate, EmailTemplatePreview } from '../../types/email'
import type { ContactStatus, ContactSource } from '../../types/contacts'
import type { Template } from '../../types/templates'
import { 
  Play, 
  Clock,
  UploadCloud,
  FileText,
  Trash2,
  Loader2,
  Plus,
  Image as ImageIcon,
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
  // Contact types from the managed list (Setup › Contact types), and how many contacts
  // "send to every contact of these types" would include (reported by the audience picker).
  const [contactTypes, setContactTypes] = useState<{ id: string; name: string }[]>([])
  const [selectAllCount, setSelectAllCount] = useState(0)
  const [statuses, setStatuses] = useState<ContactStatus[]>([])
  const [sources, setSources] = useState<ContactSource[]>([])

  // Local search in Step 2 Contact checklist

  // Variable inputs in Step 3
  // Values for the WhatsApp template's numbered placeholders ({{1}}, {{2}}, …), keyed by number.
  const [waVariables, setWaVariables] = useState<Record<string, string>>({})

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
  const [preflightFailed, setPreflightFailed] = useState(false)
  const [segmentOptions, setSegmentOptions] = useState<Segment[]>([])
  const [groupOptions, setGroupOptions] = useState<{ id: number; name: string }[]>([])

  // Segments and groups for the audience step. Either list failing leaves that picker empty.
  useEffect(() => {
    segmentService.list({ pageSize: 200 }).then(r => setSegmentOptions(r.items)).catch(() => setSegmentOptions([]))
    contactService.getContactGroups()
      .then((g: any[]) => setGroupOptions((g ?? []).map(x => ({ id: Number(x.id), name: x.name }))))
      .catch(() => setGroupOptions([]))
  }, [])

  const [emailTemplates, setEmailTemplates] = useState<EmailTemplate[]>([])
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
      setWaVariables({})
      setFileUrl('')
      setFileName('')
      setActiveStep(0)

      try {
        fetchConnectionDashboard()
        const [types, stats, srcs] = await Promise.all([
          contactService.getContactTypes(),
          contactService.getContactStatuses(),
          contactService.getContactSources(),
          wabaService.getDashboard()
        ])

        if (!isMounted) return
        setContactTypes(types.map(t => ({ id: String(t.id), name: t.name })))
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
          const fUrl = vars.find((v: any) => v.variableName === 'file')?.variableValue || ''
          const fName = fUrl ? fUrl.substring(fUrl.lastIndexOf('/') + 1).split('_').slice(1).join('_') : ''

          // Every numbered placeholder the campaign saved, not just the first two.
          setWaVariables(Object.fromEntries(
            vars.filter((v: any) => /^\d+$/.test(v.variableName)).map((v: any) => [v.variableName, v.variableValue ?? ''])
          ))
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
            scheduledTime: details.campaign.localSendAt
              ? details.campaign.localSendAt.slice(0, 16)
              : details.campaign.scheduledAt ? toDateTimeLocalValue(details.campaign.scheduledAt) : '',
            recipientLocalTime: Boolean(details.campaign.localSendAt),
            topic: details.campaign.topic ?? '',
            isTransactional: details.campaign.isTransactional ?? false,
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

  // Email connections load up front: the channel card's "configured" badge is derived from
  // them, and loading them only after Email was picked made that card always read "No verified
  // sender yet".
  useEffect(() => {
    let isMounted = true
    emailConnectionService.getConnections()
      .then(connections => {
        if (isMounted) setEmailConnections(connections)
      })
      .catch(() => { /* the card simply shows as not configured */ })
    return () => {
      isMounted = false
    }
  }, [])

  // Templates load the first time the email channel is selected.
  useEffect(() => {
    if (!isEmailChannel) return
    if (emailTemplates.length > 0) return

    let isMounted = true
    setIsLoadingEmailOptions(true)

    // enabledOnly, because offering a disabled template would produce a campaign that is
    // refused at dispatch — after the operator has already finished the wizard.
    emailTemplateService.getTemplates(true)
      .then(templates => {
        if (isMounted) setEmailTemplates(templates)
      })
      .catch(() => { /* the template picker shows its empty state */ })
      .finally(() => {
        if (isMounted) setIsLoadingEmailOptions(false)
      })

    return () => {
      isMounted = false
    }
  }, [isEmailChannel])

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
  
  // The numbered placeholders the selected WhatsApp template actually uses, in order. Read from
  // the template itself, so any approved template with variables gets its inputs.
  const waPlaceholders = React.useMemo(() => {
    const text = selectedTemplate?.bodyText ?? ''
    return Array.from(new Set(Array.from(text.matchAll(/\{\{\s*(\d+)\s*\}\}/g), m => m[1])))
      .sort((a, b) => Number(a) - Number(b))
  }, [selectedTemplate])

  // The body with the values typed so far; an unfilled placeholder stays visible.
  const getPreviewBody = () => {
    if (!selectedTemplate) return ''
    return (selectedTemplate.bodyText || '').replace(/\{\{\s*(\d+)\s*\}\}/g, (match, n: string) => waVariables[n] || match)
  }

  // Multi-step configurations
  const steps = WIZARD_STEPS

  // Sync local variable states to store's wizardForm.variables
  useEffect(() => {
    if (isEmailChannel) return
    const newVars: { variableName: string; variableValue: string }[] = waPlaceholders.map(n => ({ variableName: n, variableValue: waVariables[n] ?? '' }))
    if (fileUrl) {
      newVars.push({ variableName: 'file', variableValue: fileUrl })
    }
    setWizardForm({ variables: newVars })
  }, [isEmailChannel, waPlaceholders, waVariables, fileUrl, wizardForm.templateName])

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

    if (wizardForm.abTest?.enabled
      && wizardForm.abTest.variants.some(v => !(isEmailChannel ? v.emailTemplateId : v.templateId))) {
      toast.error('Choose a template for every A/B variant, or turn the A/B test off.')
      return
    }

    if (preflightFailed && !wizardForm.overridePrecheck) {
      toast.error('The pre-flight check failed. Fix the items marked in red before sending.')
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

    if (!hasAudience) {
      toast.error('Choose who receives the campaign: contacts, groups, segments or all contacts.')
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

  // Count final recipients count based on selection states. Segment sizes are their last count
  // (they are re-resolved at send time), so the total is an estimate when segments are chosen.
  const selectedSegmentIds = wizardForm.selectedSegmentIds ?? []
  const selectedGroupIds = wizardForm.selectedGroupIds ?? []
  const segmentEstimate = segmentOptions
    .filter(s => selectedSegmentIds.includes(s.id))
    .reduce((sum, s) => sum + (s.cachedCount ?? 0), 0)
  const finalRecipientsCount = wizardForm.selectAllContacts
    ? selectAllCount
    : wizardForm.selectedContactIds.length + segmentEstimate
  const hasAudience = wizardForm.selectAllContacts
    || wizardForm.selectedContactIds.length > 0 || selectedSegmentIds.length > 0 || selectedGroupIds.length > 0
  const toggleId = (list: number[], id: number) => (list.includes(id) ? list.filter(x => x !== id) : [...list, id])

  return (
    <motion.div {...pageTransitionProps}>
      {/* Page Title with horizontal badges row */}
      <div className="wizard-title-row omni-page-hero form-page-hero">
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
                            * {selectedEmailSender.sender.emailAddress} cannot send right now: the sender or its
                            connection is switched off, or the connection has no SMTP server yet.{' '}
                            <Link to="/connections" className="wizard-inline-action">Open Connections</Link>
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
                        options={contactTypes.map(t => ({ value: t.id, label: t.name }))}
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

                  <AbTestEditor
                    channel={isEmailChannel ? 'email' : 'whatsapp'}
                    templates={isEmailChannel
                      ? emailTemplates.filter(t => t.id !== wizardForm.emailTemplateId).map(t => ({ id: t.id, name: t.name }))
                      : templatesList
                          .filter(t => t.status?.toLowerCase() === 'approved' && t.id !== wizardForm.templateId)
                          .map(t => ({ id: t.id, name: t.name }))}
                    value={wizardForm.abTest ?? emptyAbTest()}
                    onChange={(abTest) => setWizardForm({ abTest })}
                    baseTemplateName={isEmailChannel
                      ? emailTemplates.find(t => t.id === wizardForm.emailTemplateId)?.name
                      : wizardForm.templateName}
                    audienceSize={finalRecipientsCount}
                  />
                </div>
              )}

              {/* Step 3: Contact Selection */}
              {activeStep === STEP_CONTACTS && (
                <div className="fade-in">
                  <div className="form-group">
                    <h3 className="upload-main-text">Contact Selection</h3>
                    <p className="upload-sub-text">Choose your target audience</p>
                  </div>

                  {!wizardForm.selectAllContacts && (segmentOptions.length > 0 || groupOptions.length > 0) && (
                    <div className="wizard-audience-sources margin-top-20">
                      {segmentOptions.length > 0 && (
                        <div className="form-group">
                          <label className="form-label">Segments <small>(re-evaluated when the campaign sends)</small></label>
                          <div className="wizard-chip-list">
                            {segmentOptions.map(s => (
                              <button key={s.id} type="button"
                                className={`wizard-chip${selectedSegmentIds.includes(s.id) ? ' is-selected' : ''}`}
                                aria-pressed={selectedSegmentIds.includes(s.id)}
                                onClick={() => setWizardForm({ selectedSegmentIds: toggleId(selectedSegmentIds, s.id) })}>
                                {s.name}{s.cachedCount != null ? ` · ${s.cachedCount.toLocaleString()}` : ''}
                              </button>
                            ))}
                          </div>
                        </div>
                      )}
                      {groupOptions.length > 0 && (
                        <div className="form-group">
                          <label className="form-label">Groups</label>
                          <div className="wizard-chip-list">
                            {groupOptions.map(g => (
                              <button key={g.id} type="button"
                                className={`wizard-chip${selectedGroupIds.includes(g.id) ? ' is-selected' : ''}`}
                                aria-pressed={selectedGroupIds.includes(g.id)}
                                onClick={() => setWizardForm({ selectedGroupIds: toggleId(selectedGroupIds, g.id) })}>
                                {g.name}
                              </button>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                  )}

                  <AudienceContactPicker
                    relationTypes={wizardForm.relationType}
                    typeLabels={Object.fromEntries(contactTypes.map(t => [t.id, t.name]))}
                    isEmailChannel={isEmailChannel}
                    statuses={statuses}
                    sources={sources}
                    selectAll={wizardForm.selectAllContacts}
                    onSelectAllChange={(value) => setWizardForm({ selectAllContacts: value })}
                    selectedIds={wizardForm.selectedContactIds}
                    onSelectedIdsChange={(ids) => setWizardForm({ selectedContactIds: ids })}
                    onSelectAllCountChange={setSelectAllCount}
                    onEditTypes={() => setActiveStep(STEP_BASIC_INFO)}
                  />
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
                            hidden
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
                            hidden
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

                      {/* One input per numbered placeholder in the chosen template. */}
                      {waPlaceholders.length > 0 && (
                        <fieldset className="wizard-panel margin-top-20">
                          <legend className="wizard-panel-legend">Template variables</legend>
                          <p className="ab-editor-note">Every recipient gets the same values. The preview on the right shows them in place.</p>
                          <div className="wizard-variable-grid">
                            {waPlaceholders.map(n => (
                              <div className="form-group" key={n}>
                                <label className="form-label" htmlFor={`wa-var-${n}`}>{`Value for {{${n}}}`}</label>
                                <input
                                  id={`wa-var-${n}`}
                                  type="text"
                                  className="form-control"
                                  value={waVariables[n] ?? ''}
                                  onChange={(e) => setWaVariables(prev => ({ ...prev, [n]: e.target.value }))}
                                />
                              </div>
                            ))}
                          </div>
                        </fieldset>
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
                      <label className="wizard-inline-check">
                        <input
                          type="checkbox"
                          checked={wizardForm.recipientLocalTime ?? false}
                          onChange={(e) => setWizardForm({ recipientLocalTime: e.target.checked })}
                        />
                        <span>
                          Deliver at this time in each recipient's own time zone
                          <small>Contacts without a time zone use the default set under Settings › Compliance.</small>
                        </span>
                      </label>
                    </div>
                  )}

                  <FollowUpEditor
                    channel={isEmailChannel ? 'email' : 'whatsapp'}
                    value={wizardForm.followUps ?? []}
                    onChange={(followUps) => setWizardForm({ followUps })}
                  />

                  <PreflightPanel
                    channel={isEmailChannel ? 'email' : 'whatsapp'}
                    emailTemplateId={wizardForm.emailTemplateId}
                    senderIdentityId={wizardForm.senderIdentityId}
                    subjectOverride={wizardForm.subjectOverride}
                    templateId={wizardForm.templateId}
                    isTransactional={wizardForm.isTransactional}
                    variableNames={(wizardForm.variables ?? []).map(v => v.variableName)}
                    override={wizardForm.overridePrecheck ?? false}
                    onOverrideChange={(value) => setWizardForm({ overridePrecheck: value })}
                    onResult={setPreflightFailed}
                  />
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
