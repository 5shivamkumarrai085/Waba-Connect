import React, { useEffect, useState, useRef } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { 
  ArrowLeft, 
  Save, 
  Trash2, 
  Copy, 
  ChevronDown, 
  ChevronUp, 
  AlertCircle, 
  Info, 
  UploadCloud, 
  Search, 
  MapPin, 
  Zap, 
  MessageSquare, 
  MousePointerClick, 
  ExternalLink, 
  List, 
  Image as ImageIcon, 
  User, 
  Bot,
  FileText
} from 'lucide-react'
import { 
  ReactFlow, 
  Controls, 
  Background, 
  MiniMap, 
  Handle, 
  Position, 
  ReactFlowProvider,
  useReactFlow,
  BaseEdge,
  EdgeLabelRenderer,
  getBezierPath,
  type EdgeProps
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { botFlowService } from '../../services/botFlow/botFlowService'
import { useBotFlowStore } from './botFlowStore'
import { toast } from 'react-hot-toast'
import './BotFlowDesigner.css'

// ==========================================
// VALIDATION & HELPER UTILITIES
// ==========================================

const isValidUrl = (url: string) => {
  if (!url) return false
  const clean = url.trim()
  try {
    const parsed = new URL(clean)
    return parsed.protocol === 'http:' || parsed.protocol === 'https:'
  } catch (_) {
    return false
  }
}

const isValidPhone = (phone: string) => {
  if (!phone) return false
  const clean = phone.trim()
  if (clean.startsWith('{{') && clean.endsWith('}}')) return true
  return /^\+[1-9]\d{6,14}$/.test(clean)
}

const isValidEmail = (email: string) => {
  if (!email) return true // optional
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())
}

// Validation Panel UI
const ValidationPanel = ({ errors }: { errors: string[] }) => {
  if (!errors || errors.length === 0) return null
  return (
    <div className="node-validation-panel">
      <div className="validation-header">
        <AlertCircle size={16} className="validation-error-icon" />
        <span className="validation-title">
          {errors.length === 1 ? errors[0] : 'Please fix the following issues:'}
        </span>
      </div>
      {errors.length > 1 && (
        <ul className="validation-error-list">
          {errors.map((err, idx) => (
            <li key={idx}>{err}</li>
          ))}
        </ul>
      )}
    </div>
  )
}

// Common Header Component for All Nodes
const NodeHeader = ({
  id,
  title,
  icon,
  isCollapsed,
  hasErrors,
  errorText,
  headerClass,
  showDelete = true,
  customSubtitle
}: {
  id: string
  title: string
  icon: React.ReactNode
  isCollapsed: boolean
  hasErrors: boolean
  errorText?: string
  headerClass: string
  showDelete?: boolean
  customSubtitle?: React.ReactNode
}) => {
  const duplicateNode = useBotFlowStore(state => state.duplicateNode)
  const deleteNode = useBotFlowStore(state => state.deleteNode)
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)

  return (
    <div className={`node-header-bar ${headerClass}`}>
      <div className="node-header-title-wrapper">
        <div className="node-header-icon-container">
          {icon}
        </div>
        <div className="node-header-title-text">
          <span className="node-title">{title}</span>
          {customSubtitle ? (
            customSubtitle
          ) : (
            hasErrors && (
              <span className="node-status-subtitle">
                {errorText || 'Required fields missing'}
              </span>
            )
          )}
        </div>
      </div>
      <div className="node-header-actions">
        <button 
          type="button" 
          className="node-header-action-btn"
          onClick={() => updateNodeData(id, { isCollapsed: !isCollapsed })}
          title={isCollapsed ? "Expand" : "Collapse"}
        >
          {isCollapsed ? <ChevronDown size={14} /> : <ChevronUp size={14} />}
        </button>
        <button 
          type="button" 
          className="node-header-action-btn"
          onClick={() => duplicateNode(id)}
          title="Copy Node"
        >
          <Copy size={14} />
        </button>
        {showDelete && (
          <button 
            type="button" 
            className="node-header-action-btn"
            onClick={() => deleteNode(id)}
            title="Delete Node"
          >
            <Trash2 size={14} />
          </button>
        )}
      </div>
    </div>
  )
}

// ==========================================
// 1. START TRIGGER NODE
// ==========================================
const StartTriggerNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const isCollapsed = data.isCollapsed || false
  const [keywordInput, setKeywordInput] = useState('')
  const suggestions = ['hi', 'start', 'help', 'info', 'menu', 'order', 'contact', 'hello', 'support']

  const addKeyword = (word: string) => {
    const cleanWord = word.trim().toLowerCase()
    if (!cleanWord) return
    const currentList = data.keywords || []
    if (!currentList.includes(cleanWord)) {
      updateNodeData(id, { keywords: [...currentList, cleanWord] })
    }
  }

  const removeKeyword = (wordToRemove: string) => {
    const currentList = data.keywords || []
    updateNodeData(id, { keywords: currentList.filter((k: string) => k !== wordToRemove) })
  }

  const errors: string[] = []
  if (!data.keywords || data.keywords.length === 0) {
    errors.push('At least one trigger keyword is required')
  }

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '300px' }}>
      <NodeHeader
        id={id}
        title="Start Trigger"
        icon={<Zap size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Required fields missing"
        headerClass="start-trigger"
        showDelete={false}
        customSubtitle={<span style={{ fontSize: '9px', fontWeight: 500, color: '#f5d0fe', marginTop: '1px' }}>• Entry Point</span>}
      />
      
      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        <ValidationPanel errors={errors} />

        <div className="node-input-fields">
          <label>Contact Type *</label>
          <select 
            value={data.contactType || 'Lead'} 
            onChange={(e) => updateNodeData(id, { contactType: e.target.value })}
          >
            <option value="Lead">Lead</option>
            <option value="Customer">Customer</option>
            <option value="Vendor">Vendor</option>
          </select>
        </div>

        <div className="node-input-fields">
          <label>Trigger Type *</label>
          <select 
            value={data.triggerType || 'on exact match'} 
            onChange={(e) => updateNodeData(id, { triggerType: e.target.value })}
          >
            <option value="on exact match">on exact match</option>
            <option value="contains">contains</option>
            <option value="starts with">starts with</option>
          </select>
        </div>

        <div className="node-input-fields">
          <label>Trigger Keywords *</label>
          <div className="designer-tag-input-group">
            <input 
              type="text" 
              placeholder="Add keyword..." 
              value={keywordInput}
              onChange={(e) => setKeywordInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  addKeyword(keywordInput)
                  setKeywordInput('')
                }
              }}
            />
            <button type="button" onClick={() => { addKeyword(keywordInput); setKeywordInput('') }}>+</button>
          </div>
          
          <div className="keyword-chips-list">
            {(data.keywords || []).map((k: string) => (
              <span key={k} className="keyword-chip-item">
                {k}
                <button type="button" onClick={() => removeKeyword(k)}>×</button>
              </span>
            ))}
          </div>
        </div>

        <div className="keyword-suggestions">
          <span className="sugg-title">Suggestions:</span>
          <div className="suggestions-list">
            {suggestions.map(s => (
              <span key={s} className="suggestion-chip" onClick={() => addKeyword(s)}>
                {s} +
              </span>
            ))}
          </div>
        </div>
      </div>
      
      <Handle type="source" position={Position.Right} id="output" className="canvas-node-port port-output" />
    </div>
  )
}

