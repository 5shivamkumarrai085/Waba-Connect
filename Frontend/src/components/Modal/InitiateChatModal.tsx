import React, { useEffect, useState, useMemo } from 'react'
import { X, MessageSquare } from 'lucide-react'
import { templateService } from '../../services/templates/templateService'
import { chatService } from '../../services/chat/chatService'
import type { Template } from '../../types/templates'
import toast from 'react-hot-toast'
import './InitiateChatModal.css'

interface InitiateChatModalProps {
  isOpen: boolean
  onClose: () => void
  contact: { id: number; name: string; phone: string } | null
  onSuccess?: () => void
}

export const InitiateChatModal: React.FC<InitiateChatModalProps> = ({
  isOpen,
  onClose,
  contact,
  onSuccess
}) => {
  const [templates, setTemplates] = useState<Template[]>([])
  const [selectedTemplateId, setSelectedTemplateId] = useState<string>('')
  const [variables, setVariables] = useState<Record<string, string>>({})
  const [isLoadingTemplates, setIsLoadingTemplates] = useState(false)
  const [isSending, setIsSending] = useState(false)

  // Fetch approved templates when modal opens
  useEffect(() => {
    if (!isOpen) return

    const loadTemplates = async () => {
      setIsLoadingTemplates(true)
      try {
        const allTemplates = await templateService.getTemplates()
        // Filter for APPROVED templates
        const approved = allTemplates.filter((t) => t.status?.toUpperCase() === 'APPROVED')
        setTemplates(approved)
      } catch (err) {
        toast.error('Failed to load templates.')
      } finally {
        setIsLoadingTemplates(false)
      }
    }

    void loadTemplates()
    setSelectedTemplateId('')
    setVariables({})
  }, [isOpen])

  const selectedTemplate = useMemo(() => {
    return templates.find((t) => t.id.toString() === selectedTemplateId) || null
  }, [templates, selectedTemplateId])

  // Get variable keys dynamically from template variables or regex parsing bodyText
  const variableKeys = useMemo(() => {
    if (!selectedTemplate) return []

    // If variables array exists on template object, use it
    if (selectedTemplate.variables && selectedTemplate.variables.length > 0) {
      return selectedTemplate.variables.map((v) => v.position.toString())
    }

    // Fallback: parse {{number}} using regex from template bodyText
    const matches = selectedTemplate.bodyText.match(/\{\{\d+\}\}/g) || []
    const uniqueNums = Array.from(
      new Set(matches.map((m) => m.replace('{{', '').replace('}}', '')))
    )
    return uniqueNums.sort((a, b) => Number(a) - Number(b))
  }, [selectedTemplate])

  // Compute preview text dynamically
  const previewText = useMemo(() => {
    if (!selectedTemplate) return ''
    let text = selectedTemplate.bodyText

    variableKeys.forEach((key) => {
      const value = variables[key] || `{{${key}}}`
      text = text.replace(new RegExp(`\\{\\{${key}\\}\\}`, 'g'), value)
    })

    return text
  }, [selectedTemplate, variableKeys, variables])

  if (!isOpen || !contact) return null

  const handleTemplateChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const val = e.target.value
    setSelectedTemplateId(val)
    setVariables({})
  }

  const handleVariableChange = (key: string, value: string) => {
    setVariables((prev) => ({
      ...prev,
      [key]: value
    }))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedTemplateId) {
      toast.error('Please select a template.')
      return
    }

    // Ensure all variables have values
    const missing = variableKeys.filter((key) => !variables[key]?.trim())
    if (missing.length > 0) {
      toast.error(`Please fill in all variables: ${missing.map((m) => `{{${m}}}`).join(', ')}`)
      return
    }

    setIsSending(true)
    try {
      await chatService.sendTemplateMessage(
        contact.id,
        Number(selectedTemplateId),
        variables
      )
      toast.success('Message sent successfully')
      if (onSuccess) onSuccess()
      onClose()
    } catch (err: any) {
      toast.error(err.message || 'Failed to send template message.')
    } finally {
      setIsSending(false)
    }
  }

  return (
    <div className="modal-overlay">
      <div className={`initiate-chat-container fade-in-up ${selectedTemplate ? 'expanded' : ''}`}>
        <div className="initiate-chat-header">
          <div className="initiate-chat-title-row">
            <MessageSquare size={18} className="initiate-chat-header-icon" />
            <h3>Initiate Chat</h3>
          </div>
          <button className="initiate-chat-close-btn" type="button" onClick={onClose} aria-label="Close modal">
            <X size={20} />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="initiate-chat-form">
          <div className="form-group select-template-group">
            <label className="initiate-chat-label">
              <span className="required-star">*</span> Template
            </label>
            <select
              className="form-control"
              value={selectedTemplateId}
              onChange={handleTemplateChange}
              disabled={isLoadingTemplates}
            >
              <option value="">Nothing Selected</option>
              {templates.map((tpl) => (
                <option key={tpl.id} value={tpl.id}>
                  {tpl.name} ({tpl.language})
                </option>
              ))}
            </select>
            {isLoadingTemplates && <span className="loader-text">Loading approved templates...</span>}
            {!isLoadingTemplates && templates.length === 0 && (
              <span className="error-text">No approved templates found in database.</span>
            )}
          </div>

          {selectedTemplate && (
            <div className="initiate-chat-split-view">
              {/* Left Column: Variables */}
              <div className="initiate-chat-variables-panel">
                <h4>Variables</h4>
                {variableKeys.length === 0 ? (
                  <p className="no-variables-text">This template has no dynamic variable placeholders.</p>
                ) : (
                  <div className="variables-list-scroll">
                    {variableKeys.map((key) => (
                      <div key={key} className="form-group variable-group">
                        <label>
                          Variable {'{{'}{key}{'}}'}
                        </label>
                        <input
                          type="text"
                          className="form-control"
                          value={variables[key] || ''}
                          onChange={(e) => handleVariableChange(key, e.target.value)}
                          placeholder={`Enter value for {{${key}}}`}
                          required
                        />
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {/* Right Column: Preview */}
              <div className="initiate-chat-preview-panel">
                <h4>Preview</h4>
                <div className="whatsapp-preview-pattern">
                  <div className="whatsapp-bubble-wrapper">
                    <div className="whatsapp-bubble">
                      <p className="whatsapp-bubble-body">{previewText}</p>
                      <span className="whatsapp-bubble-time">
                        {new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                      </span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}

          <div className="initiate-chat-actions">
            <button type="button" className="btn btn-secondary" onClick={onClose} disabled={isSending}>
              Close
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isSending || !selectedTemplateId}
            >
              {isSending ? 'Sending...' : 'Submit'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
