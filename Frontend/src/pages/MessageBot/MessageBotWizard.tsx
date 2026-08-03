import React, { useEffect, useState, useRef } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, Upload, X } from 'lucide-react'
import { useMessageBotStore } from '../../store/messageBotStore'
import { messageBotService } from '../../services/messageBot/messageBotService'
import { useConnectionStore } from '../../store/connectionStore'
import { toast } from 'react-hot-toast'
import './MessageBotWizard.css'
import { getErrorMessage } from '../../utils/errorHelper'

type TabType = 'ReplyButtons' | 'CtaUrl' | 'Files' | 'PersonalAssistant'

export const MessageBotWizard: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const [searchParams] = useSearchParams()
  const isViewMode = searchParams.get('view') === 'true'
  const isEditMode = !!id && !isViewMode

  const { createBot, updateBot } = useMessageBotStore()

  // Form states
  const [name, setName] = useState('')
  const [relationType, setRelationType] = useState('Lead')
  const [replyText, setReplyText] = useState('')
  const [replyType, setReplyType] = useState('On Exact Match')
  const [header, setHeader] = useState('')
  const [footer, setFooter] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [optionType, setOptionType] = useState<TabType>('ReplyButtons')
  const [connectionId, setConnectionId] = useState<number | ''>('')
  const { connections, fetchConnections } = useConnectionStore()

  useEffect(() => {
    fetchConnections()
  }, [fetchConnections])

  // Chips for trigger keywords
  const [keywordInput, setKeywordInput] = useState('')
  const [keywords, setKeywords] = useState<string[]>([])

  // Option 1: Reply Buttons
  const [button1, setButton1] = useState('')
  const [button1Id, setButton1Id] = useState('')
  const [button2, setButton2] = useState('')
  const [button2Id, setButton2Id] = useState('')
  const [button3, setButton3] = useState('')
  const [button3Id, setButton3Id] = useState('')

  // Option 2: CTA URL
  const [ctaButtonName, setCtaButtonName] = useState('')
  const [ctaButtonLink, setCtaButtonLink] = useState('')

  // Option 3: Files
  const [fileType, setFileType] = useState('Image')
  const [fileName, setFileName] = useState('')
  const [fileUrl, setFileUrl] = useState('')
  const fileInputRef = useRef<HTMLInputElement>(null)

  // Option 4: Personal Assistant
  const [assistantName, setAssistantName] = useState('select option')

  // Validation states
  const [errors, setErrors] = useState<Record<string, string>>({})

  // Load bot data if in Edit or View mode
  useEffect(() => {
    const fetchBot = async () => {
      if (id) {
        const botId = parseInt(id, 10)
        const bot = await messageBotService.getBotById(botId)
        if (bot) {
          setName(bot.name)
          setRelationType(bot.relationType)
          setReplyText(bot.replyText)
          setReplyType(bot.replyType)
          setHeader(bot.header || '')
          setFooter(bot.footer || '')
          setIsActive(bot.isActive)
          setOptionType(bot.optionType as TabType)
          setConnectionId(bot.connectionId ?? '')
          
          if (bot.triggerKeyword) {
            setKeywords(bot.triggerKeyword.split(',').map((k: string) => k.trim()).filter(Boolean))
          }

          // Load options
          setButton1(bot.button1 || '')
          setButton1Id(bot.button1Id || '')
          setButton2(bot.button2 || '')
          setButton2Id(bot.button2Id || '')
          setButton3(bot.button3 || '')
          setButton3Id(bot.button3Id || '')
          
          setCtaButtonName(bot.ctaButtonName || '')
          setCtaButtonLink(bot.ctaButtonLink || '')
          
          setFileType(bot.fileType || 'Image')
          setFileName(bot.fileName || '')
          setFileUrl(bot.fileUrl || '')
          
          setAssistantName(bot.assistantName || 'select option')
        } else {
          toast.error('Failed to load message bot.')
          navigate('/message-bot')
        }
      }
    }

    fetchBot()

    // Show clone success toast if redirected from a clone operation
    if (sessionStorage.getItem('bot_clone_success') === 'true') {
      toast.success('Bot clone successfully')
      sessionStorage.removeItem('bot_clone_success')
    }
  }, [id])

  const handleAddKeyword = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' && keywordInput.trim()) {
      e.preventDefault()
      const newKeyword = keywordInput.trim().toLowerCase()
      if (!keywords.includes(newKeyword)) {
        setKeywords([...keywords, newKeyword])
      }
      setKeywordInput('')
    }
  }

  const handleRemoveKeyword = (indexToRemove: number) => {
    if (isViewMode) return
    setKeywords(keywords.filter((_, idx) => idx !== indexToRemove))
  }

  const handleFileUploadClick = () => {
    if (isViewMode) return
    fileInputRef.current?.click()
  }

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) {
      setFileName(file.name)
      // Simulate file upload setting temporary URL
      setFileUrl(URL.createObjectURL(file))
    }
  }

  const validateForm = (): boolean => {
    const newErrors: Record<string, string> = {}

    if (!name.trim()) newErrors.name = 'Bot Name is required.'
    if (!replyText.trim()) newErrors.replyText = 'Reply Text is required.'
    if (replyText.length > 1024) newErrors.replyText = 'Reply Text cannot exceed 1024 characters.'
    if (keywords.length === 0) newErrors.keywords = 'At least one Trigger Keyword is required.'

    if (optionType === 'ReplyButtons') {
      if (button1 && !button1Id) newErrors.button1Id = 'Button 1 ID is required when Button 1 Name is defined.'
      if (button2 && !button2Id) newErrors.button2Id = 'Button 2 ID is required when Button 2 Name is defined.'
      if (button3 && !button3Id) newErrors.button3Id = 'Button 3 ID is required when Button 3 Name is defined.'
    }

    if (optionType === 'CtaUrl') {
      if (ctaButtonName && !ctaButtonLink) newErrors.ctaButtonLink = 'Button Link is required.'
      if (ctaButtonLink && !ctaButtonLink.startsWith('http://') && !ctaButtonLink.startsWith('https://')) {
        newErrors.ctaButtonLink = 'Button Link must start with http:// or https://'
      }
    }

    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (isViewMode) return

    if (!validateForm()) {
      toast.error('Please correct form validation errors.')
      return
    }

    const payload = {
      name,
      relationType,
      replyText,
      replyType,
      triggerKeyword: keywords.join(','),
      header: header || undefined,
      footer: footer || undefined,
      isActive,
      optionType,
      connectionId: connectionId === '' ? undefined : connectionId,

      button1: optionType === 'ReplyButtons' ? button1 || undefined : undefined,
      button1Id: optionType === 'ReplyButtons' ? button1Id || undefined : undefined,
      button2: optionType === 'ReplyButtons' ? button2 || undefined : undefined,
      button2Id: optionType === 'ReplyButtons' ? button2Id || undefined : undefined,
      button3: optionType === 'ReplyButtons' ? button3 || undefined : undefined,
      button3Id: optionType === 'ReplyButtons' ? button3Id || undefined : undefined,
      
      ctaButtonName: optionType === 'CtaUrl' ? ctaButtonName || undefined : undefined,
      ctaButtonLink: optionType === 'CtaUrl' ? ctaButtonLink || undefined : undefined,
      
      fileType: optionType === 'Files' ? fileType : undefined,
      fileName: optionType === 'Files' ? fileName || undefined : undefined,
      fileUrl: optionType === 'Files' ? fileUrl || undefined : undefined,
      
      assistantName: optionType === 'PersonalAssistant' ? assistantName : undefined
    }

    try {
      if (isEditMode && id) {
        await updateBot(parseInt(id, 10), payload)
        toast.success('Message Bot updated successfully!')
      } else {
        await createBot(payload)
        toast.success('Message Bot created successfully!')
      }
      navigate('/message-bot')
    } catch (error: any) {
      toast.error(getErrorMessage(error, 'An error occurred while saving.'))
    }
  }

  return (
    <motion.div {...pageTransitionProps}>
      <div className="wizard-header">
        <button className="btn-back" onClick={() => navigate('/message-bot')} aria-label="Go Back">
          <ArrowLeft size={18} />
        </button>
        <h2>{isEditMode ? 'Edit Message bot' : isViewMode ? 'View Message bot' : 'Create Message bot'}</h2>
      </div>

      <form onSubmit={handleSubmit} className="bot-wizard-container">
        {/* Left Column: Core Fields */}
        <div className="bot-wizard-card core-card">
          <div className="card-header">
            <h3>Message Bot</h3>
          </div>
          <div className="card-body">
            <div className="form-group">
              <label className="form-label required">Bot Name</label>
              <input
                type="text"
                className={`form-control ${errors.name ? 'is-invalid' : ''}`}
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={isViewMode}
                placeholder="Enter bot name"
              />
              {errors.name && <span className="invalid-feedback">{errors.name}</span>}
            </div>

            <div className="form-group">
              <label className="form-label required">Relation Type</label>
              <select
                className="form-control"
                value={relationType}
                onChange={(e) => setRelationType(e.target.value)}
                disabled={isViewMode}
              >
                <option value="Lead">Lead</option>
                <option value="Customer">Customer</option>
              </select>
            </div>

            <div className="form-group">
              <label className="form-label">Connection</label>
              <select
                className="form-control"
                value={connectionId}
                onChange={(e) => setConnectionId(e.target.value ? Number(e.target.value) : '')}
                disabled={isViewMode}
              >
                <option value="">All Connections</option>
                {connections.map((conn) => (
                  <option key={conn.id} value={conn.id}>{conn.name}</option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <div className="label-with-counter">
                <label className="form-label required">Reply text (Maximum allowed characters should be 1024)</label>
                <span className="char-counter">{replyText.length}/1024</span>
              </div>
              <textarea
                className={`form-control textarea-reply ${errors.replyText ? 'is-invalid' : ''}`}
                value={replyText}
                onChange={(e) => setReplyText(e.target.value)}
                maxLength={1024}
                disabled={isViewMode}
                rows={5}
                placeholder="Enter reply text"
              />
              {errors.replyText && <span className="invalid-feedback">{errors.replyText}</span>}
            </div>

            <div className="form-group">
              <label className="form-label required">Reply Type</label>
              <select
                className="form-control"
                value={replyType}
                onChange={(e) => setReplyType(e.target.value)}
                disabled={isViewMode}
              >
                <option value="On Exact Match">On Exact Match</option>
                <option value="Contains Keyword">Contains Keyword</option>
              </select>
            </div>

            <div className="form-group">
              <label className="form-label required">Trigger Keyword</label>
              <input
                type="text"
                className={`form-control ${errors.keywords ? 'is-invalid' : ''}`}
                value={keywordInput}
                onChange={(e) => setKeywordInput(e.target.value)}
                onKeyDown={handleAddKeyword}
                disabled={isViewMode}
                placeholder="Type and press Enter.."
              />
              {errors.keywords && <span className="invalid-feedback">{errors.keywords}</span>}

              {/* Tags Display Container */}
              {keywords.length > 0 && (
                <div className="keyword-tags-container">
                  {keywords.map((kw, idx) => (
                    <span key={idx} className="keyword-tag">
                      {kw}
                      {!isViewMode && (
                        <button type="button" onClick={() => handleRemoveKeyword(idx)} className="btn-tag-remove">
                          <X size={12} />
                        </button>
                      )}
                    </span>
                  ))}
                </div>
              )}
            </div>

            <div className="form-group">
              <div className="label-with-counter">
                <label className="form-label">Header</label>
                <span className="char-counter">{header.length}/60</span>
              </div>
              <input
                type="text"
                className="form-control"
                value={header}
                onChange={(e) => setHeader(e.target.value)}
                maxLength={60}
                disabled={isViewMode}
                placeholder="Enter optional header text"
              />
            </div>

            <div className="form-group">
              <div className="label-with-counter">
                <label className="form-label">Footer</label>
                <span className="char-counter">{footer.length}/60</span>
              </div>
              <input
                type="text"
                className="form-control"
                value={footer}
                onChange={(e) => setFooter(e.target.value)}
                maxLength={60}
                disabled={isViewMode}
                placeholder="Enter optional footer text"
              />
            </div>
          </div>
        </div>

        {/* Right Column: Option Details Tabs */}
        <div className="bot-wizard-card options-card">
          <div className="card-header">
            <h3>Options</h3>
          </div>
          <div className="card-body">
            {/* Tabs Row */}
            <div className="tabs-header-row">
              <button
                type="button"
                className={`tab-btn ${optionType === 'ReplyButtons' ? 'active' : ''}`}
                onClick={() => !isViewMode && setOptionType('ReplyButtons')}
              >
                Reply Buttons
              </button>
              <button
                type="button"
                className={`tab-btn ${optionType === 'CtaUrl' ? 'active' : ''}`}
                onClick={() => !isViewMode && setOptionType('CtaUrl')}
              >
                CTA URL
              </button>
              <button
                type="button"
                className={`tab-btn ${optionType === 'Files' ? 'active' : ''}`}
                onClick={() => !isViewMode && setOptionType('Files')}
              >
                Files
              </button>
              <button
                type="button"
                className={`tab-btn ${optionType === 'PersonalAssistant' ? 'active' : ''}`}
                onClick={() => !isViewMode && setOptionType('PersonalAssistant')}
              >
                personal_assitant
              </button>
            </div>

            {/* Tab Contents */}
            <div className="tab-content-container">
              {optionType === 'ReplyButtons' && (
                <div className="tab-pane fade-in">
                  <p className="tab-pane-info">Option 1: Bot with reply Buttons</p>
                  
                  <div className="option-fields-grid">
                    <div className="form-group">
                      <div className="label-with-counter">
                        <label className="form-label">Button1</label>
                        <span className="char-counter">{button1.length}/20</span>
                      </div>
                      <input
                        type="text"
                        className="form-control"
                        value={button1}
                        onChange={(e) => setButton1(e.target.value)}
                        maxLength={20}
                        disabled={isViewMode}
                        placeholder="Button Name"
                      />
                    </div>
                    <div className="form-group">
                      <div className="label-with-counter">
                        <label className="form-label">Button1 ID</label>
                        <span className="char-counter">{button1Id.length}/256</span>
                      </div>
                      <input
                        type="text"
                        className={`form-control ${errors.button1Id ? 'is-invalid' : ''}`}
                        value={button1Id}
                        onChange={(e) => setButton1Id(e.target.value)}
                        maxLength={256}
                        disabled={isViewMode}
                        placeholder="unique_button_id_1"
                      />
                      {errors.button1Id && <span className="invalid-feedback">{errors.button1Id}</span>}
                    </div>

                    <div className="form-group">
                      <div className="label-with-counter">
                        <label className="form-label">Button2</label>
                        <span className="char-counter">{button2.length}/20</span>
                      </div>
                      <input
                        type="text"
                        className="form-control"
                        value={button2}
                        onChange={(e) => setButton2(e.target.value)}
                        maxLength={20}
                        disabled={isViewMode}
                        placeholder="Button Name"
                      />
                    </div>
                    <div className="form-group">
                      <div className="label-with-counter">
                        <label className="form-label">Button2 ID</label>
                        <span className="char-counter">{button2Id.length}/256</span>
                      </div>
                      <input
                        type="text"
                        className={`form-control ${errors.button2Id ? 'is-invalid' : ''}`}
                        value={button2Id}
                        onChange={(e) => setButton2Id(e.target.value)}
                        maxLength={256}
                        disabled={isViewMode}
                        placeholder="unique_button_id_2"
                      />
                      {errors.button2Id && <span className="invalid-feedback">{errors.button2Id}</span>}
                    </div>

                    <div className="form-group">
                      <div className="label-with-counter">
                        <label className="form-label">Button3</label>
                        <span className="char-counter">{button3.length}/20</span>
                      </div>
                      <input
                        type="text"
                        className="form-control"
                        value={button3}
                        onChange={(e) => setButton3(e.target.value)}
                        maxLength={20}
                        disabled={isViewMode}
                        placeholder="Button Name"
                      />
                    </div>
                    <div className="form-group">
                      <div className="label-with-counter">
                        <label className="form-label">Button3 ID</label>
                        <span className="char-counter">{button3Id.length}/256</span>
                      </div>
                      <input
                        type="text"
                        className={`form-control ${errors.button3Id ? 'is-invalid' : ''}`}
                        value={button3Id}
                        onChange={(e) => setButton3Id(e.target.value)}
                        maxLength={256}
                        disabled={isViewMode}
                        placeholder="unique_button_id_3"
                      />
                      {errors.button3Id && <span className="invalid-feedback">{errors.button3Id}</span>}
                    </div>
                  </div>
                </div>
              )}

              {optionType === 'CtaUrl' && (
                <div className="tab-pane fade-in">
                  <p className="tab-pane-info">Option 2: Bot with button link - Call to Action (CTA) URL</p>

                  <div className="form-group">
                    <div className="label-with-counter">
                      <label className="form-label">Button Name</label>
                      <span className="char-counter">{ctaButtonName.length}/20</span>
                    </div>
                    <input
                      type="text"
                      className="form-control"
                      value={ctaButtonName}
                      onChange={(e) => setCtaButtonName(e.target.value)}
                      maxLength={20}
                      disabled={isViewMode}
                      placeholder="Button Name"
                    />
                  </div>

                  <div className="form-group">
                    <label className="form-label">Button Link</label>
                    <input
                      type="text"
                      className={`form-control ${errors.ctaButtonLink ? 'is-invalid' : ''}`}
                      value={ctaButtonLink}
                      onChange={(e) => setCtaButtonLink(e.target.value)}
                      disabled={isViewMode}
                      placeholder="https://"
                    />
                    {errors.ctaButtonLink && <span className="invalid-feedback">{errors.ctaButtonLink}</span>}
                  </div>
                </div>
              )}

              {optionType === 'Files' && (
                <div className="tab-pane fade-in">
                  <div className="form-group">
                    <label className="form-label">Choose File Type</label>
                    <select
                      className="form-control"
                      value={fileType}
                      onChange={(e) => {
                        setFileType(e.target.value)
                        setFileName('')
                        setFileUrl('')
                      }}
                      disabled={isViewMode}
                    >
                      <option value="Image">Image</option>
                      <option value="Video">Video</option>
                      <option value="Document">Document</option>
                      <option value="Audio">Audio</option>
                    </select>
                  </div>

                  <div className="file-upload-section">
                    <span className="file-info-label">
                      Allowed: {fileType === 'Image' ? '.jpeg, .png' : fileType === 'Video' ? '.mp4' : fileType === 'Audio' ? '.mp3' : '.pdf, .docx, .xlsx'}
                    </span>
                    
                    <input
                      type="file"
                      ref={fileInputRef}
                      className="hidden-file-input"
                      onChange={handleFileChange}
                      accept={fileType === 'Image' ? 'image/jpeg,image/png' : fileType === 'Video' ? 'video/mp4' : fileType === 'Audio' ? 'audio/mpeg,audio/mp3' : '.pdf,.docx,.xlsx'}
                      disabled={isViewMode}
                    />

                    <div className="drag-upload-box" onClick={handleFileUploadClick}>
                      <Upload size={32} className="upload-box-icon" />
                      <span className="upload-box-text">
                        {fileName ? fileName : `Select or browse to ${fileType.toLowerCase()}`}
                      </span>
                    </div>
                  </div>
                </div>
              )}

              {optionType === 'PersonalAssistant' && (
                <div className="tab-pane fade-in">
                  <div className="form-group">
                    <label className="form-label">Personal Assistant</label>
                    <select
                      className="form-control"
                      value={assistantName}
                      onChange={(e) => setAssistantName(e.target.value)}
                      disabled={isViewMode}
                    >
                      <option value="select option">select option</option>
                      <option value="OmniBot">OmniBot</option>
                    </select>
                  </div>
                </div>
              )}
            </div>

            {/* Bottom Actions Row inside Options Card */}
            {!isViewMode && (
              <div className="wizard-actions-row">
                <button type="submit" className="btn btn-primary">
                  {isEditMode ? 'Update' : 'Add'}
                </button>
              </div>
            )}
          </div>
        </div>
      </form>
    </motion.div>
  )
}

export default MessageBotWizard