// ==========================================
// 2. TEXT MESSAGE NODE
// ==========================================
const TextMessageNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const isCollapsed = data.isCollapsed || false

  const textVal = data.messageText || ''
  const errors: string[] = []
  if (!textVal.trim()) {
    errors.push('Message text is required')
  }

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '300px' }}>
      <NodeHeader
        id={id}
        title="Text Message"
        icon={<MessageSquare size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Required field"
        headerClass="text-message"
      />

      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        <ValidationPanel errors={errors} />

        <div className="node-input-fields">
          <label className={errors.length > 0 ? 'error-label' : ''}>Message Text *</label>
          <textarea
            className={errors.length > 0 ? 'error-input' : ''}
            value={textVal}
            placeholder="Enter your message text here..."
            maxLength={1000}
            onChange={(e) => updateNodeData(id, { messageText: e.target.value })}
            rows={4}
          />
          <div className="character-counter">
            {textVal.length}/1000
          </div>
        </div>
      </div>

      <Handle type="target" position={Position.Left} id="input" className="canvas-node-port port-input" />
      <Handle type="source" position={Position.Right} id="output" className="canvas-node-port port-output" />
    </div>
  )
}

// ==========================================
// 3. BUTTON MESSAGE NODE
// ==========================================
const ButtonMessageNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const edges = useBotFlowStore(state => state.edges)
  const isCollapsed = data.isCollapsed || false
  const buttons = data.buttons || []
  const textVal = data.messageText || ''

  const addButton = () => {
    if (buttons.length >= 3) {
      toast.error('Maximum 3 buttons allowed.')
      return
    }
    const newBtn = { text: `Button ${buttons.length + 1}`, value: `val_${buttons.length + 1}` }
    updateNodeData(id, { buttons: [...buttons, newBtn] })
  }

  const removeButton = (idx: number) => {
    updateNodeData(id, { buttons: buttons.filter((_: any, i: number) => i !== idx) })
  }

  const updateButtonField = (idx: number, field: string, value: string) => {
    const updated = buttons.map((b: any, i: number) => {
      if (i === idx) {
        return { ...b, [field]: value }
      }
      return b
    })
    updateNodeData(id, { buttons: updated })
  }

  // Connection validation helper
  const isBtnConnected = (idx: number) => {
    return edges.some(edge => edge.source === id && edge.sourceHandle === `button-${idx}`)
  }

  // Calculate Errors
  const errors: string[] = []
  if (!textVal.trim()) {
    errors.push('Message text is required')
  }
  if (buttons.length === 0) {
    errors.push('At least one button is required')
  }
  const hasEmptyButtonFields = buttons.some((b: any) => !b.text.trim() || !b.value.trim())
  if (hasEmptyButtonFields) {
    errors.push('All buttons must have text and value')
  }
  const hasDisconnectedButtons = buttons.some((_: any, idx: number) => !isBtnConnected(idx))
  if (hasDisconnectedButtons) {
    errors.push('All buttons must be connected to another node')
  }

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '300px' }}>
      <NodeHeader
        id={id}
        title="Button Message"
        icon={<MousePointerClick size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Required fields missing"
        headerClass="button-message"
      />

      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        <div className="node-input-fields">
          <label className={!textVal.trim() ? 'error-label' : ''}>Message Text *</label>
          <textarea
            className={!textVal.trim() ? 'error-input' : ''}
            value={textVal}
            placeholder="Enter message text..."
            onChange={(e) => updateNodeData(id, { messageText: e.target.value })}
            maxLength={1024}
            rows={2}
          />
          <div className="character-counter">{textVal.length}/1024</div>
        </div>

        <div className="node-input-fields">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
            <label>Buttons ({buttons.length}/3)</label>
            {buttons.length < 3 && (
              <button 
                type="button" 
                className="add-field-link-btn"
                onClick={addButton}
              >
                + Add Button
              </button>
            )}
          </div>
          {buttons.map((b: any, idx: number) => {
            const connected = isBtnConnected(idx)
            return (
              <div key={idx} className="inner-item-card" style={{ gap: '8px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <span className="inner-item-label">Button {idx + 1}</span>
                  <button type="button" onClick={() => removeButton(idx)} className="inner-item-delete-btn">Remove</button>
                </div>
                
                <div className="node-input-fields" style={{ gap: '2px' }}>
                  <label className="sub-label" style={{ fontSize: '10px', textTransform: 'none', color: '#4b5563', fontWeight: 600 }}>Button Text *</label>
                  <input 
                    type="text" 
                    placeholder="Button text (max 20 chars)" 
                    maxLength={20}
                    value={b.text}
                    onChange={(e) => updateButtonField(idx, 'text', e.target.value)}
                    className={!b.text.trim() ? 'error-input' : ''}
                  />
                </div>

                <div className="node-input-fields" style={{ gap: '2px' }}>
                  <label className="sub-label" style={{ fontSize: '10px', textTransform: 'none', color: '#4b5563', fontWeight: 600 }}>Value *</label>
                  <input 
                    type="text" 
                    placeholder="Payload value" 
                    value={b.value}
                    onChange={(e) => updateButtonField(idx, 'value', e.target.value)}
                    className={!b.value.trim() ? 'error-input' : ''}
                  />
                </div>
                
                {/* Connection Status indicator */}
                <div className="connection-status-wrapper" style={{ marginTop: '2px', display: 'flex', alignItems: 'center', gap: '4px', justifyContent: 'flex-start' }}>
                  <span style={{ color: connected ? '#10b981' : '#ef4444', fontSize: '10px' }}>
                    Button {idx + 1} response {connected ? 'Connected' : 'Not connected'}
                  </span>
                  <span className={`status-dot ${connected ? 'connected' : 'disconnected'}`}></span>
                </div>

                <Handle 
                  type="source" 
                  position={Position.Right} 
                  id={`button-${idx}`} 
                  className={`canvas-node-port port-output`}
                  style={{ top: '50%', right: '-14px' }} 
                />
              </div>
            )
          })}
        </div>

        {/* Validation Panel rendered at bottom matching screenshots */}
        <ValidationPanel errors={errors} />
      </div>
      
      <Handle type="target" position={Position.Left} id="input" className="canvas-node-port port-input" />
    </div>
  )
}

// ==========================================
// 4. CALL TO ACTION NODE
// ==========================================
const CallToActionNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const isCollapsed = data.isCollapsed || false

  const headerVal = data.header || ''
  const valText = data.valueText || ''
  const btnText = data.buttonText || ''
  const btnLink = data.buttonLink || ''
  const footerVal = data.footer || ''

  const isLinkValid = isValidUrl(btnLink)

  // Calculations
  const errors: string[] = []
  if (!valText.trim()) {
    errors.push('Value text is required')
  }
  if (!btnText.trim()) {
    errors.push('Button text is required')
  }
  if (!btnLink.trim()) {
    errors.push('Button link is required')
  } else if (!isLinkValid) {
    errors.push('Valid button link is required')
  }

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '300px' }}>
      <NodeHeader
        id={id}
        title="Call To Action"
        icon={<ExternalLink size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Required fields missing"
        headerClass="call-to-action"
      />

      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        <ValidationPanel errors={errors} />

        <div className="node-input-fields">
          <label>Header (Optional)</label>
          <input 
            type="text" 
            placeholder="Enter header text (optional)" 
            value={headerVal} 
            onChange={(e) => updateNodeData(id, { header: e.target.value })} 
          />
        </div>

        <div className="node-input-fields">
          <label className={!valText.trim() ? 'error-label' : ''}>Value Text *</label>
          <textarea
            className={!valText.trim() ? 'error-input' : ''}
            value={valText}
            placeholder="Enter value text"
            onChange={(e) => updateNodeData(id, { valueText: e.target.value })}
            maxLength={1000}
            rows={2}
          />
          {!valText.trim() && <span className="input-error-msg">Value text is required</span>}
          <div className="character-counter">{valText.length}/1000</div>
        </div>

        <div className="node-input-fields">
          <label className={!btnText.trim() ? 'error-label' : ''}>Button Text *</label>
          <input 
            className={!btnText.trim() ? 'error-input' : ''}
            type="text" 
            placeholder="Button text..." 
            value={btnText} 
            onChange={(e) => updateNodeData(id, { buttonText: e.target.value })} 
          />
        </div>

        <div className="node-input-fields">
          <label className={!btnLink.trim() || !isLinkValid ? 'error-label' : ''}>Button Link URL *</label>
          <input 
            className={!btnLink.trim() || !isLinkValid ? 'error-input' : ''}
            type="text" 
            placeholder="Enter URL (https://example.com)" 
            value={btnLink} 
            onChange={(e) => updateNodeData(id, { buttonLink: e.target.value })} 
          />
          {btnLink.trim() && !isLinkValid && (
            <span className="input-error-msg">Please enter a valid URL</span>
          )}
        </div>

        <div className="node-input-fields">
          <label>Footer (Optional)</label>
          <input 
            type="text" 
            placeholder="Enter footer text (optional)" 
            value={footerVal} 
            onChange={(e) => updateNodeData(id, { footer: e.target.value })} 
          />
        </div>

        {/* WhatsApp Preview Card matches original image reference */}
        <div className="preview-container">
          <span className="preview-label">Preview:</span>
          <div className="whatsapp-preview-card">
            {headerVal.trim() && <div className="preview-header">{headerVal}</div>}
            <div className="preview-body">
              {valText.trim() ? valText : 'Your value text will appear here'}
            </div>
            {footerVal.trim() && <div className="preview-footer">{footerVal}</div>}
            <div className="preview-cta-button">
              {btnText.trim() ? btnText : 'Click Here'}
            </div>
          </div>
        </div>
      </div>

      <Handle type="target" position={Position.Left} id="input" className="canvas-node-port port-input" />
      <Handle type="source" position={Position.Right} id="output" className="canvas-node-port port-output" />
    </div>
  )
}

