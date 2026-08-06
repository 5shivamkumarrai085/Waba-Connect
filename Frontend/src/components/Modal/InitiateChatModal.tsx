import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AnimatePresence, motion, useReducedMotion } from 'framer-motion'
import { MessageSquare, Check, Info } from 'lucide-react'
import { templateService } from '../../services/templates/templateService'
import { chatService } from '../../services/chat/chatService'
import { wabaService } from '../../services/waba/wabaService'
import { useConnectionStore } from '../../store/connectionStore'
import { Avatar } from '../Avatar/Avatar'
import { Stepper } from '../Stepper/Stepper'
import { Modal } from './Modal'
import type { Template } from '../../types/templates'
import toast from 'react-hot-toast'
import { staggerContainer, staggerChild, stepSlide, transitions } from '../../utils/motion'
import './InitiateChatModal.css'

interface InitiateChatModalProps {
  isOpen: boolean
  onClose: () => void
  contact?: { id: number; name: string; phone: string } | null
  contacts?: Array<{ id: number; name: string; phone: string }>
  connectionId?: number | null
  onSuccess?: () => void
}

type WizardStep = 1 | 2

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

  // Opened from Chat with a connection already chosen: there is nothing left
  // to decide on step 1, so skip it entirely rather than showing a picker with
  // one disabled-feeling option.
  const hasPresetConnection = Boolean(connectionId)
  const [step, setStep] = useState<WizardStep>(hasPresetConnection ? 2 : 1)
  const [direction, setDirection] = useState<1 | -1>(1)

  // "Latest request wins" guard for the two independent template-fetch paths
  // below (the mount-time effect and toggleConnection) — they can race against
  // each other (and against themselves, if the user clicks two connections in
  // quick succession) with no ordering guarantee on which HTTP response lands
  // last. Every fetch captures its own generation number and only commits its
  // result if no newer fetch has started since.
  const templateRequestGenerationRef = useRef(0)

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

  // Fetch connections & templates when modal opens.
  // Dependency array is deliberately [isOpen, connectionId] only — it encodes
  // the original fetch timing and must not be "corrected" to include
  // connectedConnections or step.
  useEffect(() => {
    if (!isOpen) return

    fetchDashboard()

    const activeConnId = connectionId || selectedConnectionIds[0] || (connectedConnections.length > 0 ? connectedConnections[0].id : undefined)

    const myGeneration = ++templateRequestGenerationRef.current

    const loadTemplates = async () => {
      setIsLoadingTemplates(true)
      try {
        let tpls: any[] = []
        if (activeConnId) {
          tpls = await templateService.getTemplatesByConnection(activeConnId)
        } else {
          tpls = await templateService.getTemplates()
        }
        if (templateRequestGenerationRef.current !== myGeneration) return
        setTemplates(tpls)
      } catch (err) {
        if (templateRequestGenerationRef.current !== myGeneration) return
        toast.error('Failed to load templates.')
      } finally {
        if (templateRequestGenerationRef.current === myGeneration) {
          setIsLoadingTemplates(false)
        }
      }
    }

    void loadTemplates()
    setSelectedTemplateId('')
    setVariables({})
    setStep(connectionId ? 2 : 1)
    setDirection(1)
  }, [isOpen, connectionId])

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

  const handleTemplateChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setSelectedTemplateId(e.target.value)
    setVariables({})
  }

  const handleVariableChange = (key: string, value: string) => {
    setVariables((prev) => ({ ...prev, [key]: value }))
  }

  const toggleConnection = async (id: number) => {
    const nextIds = selectedConnectionIds.includes(id) ? [] : [id]
    setSelectedConnectionIds(nextIds)
    setSelectedTemplateId('')
    setVariables({})

    const myGeneration = ++templateRequestGenerationRef.current

    if (nextIds.length > 0) {
      setIsLoadingTemplates(true)
      try {
        const connTpls = await templateService.getTemplatesByConnection(nextIds[0])
        if (templateRequestGenerationRef.current !== myGeneration) return
        setTemplates(connTpls)
      } catch {
        // Fallback
      } finally {
        if (templateRequestGenerationRef.current === myGeneration) {
          setIsLoadingTemplates(false)
        }
      }
    } else {
      if (templateRequestGenerationRef.current === myGeneration) {
        setTemplates([])
      }
    }
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
      const checkResult = await wabaService.checkLimitFast(selectedConnectionIds[0])
      if (checkResult.limitReached) {
        toast.error(checkResult.message || 'Daily message limit reached for this connection.', { duration: 4000 })
        return
      }

      let succeededCount = 0
      let failedCount = 0
      let lastErrorMessage = ''

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
          } catch (e: any) {
            failedCount++
            lastErrorMessage = e?.message || 'Failed to send template message.'
          }
        }
      }

      if (failedCount === 0) {
        toast.success(`Template sent to ${targetContacts.length} active contact${targetContacts.length > 1 ? 's' : ''} successfully!`)
      } else if (succeededCount > 0) {
        toast.success(`Sent to ${succeededCount} contact message(s), ${failedCount} failed: ${lastErrorMessage}`)
      } else {
        toast.error(lastErrorMessage || 'Failed to send template message.')
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

  const canGoNext = selectedConnectionIds.length > 0

  const goNext = () => {
    if (!canGoNext) return
    setDirection(1)
    setStep(2)
  }

  const goBack = () => {
    setDirection(-1)
    // Clear the template selection — otherwise `selectedTemplate` stays truthy,
    // `.expanded` (max-width: 850px) stays applied, and step 1 renders inside
    // the wide container instead of reverting to its normal width. Deliberately
    // does NOT touch selectedConnectionIds — the chosen connection must survive
    // the trip back to step 1.
    setSelectedTemplateId('')
    setVariables({})
    setStep(1)
  }

  // Both steps live in one <form> (the Send button submits it via the `form`
  // attribute from the footer, which sits outside this element). On step 1
  // Enter should advance rather than attempt to submit.
  const handleFormKeyDown = (e: React.KeyboardEvent<HTMLFormElement>) => {
    if (e.key === 'Enter' && step === 1) {
      e.preventDefault()
      goNext()
    }
  }

  // Height between steps of different sizes, driven by a ResizeObserver on
  // whichever step is currently mounted. Seeded to 'auto' so the very first
  // paint isn't animated from 0. A callback ref (rather than useEffect keyed
  // on `step`) is used because AnimatePresence mode="wait" unmounts the old
  // step before mounting the new one — a plain effect would still see the
  // outgoing node mid-exit at the moment `step` changes.
  //
  // overflow is permanently hidden on the viewport (in CSS, not toggled here)
  // — toggling it based on animation state used to create a feedback loop:
  // hiding overflow could change whether a scrollbar was needed, which changed
  // the observed element's box, which re-fired the observer, which restarted
  // the height animation, which toggled overflow again. The equality guard
  // below closes the other half of that loop: a resize notification that
  // doesn't actually change the (rounded) height is a no-op, so remeasuring
  // after a completed animation can't itself trigger another one.
  const [stepHeight, setStepHeight] = useState<number | 'auto'>('auto')
  const heightObserverRef = useRef<ResizeObserver | null>(null)

  const setStepBodyRef = useCallback((el: HTMLDivElement | null) => {
    heightObserverRef.current?.disconnect()
    heightObserverRef.current = null
    if (!el) return
    const observer = new ResizeObserver((entries) => {
      const next = Math.round(entries[0].contentRect.height)
      setStepHeight((prev) => (prev !== 'auto' && Math.round(prev) === next ? prev : next))
    })
    observer.observe(el)
    heightObserverRef.current = observer
  }, [])

  useEffect(() => () => heightObserverRef.current?.disconnect(), [])

  const reduceMotion = useReducedMotion()

  const showConnectionPicker = !hasPresetConnection && connectedConnections.length > 0
  const showNoConnectionsNotice = !hasPresetConnection && connectedConnections.length === 0

  return (
    <Modal
      isOpen={isOpen && targetContacts.length > 0}
      onClose={onClose}
      size="custom"
      icon={<MessageSquare size={18} />}
      title="Initiate Chat"
      subtitle={
        hasPresetConnection ? (
          'Choose a template to start the conversation.'
        ) : (
          <Stepper steps={['Select Connection', 'Select Template']} activeStep={step - 1} />
        )
      }
      className={`initiate-chat-container${selectedTemplate ? ' expanded' : ''}`}
      footer={
        step === 1 ? (
          <>
            <button type="button" className="oc-dialog-btn oc-dialog-btn-secondary" onClick={onClose}>
              Cancel
            </button>
            <button
              type="button"
              className="oc-dialog-btn oc-dialog-btn-primary"
              onClick={goNext}
              disabled={!canGoNext}
            >
              Next
            </button>
          </>
        ) : (
          <>
            <button
              type="button"
              className="oc-dialog-btn oc-dialog-btn-secondary"
              onClick={onClose}
              disabled={isSending}
            >
              Cancel
            </button>
            {!hasPresetConnection && (
              <button
                type="button"
                className="oc-dialog-btn oc-dialog-btn-secondary"
                onClick={goBack}
                disabled={isSending}
              >
                Previous
              </button>
            )}
            <button
              type="submit"
              form="initiate-chat-form"
              className="oc-dialog-btn oc-dialog-btn-primary"
              disabled={isSending || !selectedTemplateId || selectedConnectionIds.length === 0}
            >
              {isSending
                ? 'Sending...'
                : selectedConnectionIds.length > 1
                  ? `Send to ${selectedConnectionIds.length} Connections`
                  : 'Send'}
            </button>
          </>
        )
      }
    >
      <form id="initiate-chat-form" onSubmit={handleSubmit} onKeyDown={handleFormKeyDown}>
        <motion.div
          className="initiate-chat-steps-viewport"
          animate={{ height: reduceMotion ? 'auto' : stepHeight }}
          transition={reduceMotion ? { duration: 0 } : transitions.normal}
        >
          <AnimatePresence mode="wait" custom={direction} initial={false}>
            <motion.div
              key={step}
              ref={setStepBodyRef}
              custom={direction}
              variants={reduceMotion ? undefined : stepSlide}
              initial={reduceMotion ? { opacity: 0 } : 'hidden'}
              animate={reduceMotion ? { opacity: 1, transition: { duration: 0.1 } } : 'visible'}
              exit={reduceMotion ? { opacity: 0, transition: { duration: 0.1 } } : 'exit'}
              className="initiate-chat-step-slide"
            >
              {step === 1 ? (
                <>
                  {showConnectionPicker && (
                    <div className="form-group connection-picker-group">
                      <label className="initiate-chat-label">
                        Send From Connection <span className="required-star">*</span>
                      </label>
                      <motion.div
                        className="connection-rows-container"
                        variants={staggerContainer(0.05)}
                        initial="hidden"
                        animate="visible"
                      >
                        {connectedConnections.map((conn) => {
                          const isSelected = selectedConnectionIds.includes(conn.id)
                          return (
                            <motion.button
                              key={conn.id}
                              type="button"
                              variants={staggerChild}
                              className={`connection-row ${isSelected ? 'selected' : ''}`}
                              onClick={() => toggleConnection(conn.id)}
                            >
                              <span className={`connection-row-radio ${isSelected ? 'checked' : ''}`}>
                                {isSelected && <Check size={12} strokeWidth={3} />}
                              </span>
                              <Avatar name={conn.name} size="small" />
                              <span className="connection-row-name">{conn.name}</span>
                              <span className="connection-row-phone">{conn.phoneNumber}</span>
                            </motion.button>
                          )
                        })}
                      </motion.div>
                      {selectedConnectionIds.length === 0 && (
                        <span className="error-text">Select at least one connection</span>
                      )}
                      {selectedConnectionIds.length > 0 && (
                        <div className="initiate-chat-info-banner">
                          <Info size={14} />
                          <span>Messages will be sent using the selected WhatsApp Business connection.</span>
                        </div>
                      )}
                    </div>
                  )}

                  {showNoConnectionsNotice && (
                    <div className="initiate-chat-empty-connections">
                      <Info size={16} />
                      <span>No connected WhatsApp Business accounts. Connect one to send a template message.</span>
                    </div>
                  )}
                </>
              ) : (
                <>
                  {hasPresetConnection && (
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
                      Select Template <span className="required-star">*</span>
                    </label>
                    <select
                      className="form-control"
                      value={selectedTemplateId}
                      onChange={handleTemplateChange}
                      disabled={isLoadingTemplates}
                    >
                      <option value="">Choose a message template</option>
                      {templates.map((tpl) => (
                        <option key={tpl.id} value={tpl.id}>
                          {tpl.name} ({tpl.language})
                        </option>
                      ))}
                    </select>
                    <p className="initiate-chat-hint">Select a template that matches the purpose of your message.</p>
                    {isLoadingTemplates && <span className="loader-text">Loading approved templates...</span>}
                    {!isLoadingTemplates && templates.length === 0 && (
                      <span className="error-text">No approved templates found for this connection.</span>
                    )}
                  </div>

                  <AnimatePresence>
                    {selectedTemplate && (
                      <motion.div
                        className="initiate-chat-split-view"
                        initial={{ opacity: 0, height: 0 }}
                        animate={{ opacity: 1, height: 'auto' }}
                        exit={{ opacity: 0, height: 0 }}
                        transition={transitions.normal}
                      >
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
                      </motion.div>
                    )}
                  </AnimatePresence>
                </>
              )}
            </motion.div>
          </AnimatePresence>
        </motion.div>
      </form>
    </Modal>
  )
}
