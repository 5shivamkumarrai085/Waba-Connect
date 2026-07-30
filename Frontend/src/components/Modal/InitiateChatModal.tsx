import React, { useEffect, useState, useMemo } from 'react'
import { createPortal } from 'react-dom'
import { X, MessageSquare, Check } from 'lucide-react'
import { templateService } from '../../services/templates/templateService'
import { chatService } from '../../services/chat/chatService'
import { useConnectionStore } from '../../store/connectionStore'
import type { Template } from '../../types/templates'
import toast from 'react-hot-toast'
import './InitiateChatModal.css'

interface InitiateChatModalProps {
  isOpen: boolean
  onClose: () => void
  contact?: { id: number; name: string; phone: string } | null
  contacts?: Array<{ id: number; name: string; phone: string }>
  connectionId?: number | null
  onSuccess?: () => void
}

export const InitiateChatModal: React.FC<InitiateChatModalProps> = ({
  isOpen,
  onClose,
  contact,
  contacts,
  connectionId,
  onSuccess
}) => {
  const [templates, setTemplates] = useState<Template[]>([])
  const [selectedTemplateId, setSelectedTemplateId] = useState<string>('')
  const [variables, setVariables] = useState<Record<string, string>>({})
  const [isLoadingTemplates, setIsLoadingTemplates] = useState(false)
  const [isSending, setIsSending] = useState(false)
  const [selectedConnectionIds, setSelectedConnectionIds] = useState<number[]>([])

  const { connections, fetchDashboard } = useConnectionStore()
  const connectedConnections = useMemo(
    () => connections.filter((c) => c.isConnected && c.phoneNumber),
    [connections]
  )

  const targetContacts = useMemo(() => {
    if (contacts && contacts.length > 0) return contacts
    if (contact) return [contact]
    return []
  }, [contact, contacts])

  // Fetch connections & templates when modal opens
  useEffect(() => {
    if (!isOpen) return

    fetchDashboard()

    const loadTemplates = async () => {
      setIsLoadingTemplates(true)
      try {
        const allTemplates = await templateService.getTemplates()
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
  }, [isOpen, fetchDashboard])

  // Initialize selected connection(s) when connection list becomes available
  useEffect(() => {
    if (!isOpen) return

    if (connectionId) {
      setSelectedConnectionIds([connectionId])
    } else if (connectedConnections.length > 0) {
      setSelectedConnectionIds((prev) => (prev.length === 0 ? [connectedConnections[0].id] : prev))
    } else {
      setSelectedConnectionIds([])
    }
  }, [isOpen, connectionId, connectedConnections])

  const selectedTemplate = useMemo(() => {
    return templates.find((t) => t.id.toString() === selectedTemplateId) || null
  }, [templates, selectedTemplateId])

  const variableKeys = useMemo(() => {
    if (!selectedTemplate) return []
    if (selectedTemplate.variables && selectedTemplate.variables.length > 0) {
      return selectedTemplate.variables.map((v) => v.position.toString())
    }
    const matches = selectedTemplate.bodyText.match(/\{\{\d+\}\}/g) || []
    const uniqueNums = Array.from(
      new Set(matches.map((m) => m.replace('{{', '').replace('}}', '')))
    )
    return uniqueNums.sort((a, b) => Number(a) - Number(b))
  }, [selectedTemplate])

  const previewText = useMemo(() => {
    if (!selectedTemplate) return ''
    let text = selectedTemplate.bodyText
    variableKeys.forEach((key) => {
      const value = variables[key] || `{{${key}}}`
      text = text.replace(new RegExp(`\\{\\{${key}\\}\\}`, 'g'), value)
    })
    return text
  }, [selectedTemplate, variableKeys, variables])

  if (!isOpen || targetContacts.length === 0) return null

  const handleTemplateChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setSelectedTemplateId(e.target.value)
    setVariables({})
  }

  const handleVariableChange = (key: string, value: string) => {
    setVariables((prev) => ({ ...prev, [key]: value }))
  }

  const toggleConnection = (id: number) => {
    setSelectedConnectionIds((prev) =>
      prev.includes(id) ? prev.filter((cid) => cid !== id) : [...prev, id]
    )
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedTemplateId) {
      toast.error('Please select a template.')
      return
    }

    if (selectedConnectionIds.length === 0) {
      toast.error('Please select at least one connection.')
      return
    }

    const missing = variableKeys.filter((key) => !variables[key]?.trim())
    if (missing.length > 0) {
      toast.error(`Please fill in all variables: ${missing.map((m) => `{{${m}}}`).join(', ')}`)
      return
    }

    setIsSending(true)
    try {
      let succeededCount = 0
      let failedCount = 0

      for (const targetContact of targetContacts) {
        for (const connId of selectedConnectionIds) {
          try {
            await chatService.sendTemplateMessage(
              targetContact.id,
              Number(selectedTemplateId),
              variables,
              connId
            )
            succeededCount++
          } catch (e) {
            failedCount++
          }
        }
      }

      if (failedCount === 0) {
        toast.success(`Template sent to ${targetContacts.length} active contact${targetContacts.length > 1 ? 's' : ''} successfully!`)
      } else if (succeededCount > 0) {
        toast.success(`Sent to ${succeededCount} contact message(s), ${failedCount} failed.`)
      } else {
        toast.error('Failed to send template message.')
      }

      if (succeededCount > 0) {
        if (onSuccess) onSuccess()
        onClose()
      }
    } catch (err: any) {
      toast.error(err.message || 'Failed to send template message.')
    } finally {
      setIsSending(false)
    }
  }

  const showConnectionPicker = !connectionId && connectedConnections.length > 0

  return createPortal(
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
          {/* Connection Picker — shown when opened from Contact page */}
          {showConnectionPicker && (
            <div className="form-group connection-picker-group">
              <label className="initiate-chat-label">
                <span className="required-star">*</span> Send From Connection(s)
              </label>
              <div className="connection-chips-container">
                {connectedConnections.map((conn) => {
                  const isSelected = selectedConnectionIds.includes(conn.id)
                  return (
                    <button
                      key={conn.id}
                      type="button"
                      className={`connection-chip ${isSelected ? 'selected' : ''}`}
                      onClick={() => toggleConnection(conn.id)}
                    >
                      {isSelected && <Check size={14} />}
                      <span>{conn.name}</span>
                      <span className="chip-phone">{conn.phoneNumber}</span>
                    </button>
                  )
                })}
              </div>
              {selectedConnectionIds.length === 0 && (
                <span className="error-text">Select at least one connection</span>
              )}
            </div>
          )}

          {/* If opened from Chat with connectionId, show which connection */}
          {connectionId && (
            <div className="form-group">
              <label className="initiate-chat-label">Sending From</label>
              <div className="sending-from-badge">
                {connectedConnections.find((c) => c.id === connectionId)?.name || 'Selected Connection'}
                {' — '}
                {connectedConnections.find((c) => c.id === connectionId)?.phoneNumber || ''}
              </div>
            </div>
          )}

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
              disabled={isSending || !selectedTemplateId || selectedConnectionIds.length === 0}
            >
              {isSending ? 'Sending...' : selectedConnectionIds.length > 1 ? `Send to ${selectedConnectionIds.length} Connections` : 'Submit'}
            </button>
          </div>
        </form>
      </div>
    </div>,
    document.body
  )
}