// ==========================================
// 5. LIST MESSAGE NODE
// ==========================================
const ListMessageNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const edges = useBotFlowStore(state => state.edges)
  const isCollapsed = data.isCollapsed || false
  const sections = data.sections || []
  const bodyText = data.bodyText || ''
  const btnText = data.buttonText || ''

  const addSection = () => {
    if (sections.length >= 10) {
      toast.error('Maximum 10 sections allowed.')
      return
    }
    const newSec = { title: `Section ${sections.length + 1}`, items: [] }
    updateNodeData(id, { sections: [...sections, newSec] })
  }

  const removeSection = (secIdx: number) => {
    updateNodeData(id, { sections: sections.filter((_: any, i: number) => i !== secIdx) })
  }

  const updateSectionTitle = (secIdx: number, title: string) => {
    const updated = sections.map((sec: any, i: number) => {
      if (i === secIdx) return { ...sec, title }
      return sec
    })
    updateNodeData(id, { sections: updated })
  }

  const addItemToSection = (secIdx: number) => {
    const totalItems = sections.reduce((acc: number, sec: any) => acc + (sec.items || []).length, 0)
    if (totalItems >= 10) {
      toast.error('Maximum 10 total items allowed across all sections.')
      return
    }
    const sec = sections[secIdx]
    if (sec && (sec.items || []).length >= 10) {
      toast.error('Maximum 10 items per section allowed.')
      return
    }

    const updated = sections.map((sec: any, i: number) => {
      if (i === secIdx) {
        const items = sec.items || []
        const newItem = { id: `item-${Date.now()}-${Math.random().toString(36).substr(2, 5)}`, title: `Item ${items.length + 1}`, description: '', value: `val_${items.length + 1}` }
        return { ...sec, items: [...items, newItem] }
      }
      return sec
    })
    updateNodeData(id, { sections: updated })
  }

  const removeItemFromSection = (secIdx: number, itemIdx: number) => {
    const updated = sections.map((sec: any, i: number) => {
      if (i === secIdx) {
        const items = sec.items || []
        return { ...sec, items: items.filter((_: any, idx: number) => idx !== itemIdx) }
      }
      return sec
    })
    updateNodeData(id, { sections: updated })
  }

  const updateItemField = (secIdx: number, itemIdx: number, field: string, value: string) => {
    const updated = sections.map((sec: any, i: number) => {
      if (i === secIdx) {
        const items = sec.items || []
        const updatedItems = items.map((item: any, idx: number) => {
          if (idx === itemIdx) {
            return { ...item, [field]: value }
          }
          return item
        })
        return { ...sec, items: updatedItems }
      }
      return sec
    })
    updateNodeData(id, { sections: updated })
  }

  // Check if item is connected
  const isItemConnected = (itemVal: string, itemId: string) => {
    const handleId = `item-${itemVal || itemId}`
    return edges.some(edge => edge.source === id && edge.sourceHandle === handleId)
  }

  // Calculate Errors
  const errors: string[] = []
  if (!bodyText.trim()) {
    errors.push('Body text is required')
  }
  if (!btnText.trim()) {
    errors.push('Button text is required')
  }
  if (sections.length === 0) {
    errors.push('At least one section is required')
  }
  const hasEmptySectionTitle = sections.some((s: any) => !s.title.trim())
  if (hasEmptySectionTitle) {
    errors.push('All section titles are required')
  }
  const itemsList = sections.flatMap((s: any) => s.items || [])
  const hasEmptyItemTitle = itemsList.some((it: any) => !it.title.trim())
  if (hasEmptyItemTitle) {
    errors.push('All item titles are required')
  }
  const hasDisconnectedItems = sections.some((sec: any) => 
    (sec.items || []).some((item: any) => !isItemConnected(item.value, item.id))
  )
  if (hasDisconnectedItems) {
    errors.push('All list items must be connected to another node')
  }

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '320px' }}>
      <NodeHeader
        id={id}
        title="List Message"
        icon={<List size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Required fields missing"
        headerClass="list-message"
      />

      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        {/* Validation Panel */}
        <ValidationPanel errors={errors} />

        <div className="node-input-fields">
          <label>Header Text (Optional)</label>
          <input 
            type="text" 
            placeholder="Header text..." 
            value={data.headerText || ''} 
            onChange={(e) => updateNodeData(id, { headerText: e.target.value })} 
          />
        </div>

        <div className="node-input-fields">
          <label className={!bodyText.trim() ? 'error-label' : ''}>Body Text *</label>
          <textarea
            className={!bodyText.trim() ? 'error-input' : ''}
            value={bodyText}
            placeholder="Message body..."
            onChange={(e) => updateNodeData(id, { bodyText: e.target.value })}
            maxLength={1024}
            rows={2}
          />
          <div className="character-counter">{bodyText.length}/1024</div>
        </div>

        <div className="node-input-fields">
          <label>Footer Text (Optional)</label>
          <input 
            type="text" 
            placeholder="Footer text..." 
            value={data.footerText || ''} 
            onChange={(e) => updateNodeData(id, { footerText: e.target.value })} 
          />
        </div>

        <div className="node-input-fields">
          <label className={!btnText.trim() ? 'error-label' : ''}>Button Text *</label>
          <input 
            className={!btnText.trim() ? 'error-input' : ''}
            type="text" 
            placeholder="e.g. View Menu" 
            value={btnText} 
            onChange={(e) => updateNodeData(id, { buttonText: e.target.value })} 
          />
        </div>

        {/* Section Management */}
        <div className="node-input-fields">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
            <label style={{ fontSize: '11px', fontWeight: 600, color: '#374151' }}>Sections & Items</label>
            <span style={{ fontSize: '10px', color: '#6b7280', fontWeight: 500 }}>
              {sections.reduce((acc: number, s: any) => acc + (s.items || []).length, 0)}/10 items
            </span>
          </div>

          {sections.map((sec: any, secIdx: number) => (
            <div key={secIdx} className="inner-item-card list-section-card" style={{ border: '1px solid #a7f3d0', backgroundColor: '#f0fdf4', padding: '12px', borderRadius: '8px', marginBottom: '10px' }}>
              
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', marginBottom: '8px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <label style={{ fontSize: '10px', fontWeight: 700, color: '#047857', textTransform: 'none' }}>Section Title * (Required)</label>
                  <button 
                    type="button" 
                    onClick={() => removeSection(secIdx)} 
                    style={{
                      border: 'none',
                      background: 'none',
                      color: '#ef4444',
                      fontSize: '14px',
                      fontWeight: 'bold',
                      cursor: 'pointer',
                      padding: '0 4px'
                    }}
                    title="Delete Section"
                  >
                    ×
                  </button>
                </div>
                <input 
                  type="text" 
                  value={sec.title} 
                  placeholder="Section title" 
                  onChange={(e) => updateSectionTitle(secIdx, e.target.value)}
                  className={!sec.title.trim() ? 'error-input' : ''}
                />
              </div>

              <div style={{ marginTop: '8px' }}>
                {(sec.items || []).map((item: any, itemIdx: number) => {
                  const connected = isItemConnected(item.value, item.id)
                  return (
                    <div key={item.id} className="inner-item-card list-item-subcard" style={{ gap: '8px', border: '1px solid #cbd5e1', padding: '10px', borderRadius: '6px', backgroundColor: '#ffffff', position: 'relative', marginTop: '8px' }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <span style={{ fontSize: '10px', fontWeight: 700, color: connected ? '#10b981' : '#ef4444' }}>
                          — Item {itemIdx + 1} * (Required) {connected ? '' : '(Not connected)'}
                        </span>
                        <button type="button" onClick={() => removeItemFromSection(secIdx, itemIdx)} className="inner-item-delete-btn">Remove</button>
                      </div>

                      <div className="node-input-fields" style={{ gap: '2px' }}>
                        <label className="sub-label" style={{ fontSize: '10px', textTransform: 'none', color: '#4b5563', fontWeight: 600 }}>Item Title *</label>
                        <input 
                          type="text" 
                          value={item.title} 
                          placeholder="Item title" 
                          onChange={(e) => updateItemField(secIdx, itemIdx, 'title', e.target.value)}
                          className={!item.title.trim() ? 'error-input' : ''}
                        />
                      </div>

                      <div className="node-input-fields" style={{ gap: '2px' }}>
                        <label className="sub-label" style={{ fontSize: '10px', textTransform: 'none', color: '#4b5563', fontWeight: 600 }}>Description (optional)</label>
                        <input 
                          type="text" 
                          value={item.description} 
                          placeholder="Item description (optional)" 
                          onChange={(e) => updateItemField(secIdx, itemIdx, 'description', e.target.value)}
                        />
                      </div>



                      {/* Connection status for list items */}
                      <div className="connection-status-wrapper" style={{ marginTop: '2px', display: 'flex', alignItems: 'center', gap: '4px', justifyContent: 'flex-start' }}>
                        <span style={{ color: connected ? '#10b981' : '#ef4444', fontSize: '10px' }}>
                          Item {itemIdx + 1} response {connected ? 'Connected' : 'Not connected'}
                        </span>
                        <span className={`status-dot ${connected ? 'connected' : 'disconnected'}`}></span>
                      </div>

                      <Handle 
                        type="source" 
                        position={Position.Right} 
                        id={`item-${item.value || item.id}`} 
                        className={`canvas-node-port port-output`}
                        style={{ top: '50%', right: '-14px' }} 
                      />
                    </div>
                  )
                })}

                {/* Full-width Add Item Button inside section card */}
                <button 
                  type="button" 
                  onClick={() => addItemToSection(secIdx)}
                  style={{
                    width: '100%',
                    padding: '8px',
                    borderRadius: '6px',
                    backgroundColor: '#ecfdf5',
                    color: '#047857',
                    border: '1px dashed #a7f3d0',
                    fontSize: '10px',
                    fontWeight: 600,
                    cursor: 'pointer',
                    marginTop: '10px',
                    textAlign: 'center'
                  }}
                >
                  + Add Item
                </button>
              </div>
            </div>
          ))}

          {/* Full-width Add Section Button below sections block */}
          <button 
            type="button" 
            onClick={addSection}
            style={{
              width: '100%',
              padding: '8px',
              borderRadius: '6px',
              backgroundColor: '#e0f2fe',
              color: '#0369a1',
              border: '1px dashed #bae6fd',
              fontSize: '11px',
              fontWeight: 600,
              cursor: 'pointer',
              marginTop: '4px',
              textAlign: 'center'
            }}
          >
            + Add Section
          </button>
        </div>

        {/* Usage Information matches original reference */}
        <div className="usage-info-card">
          <div className="usage-info-header">
            <Info size={14} className="usage-info-icon" />
            <span>Usage Information</span>
          </div>
          <ul className="usage-info-list">
            <li>Required fields are marked with *</li>
            <li>Maximum 10 items total across all sections</li>
            <li>Maximum 10 sections allowed</li>
            <li>Maximum 10 items per section</li>
            <li>Each list item must be connected to another node</li>
          </ul>
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" className="canvas-node-port port-input" />
    </div>
  )
}

