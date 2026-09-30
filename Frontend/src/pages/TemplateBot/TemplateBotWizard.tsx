import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, X, UploadCloud, FileText, Trash2, Loader2 } from 'lucide-react'
import { templateBotService } from '../../services/templateBot/templateBotService'
import { templateService } from '../../services/templates/templateService'
import { campaignService } from '../../services/campaigns/campaignService'
import { useConnectionStore } from '../../store/connectionStore'
import { toast } from 'react-hot-toast'
import type { Template } from '../../types/templates'
import type { TemplateBotVariable } from '../../types/templateBot'
import './TemplateBotWizard.css'
import { getErrorMessage } from '../../utils/errorHelper'

export const TemplateBotWizard: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const [searchParams] = useSearchParams()
  const isViewMode = searchParams.get('view') === 'true'
  const isEditMode = !!id && !isViewMode

  // Form states
  const [name, setName] = useState('')
  const [relationType, setRelationType] = useState('Lead')
  const [templateId, setTemplateId] = useState<number | ''>('')
  const [replyType, setReplyType] = useState('On Exact Match')
  const [isActive, setIsActive] = useState(true)
  const [connectionId, setConnectionId] = useState<number | ''>('')
  const { connections, fetchConnections } = useConnectionStore()

  useEffect(() => {
    fetchConnections()
  }, [fetchConnections])

  // Keyword tags state
  const [keywordInput, setKeywordInput] = useState('')
  const [keywords, setKeywords] = useState<string[]>([])

  // Variable values mapping state: position -> { value, mergeField, type: 'static' | 'merge' }
  const [variableMappings, setVariableMappings] = useState<Record<number, {
    value: string
    mergeField: string
    type: 'static' | 'merge'
  }>>({})

  // File / PDF Attachment states
  const [fileUrl, setFileUrl] = useState('')
  const [fileName, setFileName] = useState('')
  const [uploadingFile, setUploadingFile] = useState(false)

  // Database template models
  const [templates, setTemplates] = useState<Template[]>([])
  const [selectedTemplate, setSelectedTemplate] = useState<Template | null>(null)
  
  // Validation messages
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [isSaving, setIsSaving] = useState(false)
  const [warnings, setWarnings] = useState<string[]>([])

  useEffect(() => {
    const checkCollisions = async () => {
      if (keywords.length === 0) {
        setWarnings([])
        return
      }
      try {
        const ignoreId = id ? parseInt(id, 10) : 0
        const activeWarnings = await templateBotService.checkKeywords(keywords.join(','), ignoreId, 0)
        setWarnings(activeWarnings)
      } catch (err) {
        console.error('Failed to validate keywords:', err)
      }
    }
    checkCollisions()
  }, [keywords, id])

  // Fetch templates list on mount
  useEffect(() => {
    const fetchTemplates = async () => {
      try {
        const list = await templateService.getTemplates()
        setTemplates(list)
      } catch (error) {
        toast.error('Failed to load templates.')
      }
    }
    fetchTemplates()
  }, [])

  // Load bot data if editing or viewing
  useEffect(() => {
    const fetchBotData = async () => {
      if (!id) return
      try {
        const botId = parseInt(id, 10)
        const bot = await templateBotService.getTemplateBotById(botId)
        if (bot) {
          setName(bot.name)
          setRelationType(bot.relationType)
          setTemplateId(bot.templateId)
          setReplyType(bot.replyType)
          setIsActive(bot.isActive)
          setConnectionId(bot.connectionId ?? '')

          if (bot.triggerKeyword) {
            setKeywords(bot.triggerKeyword.split(',').map((k: string) => k.trim()).filter(Boolean))
          }

          // Restore variable mappings and file attachments
          const mappings: Record<number, { value: string; mergeField: string; type: 'static' | 'merge' }> = {}
          if (bot.variables) {
            bot.variables.forEach((v) => {
              if (v.variableName === 'file') {
                if (v.variableValue) {
                  setFileUrl(v.variableValue)
                  const fName = v.mergeField || v.variableValue.substring(v.variableValue.lastIndexOf('/') + 1)
                  setFileName(fName)
                }
              } else {
                const pos = parseInt(v.variableName, 10)
                if (!isNaN(pos)) {
                  mappings[pos] = {
                    value: v.variableValue || '',
                    mergeField: v.mergeField || '',
                    type: v.mergeField ? 'merge' : 'static'
                  }
                }
              }
            })
          }
          setVariableMappings(mappings)
        }
      } catch (error) {
        toast.error('Failed to load template bot data.')
      }
    }
    fetchBotData()
  }, [id])

  // Track template changed to fetch/update variable slots
  useEffect(() => {
    if (templateId) {
      const tpl = templates.find((t) => t.id === Number(templateId))
      if (tpl) {
        setSelectedTemplate(tpl)
        
        // Parse variables in bodyText (e.g. {{1}}, {{2}})
        const matches = tpl.bodyText.match(/\{\{\d+\}\}/g)
        const parsedPositions = matches 
          ? Array.from(new Set(matches.map(m => parseInt(m.replace(/[\{\}]/g, ''), 10))))
          : []
        
        // Initialize mapping for any new variables found
        setVariableMappings((prev) => {
          const updated = { ...prev }
          parsedPositions.forEach((pos) => {
            if (!updated[pos]) {
              updated[pos] = { value: '', mergeField: 'Name', type: 'static' }
            }
          })
          return updated
        })
      }
    } else {
      setSelectedTemplate(null)
      setVariableMappings({})
    }
  }, [templateId, templates])

  const handleKeywordAdd = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' && keywordInput.trim()) {
      e.preventDefault()
      const newKeyword = keywordInput.trim().toLowerCase()
      if (!keywords.includes(newKeyword)) {
        setKeywords([...keywords, newKeyword])
      }
      setKeywordInput('')
    }
  }

  const handleKeywordRemove = (index: number) => {
    if (isViewMode) return
    setKeywords(keywords.filter((_, idx) => idx !== index))
  }

  const handleVariableChange = (pos: number, field: 'value' | 'mergeField' | 'type', value: string) => {
    setVariableMappings((prev) => ({
      ...prev,
      [pos]: {
        ...prev[pos],
        [field]: value
      }
    }))
  }

  const validate = () => {
    const newErrors: Record<string, string> = {}
    if (!name.trim()) newErrors.name = 'Bot Name is required'
    if (!relationType) newErrors.relationType = 'Relation Type is required'
    if (!templateId) newErrors.templateId = 'Template selection is required'
    if (!replyType) newErrors.replyType = 'Reply Type is required'
    if (keywords.length === 0) newErrors.triggerKeyword = 'At least one Trigger Keyword is required'
    
    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (isViewMode) return
    if (!validate()) return

    // Build variables list to save
    const variablesPayload: TemplateBotVariable[] = Object.entries(variableMappings).map(([posStr, map]) => {
      return {
        variableName: posStr,
        variableValue: map.type === 'static' ? map.value : undefined,
        mergeField: map.type === 'merge' ? map.mergeField : undefined
      }
    })

    if (fileUrl) {
      variablesPayload.push({
        variableName: 'file',
        variableValue: fileUrl,
        mergeField: fileName || 'file'
      })
    }

    const payload = {
      name: name.trim(),
      relationType,
      templateId: Number(templateId),
      replyType,
      triggerKeyword: keywords.join(', '),
      isActive,
      variables: variablesPayload,
      connectionId: connectionId === '' ? undefined : connectionId
    }

    setIsSaving(true)
    try {
      if (isEditMode && id) {
        await templateBotService.updateTemplateBot(parseInt(id, 10), payload)
        toast.success('Template Bot updated successfully!')
      } else {
        await templateBotService.createTemplateBot(payload)
        toast.success('Template Bot created successfully!')
      }
      navigate('/template-bot')
    } catch (error: any) {
      toast.error(getErrorMessage(error, 'Failed to save Template Bot.'))
    } finally {
      setIsSaving(false)
    }
  }

  // Helper to compile text preview with variable replacements
  const getPreviewText = () => {
    if (!selectedTemplate) return ''
    let preview = selectedTemplate.bodyText || ''
    
    Object.entries(variableMappings).forEach(([posStr, map]) => {
      const placeholder = `{{${posStr}}}`
      const replacement = map.type === 'static' 
        ? (map.value || `{{${posStr}}}`)
        : `[${map.mergeField}]`
      
      // Replace all occurrences of the placeholder
      preview = preview.replaceAll(placeholder, replacement)
    })
    return preview
  }

  const templateVariablesList = selectedTemplate 
    ? Array.from(new Set((selectedTemplate.bodyText.match(/\{\{\d+\}\}/g) || []).map(m => parseInt(m.replace(/[\{\}]/g, ''), 10)))).sort((a, b) => a - b)
    : []

  return (
    <motion.div {...pageTransitionProps}>
      {/* Title Bar */}
      <div className="wizard-title-bar omni-page-hero form-page-hero">
        <button className="back-arrow-btn" onClick={() => navigate('/template-bot')} aria-label="Go Back">
          <ArrowLeft size={20} />
        </button>
        <h2 className="wizard-title">
          {isViewMode ? 'View Template Bot' : isEditMode ? 'Edit Template Bot' : 'Create Template Bot'}
        </h2>
      </div>

      <form className="wizard-split-layout" style={!selectedTemplate ? { gridTemplateColumns: '1fr' } : undefined} onSubmit={handleSubmit}>
        {/* Panel 1: Template Bot Config */}
        <div className="wizard-panel config-panel" style={!selectedTemplate ? { maxWidth: '520px', margin: '0 auto', width: '100%' } : undefined}>
          <div className="panel-header">Template Bot</div>
          <div className="panel-body">
            
            {/* Bot Name */}
            <div className="form-group">
              <label className="required-label">Bot Name</label>
              <input
                type="text"
                disabled={isViewMode}
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Enter bot name..."
              />
              {errors.name && <span className="error-text">{errors.name}</span>}
            </div>

            {/* Relation Type */}
            <div className="form-group">
              <label className="required-label">Relation Type</label>
              <select
                disabled={isViewMode}
                value={relationType}
                onChange={(e) => setRelationType(e.target.value)}
              >
                <option value="Lead">Lead</option>
                <option value="Customer">Customer</option>
              </select>
              {errors.relationType && <span className="error-text">{errors.relationType}</span>}
            </div>

            {/* Connection scope */}
            <div className="form-group">
              <label>Connection</label>
              <select
                disabled={isViewMode}
                value={connectionId}
                onChange={(e) => setConnectionId(e.target.value ? Number(e.target.value) : '')}
              >
                <option value="">All Connections</option>
                {connections.map((conn) => (
                  <option key={conn.id} value={conn.id}>{conn.name}</option>
                ))}
              </select>
            </div>

            {/* Template select */}
            <div className="form-group">
              <label className="required-label">Template</label>
              <select
                disabled={isViewMode}
                value={templateId}
                onChange={(e) => setTemplateId(e.target.value ? Number(e.target.value) : '')}
              >
                <option value="">Nothing Selected</option>
                {templates.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name} ({t.language})
                  </option>
                ))}
              </select>
              {errors.templateId && <span className="error-text">{errors.templateId}</span>}
            </div>

            {/* Reply Type */}
            <div className="form-group">
              <label className="required-label">Reply Type</label>
              <select
                disabled={isViewMode}
                value={replyType}
                onChange={(e) => setReplyType(e.target.value)}
              >
                <option value="On Exact Match">On Exact Match</option>
                <option value="When Message Contains">When Message Contains</option>
              </select>
              {errors.replyType && <span className="error-text">{errors.replyType}</span>}
            </div>

            {/* Trigger Keywords tags */}
            <div className="form-group">
              <label className="required-label">Trigger Keyword</label>
              {!isViewMode && (
                <input
                  type="text"
                  value={keywordInput}
                  onChange={(e) => setKeywordInput(e.target.value)}
                  onKeyDown={handleKeywordAdd}
                  placeholder="Type and press Enter..."
                />
              )}
              {errors.triggerKeyword && <span className="error-text">{errors.triggerKeyword}</span>}
              
              <div className="keyword-tags-container">
                {keywords.map((kw, idx) => (
                  <span key={idx} className="keyword-tag">
                    {kw}
                    {!isViewMode && (
                      <button type="button" onClick={() => handleKeywordRemove(idx)}>
                        <X size={12} />
                      </button>
                    )}
                  </span>
                ))}
              </div>
              {warnings.length > 0 && (
                <div style={{ marginTop: '8px', padding: '8px', backgroundColor: '#fffbeb', border: '1px solid #fef3c7', borderRadius: '6px' }}>
                  {warnings.map((warn, i) => (
                    <div key={i} style={{ display: 'flex', alignItems: 'center', gap: '6px', color: '#b45309', fontSize: '11.5px', fontWeight: 500, margin: '2px 0' }}>
                      <span>⚠️</span>
                      <span>{warn}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>

          </div>
        </div>

        {selectedTemplate && (
          <>
            {/* Panel 2: Variables Configuration */}
            <div className="wizard-panel variables-panel">
              <div className="panel-header">Variables & Attachments</div>
              <div className="panel-body">
                {templateVariablesList.length === 0 ? (
                  <div className="variables-alert-danger" style={{ marginBottom: '16px' }}>
                    Currently, no text variable is available for this template body.
                  </div>
                ) : (
                  <div className="variables-inputs-list">
                    {templateVariablesList.map((pos) => {
                      const mapping = variableMappings[pos] || { value: '', mergeField: 'Name', type: 'static' }
                      return (
                        <div key={pos} className="variable-row-card">
                          <div className="variable-row-header">
                            Variable {"{{"}{pos}{"}}"}
                          </div>
                          
                          <div className="variable-row-type-select">
                            <label>
                              <input
                                type="radio"
                                disabled={isViewMode}
                                name={`var-type-${pos}`}
                                checked={mapping.type === 'static'}
                                onChange={() => handleVariableChange(pos, 'type', 'static')}
                              />
                              Static Value
                            </label>
                            <label>
                              <input
                                type="radio"
                                disabled={isViewMode}
                                name={`var-type-${pos}`}
                                checked={mapping.type === 'merge'}
                                onChange={() => handleVariableChange(pos, 'type', 'merge')}
                              />
                              Contact Field
                            </label>
                          </div>

                          {mapping.type === 'static' ? (
                            <div className="form-group">
                              <input
                                type="text"
                                disabled={isViewMode}
                                value={mapping.value}
                                onChange={(e) => handleVariableChange(pos, 'value', e.target.value)}
                                placeholder="Enter static text value..."
                              />
                            </div>
                          ) : (
                            <div className="form-group">
                              <select
                                disabled={isViewMode}
                                value={mapping.mergeField}
                                onChange={(e) => handleVariableChange(pos, 'mergeField', e.target.value)}
                              >
                                <option value="Name">Name</option>
                                <option value="PhoneNumber">Phone Number</option>
                                <option value="Email">Email</option>
                                <option value="Company">Company</option>
                              </select>
                            </div>
                          )}
                        </div>
                      )
                    })}
                  </div>
                )}

                {/* PDF / File Attachment Section */}
                <div className="variable-row-card media-upload-card" style={{ marginTop: '16px' }}>
                  <div className="variable-row-header" style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <UploadCloud size={16} /> Attach Document / Media File (PDF, Image, File)
                  </div>
                  <p className="upload-sub-text" style={{ fontSize: '12px', color: '#6b7280', margin: '4px 0 12px 0' }}>
                    Attach a PDF or document to be sent automatically to the customer along with this template bot response.
                  </p>

                  {fileUrl ? (
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 14px', backgroundColor: '#f3f4f6', borderRadius: '8px', border: '1px solid #e5e7eb' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '8px', overflow: 'hidden' }}>
                        <FileText size={18} style={{ color: '#4f46e5', flexShrink: 0 }} />
                        <span style={{ fontSize: '13px', fontWeight: 500, color: '#374151', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                          {fileName || fileUrl.split('/').pop()}
                        </span>
                      </div>
                      {!isViewMode && (
                        <button
                          type="button"
                          onClick={() => { setFileUrl(''); setFileName('') }}
                          style={{ background: 'none', border: 'none', color: '#dc2626', cursor: 'pointer', padding: '4px' }}
                          title="Remove file"
                        >
                          <Trash2 size={16} />
                        </button>
                      )}
                    </div>
                  ) : (
                    !isViewMode && (
                      <div>
                        <input
                          type="file"
                          id="template-bot-file-input"
                          style={{ display: 'none' }}
                          onChange={async (e) => {
                            const file = e.target.files?.[0]
                            if (file) {
                              setUploadingFile(true)
                              try {
                                const res = await campaignService.uploadFile(file)
                                setFileUrl(res.url)
                                setFileName(file.name)
                                toast.success('File attached successfully!')
                              } catch (err) {
                                toast.error('Failed to upload file attachment.')
                              } finally {
                                setUploadingFile(false)
                              }
                            }
                          }}
                        />
                        <label
                          htmlFor="template-bot-file-input"
                          style={{
                            display: 'flex',
                            flexDirection: 'column',
                            alignItems: 'center',
                            justifyContent: 'center',
                            padding: '16px',
                            border: '2px dashed #d1d5db',
                            borderRadius: '8px',
                            cursor: 'pointer',
                            backgroundColor: '#fafafa',
                            transition: 'border-color 0.2s'
                          }}
                        >
                          {uploadingFile ? (
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#4f46e5' }}>
                              <Loader2 className="animate-spin" size={18} />
                              <span>Uploading attachment...</span>
                            </div>
                          ) : (
                            <>
                              <UploadCloud size={24} style={{ color: '#6b7280', marginBottom: '4px' }} />
                              <span style={{ fontSize: '13px', fontWeight: 500, color: '#374151' }}>Click to upload PDF or document</span>
                              <span style={{ fontSize: '11px', color: '#9ca3af' }}>PDF, DOCX, PNG, JPG (max 25MB)</span>
                            </>
                          )}
                        </label>
                      </div>
                    )
                  )}
                </div>
              </div>
            </div>

        {/* Panel 3: Live Preview */}
        <div className="wizard-panel preview-panel">
          <div className="panel-header">Preview</div>
          <div className="panel-body preview-body-layout">
            
            {/* Mock Whatsapp screen */}
            <div className="mock-chat-screen">
              <div className="whatsapp-bubble">
                {selectedTemplate.headerContent && (
                  <div className="bubble-header">{selectedTemplate.headerContent}</div>
                )}
                <div className="bubble-body">{getPreviewText()}</div>
                {selectedTemplate.footerText && (
                  <div className="bubble-footer">{selectedTemplate.footerText}</div>
                )}
                <div className="bubble-timestamp">
                  {new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </div>
              </div>
            </div>

            {/* Action Save/Update button */}
            {!isViewMode && (
              <div className="preview-action-footer">
                <button type="submit" className="template-bot-btn btn-primary btn-submit" disabled={isSaving}>
                  {isSaving ? 'Saving...' : (isEditMode ? 'Update' : 'Add')}
                </button>
              </div>
            )}

          </div>
        </div>
      </>
    )}
  </form>
    </motion.div>
  )
}