// ==========================================
// 6. MEDIA MESSAGE NODE
// ==========================================
const MediaMessageNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const isCollapsed = data.isCollapsed || false
  const fileInputRef = useRef<HTMLInputElement>(null)

  const mediaType = data.mediaType || 'image'
  const mediaUrl = data.mediaUrl || ''
  const caption = data.caption || ''
  const fileName = data.fileName || ''

  const errors: string[] = []
  if (!mediaUrl.trim()) {
    errors.push('Media URL is required')
  }

  // File format suggestions based on mediaType
  let formatsText = 'Supported formats: .jpeg, .jpg, .png'
  if (mediaType === 'video') {
    formatsText = 'Supported formats: .mp4'
  } else if (mediaType === 'document') {
    formatsText = 'Supported formats: .pdf, .docx, .xlsx'
  }

  // Handle local file uploads dynamically
  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return

    const reader = new FileReader()
    reader.onload = (event) => {
      const result = event.target?.result as string
      updateNodeData(id, { 
        mediaUrl: result, 
        fileName: file.name 
      })
      toast.success(`${file.name} uploaded successfully!`)
    }
    reader.readAsDataURL(file)
  }

  const getHeaderTitle = () => {
    if (mediaType === 'video') return 'Video'
    if (mediaType === 'document') return 'Document'
    return 'Image'
  }

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '280px' }}>
      <NodeHeader
        id={id}
        title={getHeaderTitle()}
        icon={<ImageIcon size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Media URL is required"
        headerClass="media-message"
      />

      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        <ValidationPanel errors={errors} />

        <div className="node-input-fields">
          <label>Media Type *</label>
          <select 
            value={mediaType} 
            onChange={(e) => updateNodeData(id, { mediaType: e.target.value, mediaUrl: '', fileName: '' })}
          >
            <option value="image">Image</option>
            <option value="video">Video</option>
            <option value="document">Document</option>
          </select>
          <span style={{ fontSize: '10px', color: '#9ca3af', marginTop: '2px' }}>
            {formatsText}
          </span>
        </div>

        {/* Dotted Upload Media box with dynamic click handling */}
        <div className="node-input-fields">
          <label>Upload Media</label>
          <div className="upload-media-area" onClick={() => fileInputRef.current?.click()}>
            <UploadCloud size={24} className="upload-icon" />
            <span className="upload-text">Click to upload media</span>
            <input 
              type="file" 
              ref={fileInputRef} 
              style={{ display: 'none' }} 
              onChange={handleFileChange}
              accept={
                mediaType === 'image' ? 'image/*' : 
                mediaType === 'video' ? 'video/*' : 
                '.pdf,.doc,.docx,.xls,.xlsx,.txt'
              }
            />
          </div>
        </div>

        <div className="node-input-fields">
          <label className={errors.length > 0 ? 'error-label' : ''}>Media URL *</label>
          <input 
            className={errors.length > 0 ? 'error-input' : ''}
            type="text" 
            placeholder="Enter media file URL..." 
            value={mediaUrl} 
            onChange={(e) => updateNodeData(id, { mediaUrl: e.target.value, fileName: e.target.value.split('/').pop() || '' })} 
          />
        </div>

        <div className="node-input-fields">
          <label>Caption (Optional)</label>
          <input 
            type="text" 
            placeholder="Enter caption text..." 
            value={caption} 
            onChange={(e) => updateNodeData(id, { caption: e.target.value })} 
          />
        </div>

        {/* Media Preview matches original styling */}
        {mediaUrl.trim() && (
          <div className="media-preview-container">
            <span className="preview-label">Preview:</span>
            <div className="media-preview-box">
              {mediaType === 'image' && (
                <img src={mediaUrl} alt="Preview" className="preview-img" onError={(e) => {
                  (e.target as HTMLElement).style.display = 'none'
                }} />
              )}
              {mediaType === 'video' && (
                <video src={mediaUrl} controls className="preview-img" style={{ maxHeight: '120px', width: '100%', objectFit: 'contain', backgroundColor: '#000' }} />
              )}
              {mediaType === 'document' && (
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px', backgroundColor: '#f8fafc', borderBottom: '1px solid #cbd5e1' }}>
                  <FileText size={24} style={{ color: '#64748b', flexShrink: 0 }} />
                  <div style={{ display: 'flex', flexDirection: 'column', overflow: 'hidden', textAlign: 'left' }}>
                    <span style={{ fontSize: '12px', fontWeight: 600, color: '#334155', textOverflow: 'ellipsis', overflow: 'hidden', whiteSpace: 'nowrap' }}>
                      {fileName || 'document.pdf'}
                    </span>
                    <span style={{ fontSize: '10px', color: '#94a3b8' }}>Document File</span>
                  </div>
                </div>
              )}
              <div className="media-preview-details">
                <span className="media-type-badge">{mediaType.toUpperCase()} MESSAGE</span>
                <span className="media-caption">{caption || 'Will send media via WhatsApp'}</span>
              </div>
            </div>
          </div>
        )}
      </div>

      <Handle type="target" position={Position.Left} id="input" className="canvas-node-port port-input" />
      <Handle type="source" position={Position.Right} id="output" className="canvas-node-port port-output" />
    </div>
  )
}

// ==========================================
// 7. LOCATION NODE
// ==========================================
const LocationNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const isCollapsed = data.isCollapsed || false
  const [zoom, setZoom] = useState(13)
  const [searchVal, setSearchVal] = useState('')

  const lat = data.latitude || 0
  const lng = data.longitude || 0
  const name = data.locationName || ''
  const address = data.address || ''

  const errors: string[] = []
  if (lat === 0 || lng === 0 || isNaN(lat) || isNaN(lng)) {
    errors.push('Valid coordinates are required to send a location message.')
  }

  // Location search using OpenStreetMap Nominatim API
  const handleSearch = async () => {
    if (!searchVal.trim()) return
    try {
      const res = await fetch(`https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(searchVal)}`)
      const resolved = await res.json()
      if (resolved && resolved.length > 0) {
        const first = resolved[0]
        updateNodeData(id, {
          latitude: parseFloat(first.lat),
          longitude: parseFloat(first.lon),
          locationName: searchVal,
          address: first.display_name
        })
        toast.success(`Resolved to: ${first.display_name}`)
      } else {
        toast.error('Location not found.')
      }
    } catch (err) {
      // Fallback
      updateNodeData(id, {
        latitude: 28.6139,
        longitude: 77.2090,
        locationName: searchVal,
        address: `${searchVal}, India`
      })
      toast.error('Search query failed. Defaulting coordinates.')
    }
  }

  // Bounding box offset logic for zooming
  const bboxOffset = 0.08 / (zoom - 10)
  const mapLat = lat || 28.6139
  const mapLng = lng || 77.2090
  const mapUrl = `https://www.openstreetmap.org/export/embed.html?bbox=${mapLng - bboxOffset}%2C${mapLat - bboxOffset}%2C${mapLng + bboxOffset}%2C${mapLat + bboxOffset}&layer=mapnik&marker=${mapLat}%2C${mapLng}`

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '300px' }}>
      <NodeHeader
        id={id}
        title="Location Message"
        icon={<MapPin size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Coordinates are required"
        headerClass="location"
      />

      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        
        {/* Search Input and button */}
        <div className="node-input-fields">
          <label>Search Location</label>
          <div className="location-search-group">
            <input 
              type="text" 
              placeholder="Search for a location..." 
              value={searchVal}
              onChange={(e) => setSearchVal(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
            />
            <button type="button" onClick={handleSearch} className="search-btn">
              <Search size={14} />
            </button>
          </div>
        </div>

        {/* Dynamic Map Embed iframe */}
        <div className="node-input-fields">
          <label>Map</label>
          <div className="map-mockup-wrapper" style={{
            position: 'relative',
            height: '150px',
            backgroundColor: '#f1f5f9',
            borderRadius: '6px',
            overflow: 'hidden',
            border: '1px solid #cbd5e1'
          }}>
            <iframe
              title="Location Map"
              src={mapUrl}
              width="100%"
              height="100%"
              style={{ border: 'none', filter: 'contrast(1.05) brightness(0.98)' }}
            />

            {/* Float Zoom controls */}
            <div style={{
              position: 'absolute',
              left: '8px',
              top: '8px',
              display: 'flex',
              flexDirection: 'column',
              gap: '4px',
              zIndex: 5
            }}>
              <button type="button" onClick={() => setZoom(z => z + 1)} className="zoom-btn" title="Zoom In">+</button>
              <button type="button" onClick={() => setZoom(z => Math.max(11, z - 1))} className="zoom-btn" title="Zoom Out">-</button>
              <button type="button" onClick={() => setZoom(13)} className="zoom-btn" title="Recenter" style={{ fontSize: '10px' }}>⌖</button>
            </div>
          </div>
        </div>

        {/* Coordinates Preview */}
        <div className="node-input-fields">
          <label className={lat === 0 ? 'error-label' : ''}>Latitude</label>
          <input 
            className={lat === 0 ? 'error-input' : ''}
            type="number" 
            step="0.000001" 
            placeholder="e.g. 37.7749" 
            value={lat || ''} 
            onChange={(e) => updateNodeData(id, { latitude: parseFloat(e.target.value) || 0 })} 
          />
        </div>
        <div className="node-input-fields">
          <label className={lng === 0 ? 'error-label' : ''}>Longitude</label>
          <input 
            className={lng === 0 ? 'error-input' : ''}
            type="number" 
            step="0.000001" 
            placeholder="e.g. -122.4194" 
            value={lng || ''} 
            onChange={(e) => updateNodeData(id, { longitude: parseFloat(e.target.value) || 0 })} 
          />
        </div>

        <div className="node-input-fields">
          <label>Location Name</label>
          <input 
            type="text" 
            placeholder="e.g. Company Headquarters" 
            value={name} 
            onChange={(e) => updateNodeData(id, { locationName: e.target.value })} 
          />
        </div>

        <div className="node-input-fields">
          <label>Address</label>
          <textarea 
            placeholder="Enter full address" 
            value={address} 
            onChange={(e) => updateNodeData(id, { address: e.target.value })} 
            rows={2}
          />
        </div>

        {/* Validation Box at the bottom */}
        <ValidationPanel errors={errors} />
      </div>

      <Handle type="target" position={Position.Left} id="input" className="canvas-node-port port-input" />
      <Handle type="source" position={Position.Right} id="output" className="canvas-node-port port-output" />
    </div>
  )
}

// ==========================================
// 8. CONTACT CARD NODE
// ==========================================
const ContactCardNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const isCollapsed = data.isCollapsed || false
  const contacts = data.contacts || []

  const addContact = () => {
    const newContact = { firstName: '', lastName: '', phone: '', email: '', company: '', title: '' }
    updateNodeData(id, { contacts: [...contacts, newContact] })
  }

  const removeContact = (idx: number) => {
    updateNodeData(id, { contacts: contacts.filter((_: any, i: number) => i !== idx) })
  }

  const updateContactField = (idx: number, field: string, value: string) => {
    const updated = contacts.map((c: any, i: number) => {
      if (i === idx) return { ...c, [field]: value }
      return c
    })
    updateNodeData(id, { contacts: updated })
  }

  // Calculate Errors
  const errors: string[] = []
  if (contacts.length === 0) {
    errors.push('Please fill in all required fields marked with *')
  }
  const hasEmptyFields = contacts.some((c: any) => !c.firstName.trim() || !c.phone.trim())
  if (hasEmptyFields && errors.length === 0) {
    errors.push('Please fill in all required fields marked with *')
  }
  
  const invalidPhones = contacts.some((c: any) => c.phone.trim() && !isValidPhone(c.phone))
  if (invalidPhones) {
    errors.push('Phone number is invalid (must be E.164 format or {{phone}})')
  }

  const invalidEmails = contacts.some((c: any) => c.email && !isValidEmail(c.email))
  if (invalidEmails) {
    errors.push('Email address is invalid')
  }

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '310px' }}>
      <NodeHeader
        id={id}
        title="Contact Message"
        icon={<User size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Required fields missing"
        headerClass="contact-card"
      />

      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        
        {/* Validation Panel */}
        <ValidationPanel errors={errors} />

        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <label style={{ fontSize: '11px', fontWeight: 600, color: '#6b7280' }}>Contacts ({contacts.length})</label>
          <button 
            type="button" 
            onClick={addContact}
            className="add-field-link-btn"
          >
            + Add Another Contact
          </button>
        </div>

        {contacts.map((c: any, idx: number) => {
          const isPhoneValid = !c.phone.trim() || isValidPhone(c.phone)
          const isEmailValid = isValidEmail(c.email)

          return (
            <div key={idx} className="inner-item-card contact-item-card">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
                <span className="inner-item-label">Contact {idx + 1}</span>
                <button type="button" onClick={() => removeContact(idx)} className="inner-item-delete-btn">Remove</button>
              </div>
              <div>
                <label className="sub-label">First Name *</label>
                <input 
                  type="text" 
                  placeholder="First name" 
                  value={c.firstName} 
                  onChange={(e) => updateContactField(idx, 'firstName', e.target.value)} 
                  className={!c.firstName.trim() ? 'error-input' : ''}
                />
              </div>
              <div style={{ marginTop: '6px' }}>
                <label className="sub-label">Last Name *</label>
                <input 
                  type="text" 
                  placeholder="Last name" 
                  value={c.lastName || ''} 
                  onChange={(e) => updateContactField(idx, 'lastName', e.target.value)} 
                  className={!c.lastName?.trim() ? 'error-input' : ''}
                />
              </div>

              <div style={{ marginTop: '6px' }}>
                <label className="sub-label">Phone *</label>
                <input 
                  type="text" 
                  placeholder="e.g. +1234567890 or {{phone}}" 
                  value={c.phone} 
                  onChange={(e) => updateContactField(idx, 'phone', e.target.value)} 
                  className={!c.phone.trim() || !isPhoneValid ? 'error-input' : ''}
                />
                {!isPhoneValid && (
                  <span className="input-error-msg">Phone number is invalid</span>
                )}
              </div>

              <div style={{ marginTop: '6px' }}>
                <label className="sub-label">Email</label>
                <input 
                  type="text" 
                  placeholder="Email (optional)" 
                  value={c.email || ''} 
                  onChange={(e) => updateContactField(idx, 'email', e.target.value)} 
                  className={!isEmailValid ? 'error-input' : ''}
                />
                {!isEmailValid && (
                  <span className="input-error-msg">Email is invalid</span>
                )}
              </div>

              <div style={{ marginTop: '6px' }}>
                <label className="sub-label">Company</label>
                <input 
                  type="text" 
                  placeholder="Company (optional)" 
                  value={c.company || ''} 
                  onChange={(e) => updateContactField(idx, 'company', e.target.value)} 
                />
              </div>
              <div style={{ marginTop: '6px' }}>
                <label className="sub-label">Title</label>
                <input 
                  type="text" 
                  placeholder="Title (optional)" 
                  value={c.title || ''} 
                  onChange={(e) => updateContactField(idx, 'title', e.target.value)} 
                />
              </div>
            </div>
          )
        })}

        <div style={{ fontSize: '10px', color: '#9ca3af', display: 'flex', gap: '4px', alignItems: 'center' }}>
          <Info size={12} />
          <span>Required fields are marked with *</span>
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" className="canvas-node-port port-input" />
      <Handle type="source" position={Position.Right} id="output" className="canvas-node-port port-output" />
    </div>
  )
}

// ==========================================
// 9. CUSTOM AI ASSISTANT NODE
// ==========================================
const AIAssistantNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const isCollapsed = data.isCollapsed || false

  const errors: string[] = []
  if (!data.instructions || !data.instructions.trim()) {
    errors.push('System Instructions context is required')
  }

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''} ${errors.length > 0 ? 'invalid' : ''}`} style={{ width: '300px' }}>
      <NodeHeader
        id={id}
        title="AI Assistant"
        icon={<Bot size={14} />}
        isCollapsed={isCollapsed}
        hasErrors={errors.length > 0}
        errorText="Required fields missing"
        headerClass="ai-personal-assistant"
      />

      <div className="node-body-container" style={{ display: isCollapsed ? 'none' : 'flex', flexDirection: 'column', gap: '12px' }}>
        <ValidationPanel errors={errors} />

        <div className="node-input-fields">
          <label>AI Model *</label>
          <select 
            value={data.aiModel || 'Gemini 1.5 Flash'} 
            onChange={(e) => updateNodeData(id, { aiModel: e.target.value })}
          >
            <option value="Gemini 1.5 Flash">Gemini 1.5 Flash</option>
            <option value="Gemini 1.5 Pro">Gemini 1.5 Pro</option>
            <option value="GPT-4o Mock">GPT-4o Mock</option>
          </select>
        </div>
        <div className="node-input-fields">
          <label className={errors.length > 0 ? 'error-label' : ''}>System Instructions *</label>
          <textarea
            className={errors.length > 0 ? 'error-input' : ''}
            value={data.instructions || ''}
            placeholder="Enter bot flow instructions context..."
            onChange={(e) => updateNodeData(id, { instructions: e.target.value })}
            rows={4}
          />
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" className="canvas-node-port port-input" />
      <Handle type="source" position={Position.Right} id="output" className="canvas-node-port port-output" />
    </div>
  )
}

// Mappings & Node Register
const typeMapping: Record<string, string> = {
  'Start Trigger': 'startTrigger',
  'Text Message': 'textMessage',
  'Button Message': 'buttonMessage',
  'Call to Action': 'callToAction',
  'List Message': 'listMessage',
  'Media Message': 'mediaMessage',
  'Location': 'location',
  'Contact Card': 'contactCard',
  'AI Personal Assistant': 'aiAssistant'
}

const reverseTypeMapping = Object.fromEntries(
  Object.entries(typeMapping).map(([k, v]) => [v, k])
)

const nodeTypes = {
  startTrigger: StartTriggerNode,
  textMessage: TextMessageNode,
  buttonMessage: ButtonMessageNode,
  callToAction: CallToActionNode,
  listMessage: ListMessageNode,
  mediaMessage: MediaMessageNode,
  location: LocationNode,
  contactCard: ContactCardNode,
  aiAssistant: AIAssistantNode
}

// Custom Edge with Delete Button
function ButtonEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style = {},
  markerEnd,
}: EdgeProps) {
  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });

  const { setEdges } = useReactFlow();

  const onEdgeClick = (evt: React.MouseEvent) => {
    evt.stopPropagation();
    setEdges((edges) => edges.filter((edge) => edge.id !== id));
  };

  return (
    <>
      <BaseEdge path={edgePath} markerEnd={markerEnd} style={style} />
      <EdgeLabelRenderer>
        <div
          style={{
            position: 'absolute',
            transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
            pointerEvents: 'all',
          }}
          className="nodrag nopan edge-delete-btn-container"
        >
          <button className="edge-delete-btn" onClick={onEdgeClick} title="Delete connection">
            ×
          </button>
        </div>
      </EdgeLabelRenderer>
    </>
  );
}

const edgeTypes = {
  buttonedge: ButtonEdge,
};

// ==========================================
// 10. MAIN CANVAS DESIGNER
// ==========================================
const DesignerFlow = () => {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const flowId = id ? parseInt(id, 10) : 0

  const nodes = useBotFlowStore(state => state.nodes)
  const edges = useBotFlowStore(state => state.edges)
  const setNodes = useBotFlowStore(state => state.setNodes)
  const setEdges = useBotFlowStore(state => state.setEdges)
  const onNodesChange = useBotFlowStore(state => state.onNodesChange)
  const onEdgesChange = useBotFlowStore(state => state.onEdgesChange)
  const onConnect = useBotFlowStore(state => state.onConnect)
  const addNode = useBotFlowStore(state => state.addNode)

  const [flowName, setFlowName] = useState('')
  const [flowDesc, setFlowDesc] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [isSaving, setIsSaving] = useState(false)

  const { screenToFlowPosition } = useReactFlow()

  // Load flow data from server
  useEffect(() => {
    const loadFlow = async () => {
      try {
        const flow = await botFlowService.getBotFlowById(flowId)
        if (flow) {
          setFlowName(flow.name)
          setFlowDesc(flow.description || '')
          setIsActive(flow.isActive)

          if (flow.flowData && flow.flowData !== '{}') {
            const parsed = JSON.parse(flow.flowData)
            const mappedNodes = (parsed.nodes || []).map((node: any) => ({
              ...node,
              type: typeMapping[node.type] || node.type
            }))
            setNodes(mappedNodes)
            setEdges(parsed.edges || parsed.connections || [])
          } else {
            // Default Start Trigger node if canvas empty
            setNodes([
              {
                id: 'trigger-1',
                type: 'startTrigger',
                position: { x: 150, y: 150 },
                data: {
                  contactType: 'Lead',
                  triggerType: 'on exact match',
                  keywords: ['remit']
                }
              }
            ])
            setEdges([])
          }
        }
      } catch (err) {
        toast.error('Failed to load bot flow.')
      }
    }
    loadFlow()
  }, [flowId, setNodes, setEdges])

  // Drag and drop sidebar utilities
  const onDragOver = (e: React.DragEvent) => {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'move'
  }

  const onDrop = (e: React.DragEvent) => {
    e.preventDefault()
    const rawType = e.dataTransfer.getData('application/reactflow')
    if (!rawType) return

    const type = typeMapping[rawType] || rawType
    const position = screenToFlowPosition({
      x: e.clientX,
      y: e.clientY
    })

    const newNode = {
      id: `node_${Date.now()}`,
      type,
      position,
      data: {
        messageText: '',
        buttons: [],
        keywords: []
      }
    }

    addNode(newNode)
  }

  const handleSaveFlow = async () => {
    setIsSaving(true)
    try {
      const mappedNodes = nodes.map(node => ({
        ...node,
        type: node.type ? (reverseTypeMapping[node.type] || node.type) : undefined
      }))
      const flowDataStr = JSON.stringify({ nodes: mappedNodes, edges })
      await botFlowService.updateBotFlow(flowId, {
        name: flowName,
        description: flowDesc,
        isActive,
        flowData: flowDataStr
      })
      toast.success('Flow saved successfully!')
    } catch (err) {
      toast.error('Failed to save flow configuration.')
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="bot-flow-designer-container">
      <div className="designer-header">
        <div className="header-left">
          <button onClick={() => navigate('/bot-flow')} style={{ background: 'none', border: 'none', cursor: 'pointer', display: 'flex', alignItems: 'center' }}>
            <ArrowLeft size={20} />
          </button>
          <div className="flow-meta-titles">
            <span className="flow-designer-title">{flowName || 'Bot Flow Builder'}</span>
            <span className="flow-designer-subtitle">{flowDesc || 'Configure nodes and branching logic'}</span>
          </div>
        </div>

        <div className="header-right">
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginRight: '16px' }}>
            <label style={{ fontSize: '13px', fontWeight: 500, cursor: 'pointer' }}>
              <input 
                type="checkbox" 
                checked={isActive} 
                onChange={(e) => setIsActive(e.target.checked)} 
                style={{ marginRight: '6px' }}
              />
              Active
            </label>
          </div>
          <button 
            className="designer-action-btn btn-primary" 
            onClick={handleSaveFlow} 
            disabled={isSaving}
          >
            <Save size={16} />
            <span>{isSaving ? 'Saving...' : 'Save Flow'}</span>
          </button>
        </div>
      </div>

      <div className="designer-editor-pane">
        {/* Available Components sidebar list */}
        <div className="designer-components-sidebar">
          <div className="sidebar-header">
            <span>Available Components</span>
          </div>
          <div className="sidebar-scrollable-body">
            <div className="component-category">
              <span className="category-title">Basic Messages</span>
              <div className="category-list">
                <div 
                  className="draggable-node-item" 
                  draggable 
                  onDragStart={(e) => e.dataTransfer.setData('application/reactflow', 'Text Message')}
                >
                  💬 Text Message
                </div>
                <div 
                  className="draggable-node-item" 
                  draggable 
                  onDragStart={(e) => e.dataTransfer.setData('application/reactflow', 'Button Message')}
                >
                  🔘 Button Message
                </div>
                <div 
                  className="draggable-node-item" 
                  draggable 
                  onDragStart={(e) => e.dataTransfer.setData('application/reactflow', 'Call to Action')}
                >
                  🔗 Call To Action
                </div>
              </div>
            </div>

            <div className="component-category">
              <span className="category-title">Interactive Content</span>
              <div className="category-list">
                <div 
                  className="draggable-node-item" 
                  draggable 
                  onDragStart={(e) => e.dataTransfer.setData('application/reactflow', 'List Message')}
                >
                  📋 List Message
                </div>
                <div 
                  className="draggable-node-item" 
                  draggable 
                  onDragStart={(e) => e.dataTransfer.setData('application/reactflow', 'Media Message')}
                >
                  🖼️ Media Message
                </div>
                <div 
                  className="draggable-node-item" 
                  draggable 
                  onDragStart={(e) => e.dataTransfer.setData('application/reactflow', 'Location')}
                >
                  📍 Location
                </div>
                <div 
                  className="draggable-node-item" 
                  draggable 
                  onDragStart={(e) => e.dataTransfer.setData('application/reactflow', 'Contact Card')}
                >
                  📇 Contact Card
                </div>
              </div>
            </div>

            <div className="component-category">
              <span className="category-title">Advanced Features</span>
              <div className="category-list">
                <div 
                  className="draggable-node-item" 
                  draggable 
                  onDragStart={(e) => e.dataTransfer.setData('application/reactflow', 'AI Personal Assistant')}
                >
                  🤖 AI Assistant
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* React Flow grid canvas */}
        <div 
          className="designer-canvas-grid" 
          onDragOver={onDragOver}
          onDrop={onDrop}
          style={{ height: '100%', width: '100%', position: 'relative' }}
        >
          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            fitView
          >
            <Background color="#cbd5e1" gap={16} size={1} />
            <Controls />
            <MiniMap style={{ bottom: 24, right: 24 }} />
          </ReactFlow>
        </div>
      </div>
    </div>
  )
}

export const BotFlowDesigner = () => {
  return (
    <ReactFlowProvider>
      <DesignerFlow />
    </ReactFlowProvider>
  )
}

export default BotFlowDesigner
