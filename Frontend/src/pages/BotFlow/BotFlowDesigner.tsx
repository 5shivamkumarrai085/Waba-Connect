import React, { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { ArrowLeft, Save, Trash2 } from 'lucide-react'
import { 
  ReactFlow, 
  Controls, 
  Background, 
  MiniMap, 
  Handle, 
  Position, 
  ReactFlowProvider,
  useReactFlow
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { botFlowService } from '../../services/botFlow/botFlowService'
import { useBotFlowStore } from './botFlowStore'
import { toast } from 'react-hot-toast'
import './BotFlowDesigner.css'

// 1. Custom Start Trigger Node
const StartTriggerNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
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

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`} style={{ width: '300px' }}>
      <div className="node-header-bar start-trigger">
        <span>Start Trigger</span>
      </div>
      <div className="node-body-container">
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
      <Handle type="source" position={Position.Right} id="output" />
    </div>
  )
}

// 2. Custom Text Message Node
const TextMessageNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const deleteNode = useBotFlowStore(state => state.deleteNode)

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`}>
      <div className="node-header-bar text-message">
        <span>Text Message</span>
        <button className="node-delete-btn" onClick={() => deleteNode(id)}>
          <Trash2 size={12} />
        </button>
      </div>
      <div className="node-body-container">
        <div className="node-input-fields">
          <label>Message Text *</label>
          <textarea
            value={data.messageText || ''}
            placeholder="Enter your message text here..."
            maxLength={1000}
            onChange={(e) => updateNodeData(id, { messageText: e.target.value })}
            rows={3}
          />
          <div style={{ textAlign: 'right', fontSize: '10px', color: '#9ca3af' }}>
            {(data.messageText || '').length}/1000
          </div>
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" />
      <Handle type="source" position={Position.Right} id="output" />
    </div>
  )
}

// 3. Custom Button Message Node
const ButtonMessageNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const deleteNode = useBotFlowStore(state => state.deleteNode)
  const buttons = data.buttons || []

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

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`} style={{ width: '300px' }}>
      <div className="node-header-bar button-message">
        <span>Button Message</span>
        <button className="node-delete-btn" onClick={() => deleteNode(id)}>
          <Trash2 size={12} />
        </button>
      </div>
      <div className="node-body-container">
        <div className="node-input-fields">
          <label>Message Text *</label>
          <textarea
            value={data.messageText || ''}
            placeholder="Enter message text..."
            onChange={(e) => updateNodeData(id, { messageText: e.target.value })}
            rows={2}
          />
        </div>

        <div className="node-input-fields">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '6px' }}>
            <label>Buttons ({buttons.length}/3)</label>
            {buttons.length < 3 && (
              <button 
                type="button" 
                style={{ background: 'none', border: 'none', color: '#6366f1', fontSize: '11px', cursor: 'pointer', fontWeight: 600 }}
                onClick={addButton}
              >
                + Add Button
              </button>
            )}
          </div>
          {buttons.map((b: any, idx: number) => (
            <div key={idx} style={{ position: 'relative', border: '1px solid #f3f4f6', padding: '8px', borderRadius: '4px', marginBottom: '8px', backgroundColor: '#f9fafb' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span style={{ fontSize: '11px', fontWeight: 600, color: '#4b5563' }}>Button {idx + 1}</span>
                <button type="button" onClick={() => removeButton(idx)} style={{ background: 'none', border: 'none', color: '#ef4444', fontSize: '11px', cursor: 'pointer' }}>Remove</button>
              </div>
              <div style={{ display: 'flex', gap: '6px', marginTop: '6px' }}>
                <input 
                  type="text" 
                  placeholder="Text (max 20 chars)" 
                  maxLength={20}
                  value={b.text}
                  onChange={(e) => updateButtonField(idx, 'text', e.target.value)}
                  style={{ flex: 1, padding: '4px', fontSize: '12px' }}
                />
                <input 
                  type="text" 
                  placeholder="Payload value" 
                  value={b.value}
                  onChange={(e) => updateButtonField(idx, 'value', e.target.value)}
                  style={{ flex: 1, padding: '4px', fontSize: '12px' }}
                />
              </div>
              <Handle 
                type="source" 
                position={Position.Right} 
                id={`button-${idx}`} 
                style={{ top: '50%', right: '-12px' }} 
              />
            </div>
          ))}
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" />
    </div>
  )
}

// 4. Custom Call To Action Node
const CallToActionNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const deleteNode = useBotFlowStore(state => state.deleteNode)

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`} style={{ width: '300px' }}>
      <div className="node-header-bar call-to-action">
        <span>Call To Action</span>
        <button className="node-delete-btn" onClick={() => deleteNode(id)}>
          <Trash2 size={12} />
        </button>
      </div>
      <div className="node-body-container">
        <div className="node-input-fields">
          <label>HeaderText (Optional)</label>
          <input 
            type="text" 
            placeholder="Header text..." 
            value={data.header || ''} 
            onChange={(e) => updateNodeData(id, { header: e.target.value })} 
          />
        </div>
        <div className="node-input-fields">
          <label>Value Text *</label>
          <textarea
            value={data.valueText || ''}
            placeholder="Main message text..."
            onChange={(e) => updateNodeData(id, { valueText: e.target.value })}
            rows={2}
          />
        </div>
        <div className="node-input-fields">
          <label>Button Text *</label>
          <input 
            type="text" 
            placeholder="Button text..." 
            value={data.buttonText || ''} 
            onChange={(e) => updateNodeData(id, { buttonText: e.target.value })} 
          />
        </div>
        <div className="node-input-fields">
          <label>Button Link URL *</label>
          <input 
            type="text" 
            placeholder="https://example.com" 
            value={data.buttonLink || ''} 
            onChange={(e) => updateNodeData(id, { buttonLink: e.target.value })} 
          />
        </div>
        <div className="node-input-fields">
          <label>FooterText (Optional)</label>
          <input 
            type="text" 
            placeholder="Footer text..." 
            value={data.footer || ''} 
            onChange={(e) => updateNodeData(id, { footer: e.target.value })} 
          />
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" />
      <Handle type="source" position={Position.Right} id="output" />
    </div>
  )
}

// 5. Custom List Message Node
const ListMessageNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const deleteNode = useBotFlowStore(state => state.deleteNode)
  const sections = data.sections || []

  const addSection = () => {
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
    const updated = sections.map((sec: any, i: number) => {
      if (i === secIdx) {
        const items = sec.items || []
        const newItem = { id: `item-${Date.now()}`, title: `Item ${items.length + 1}`, description: '' }
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

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`} style={{ width: '320px' }}>
      <div className="node-header-bar call-to-action" style={{ backgroundColor: '#ec4899' }}>
        <span>List Message</span>
        <button className="node-delete-btn" onClick={() => deleteNode(id)}>
          <Trash2 size={12} />
        </button>
      </div>
      <div className="node-body-container">
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
          <label>Body Text *</label>
          <textarea
            value={data.bodyText || ''}
            placeholder="Message body..."
            onChange={(e) => updateNodeData(id, { bodyText: e.target.value })}
            rows={2}
          />
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
          <label>Button Text *</label>
          <input 
            type="text" 
            placeholder="e.g. View Menu" 
            value={data.buttonText || ''} 
            onChange={(e) => updateNodeData(id, { buttonText: e.target.value })} 
          />
        </div>

        <div className="node-input-fields">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <label>Sections & Items</label>
            <button 
              type="button" 
              onClick={addSection}
              style={{ background: 'none', border: 'none', color: '#6366f1', fontSize: '11px', cursor: 'pointer', fontWeight: 600 }}
            >
              + Add Section
            </button>
          </div>

          {sections.map((sec: any, secIdx: number) => (
            <div key={secIdx} style={{ border: '1px solid #e5e7eb', borderRadius: '4px', padding: '8px', marginTop: '6px', backgroundColor: '#f9fafb' }}>
              <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
                <input 
                  type="text" 
                  value={sec.title} 
                  placeholder="Section title..." 
                  onChange={(e) => updateSectionTitle(secIdx, e.target.value)}
                  style={{ flex: 1, padding: '4px', fontSize: '12px' }}
                />
                <button type="button" onClick={() => removeSection(secIdx)} style={{ background: 'none', border: 'none', color: '#ef4444', fontSize: '11px', cursor: 'pointer' }}>Delete</button>
              </div>

              <div style={{ marginTop: '8px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <span style={{ fontSize: '10px', fontWeight: 600, color: '#6b7280' }}>Items</span>
                  <button 
                    type="button" 
                    onClick={() => addItemToSection(secIdx)}
                    style={{ background: 'none', border: 'none', color: '#10b981', fontSize: '10px', cursor: 'pointer', fontWeight: 600 }}
                  >
                    + Add Item
                  </button>
                </div>

                {(sec.items || []).map((item: any, itemIdx: number) => (
                  <div key={item.id} style={{ position: 'relative', border: '1px solid #f3f4f6', borderRadius: '4px', padding: '6px', marginTop: '6px', backgroundColor: '#ffffff' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <input 
                        type="text" 
                        value={item.title} 
                        placeholder="Item Title" 
                        onChange={(e) => updateItemField(secIdx, itemIdx, 'title', e.target.value)}
                        style={{ flex: 1, border: 'none', borderBottom: '1px solid #e5e7eb', fontSize: '12px', padding: '2px 0' }}
                      />
                      <button type="button" onClick={() => removeItemFromSection(secIdx, itemIdx)} style={{ background: 'none', border: 'none', color: '#ef4444', fontSize: '11px', cursor: 'pointer', marginLeft: '6px' }}>×</button>
                    </div>
                    <input 
                      type="text" 
                      value={item.description} 
                      placeholder="Description (optional)" 
                      onChange={(e) => updateItemField(secIdx, itemIdx, 'description', e.target.value)}
                      style={{ border: 'none', fontSize: '10px', width: '100%', marginTop: '4px' }}
                    />
                    <input 
                      type="text" 
                      value={item.value || ''} 
                      placeholder="Item Value ID" 
                      onChange={(e) => updateItemField(secIdx, itemIdx, 'value', e.target.value)}
                      style={{ border: 'none', fontSize: '10px', width: '100%', marginTop: '4px', color: '#9ca3af' }}
                    />
                    <Handle 
                      type="source" 
                      position={Position.Right} 
                      id={`item-${item.value || item.id}`} 
                      style={{ top: '50%', right: '-10px' }} 
                    />
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" />
    </div>
  )
}

// 6. Custom Media Message Node
const MediaMessageNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const deleteNode = useBotFlowStore(state => state.deleteNode)

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`}>
      <div className="node-header-bar media-message">
        <span>Media Message</span>
        <button className="node-delete-btn" onClick={() => deleteNode(id)}>
          <Trash2 size={12} />
        </button>
      </div>
      <div className="node-body-container">
        <div className="node-input-fields">
          <label>Media Type *</label>
          <select 
            value={data.mediaType || 'image'} 
            onChange={(e) => updateNodeData(id, { mediaType: e.target.value })}
          >
            <option value="image">Image</option>
            <option value="video">Video</option>
            <option value="document">Document</option>
          </select>
        </div>
        <div className="node-input-fields">
          <label>Media URL *</label>
          <input 
            type="text" 
            placeholder="Enter media file URL..." 
            value={data.mediaUrl || ''} 
            onChange={(e) => updateNodeData(id, { mediaUrl: e.target.value })} 
          />
        </div>
        <div className="node-input-fields">
          <label>Caption (Optional)</label>
          <input 
            type="text" 
            placeholder="Enter caption text..." 
            value={data.caption || ''} 
            onChange={(e) => updateNodeData(id, { caption: e.target.value })} 
          />
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" />
      <Handle type="source" position={Position.Right} id="output" />
    </div>
  )
}

// 7. Custom Location Node
const LocationNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const deleteNode = useBotFlowStore(state => state.deleteNode)

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`}>
      <div className="node-header-bar location">
        <span>Location Message</span>
        <button className="node-delete-btn" onClick={() => deleteNode(id)}>
          <Trash2 size={12} />
        </button>
      </div>
      <div className="node-body-container">
        <div className="node-input-fields">
          <label>Latitude *</label>
          <input 
            type="number" 
            step="0.000001" 
            placeholder="e.g. 28.6139" 
            value={data.latitude || ''} 
            onChange={(e) => updateNodeData(id, { latitude: parseFloat(e.target.value) || 0 })} 
          />
        </div>
        <div className="node-input-fields">
          <label>Longitude *</label>
          <input 
            type="number" 
            step="0.000001" 
            placeholder="e.g. 77.2090" 
            value={data.longitude || ''} 
            onChange={(e) => updateNodeData(id, { longitude: parseFloat(e.target.value) || 0 })} 
          />
        </div>
        <div className="node-input-fields">
          <label>Location Name</label>
          <input 
            type="text" 
            placeholder="Place name..." 
            value={data.locationName || ''} 
            onChange={(e) => updateNodeData(id, { locationName: e.target.value })} 
          />
        </div>
        <div className="node-input-fields">
          <label>Address</label>
          <input 
            type="text" 
            placeholder="Full address..." 
            value={data.address || ''} 
            onChange={(e) => updateNodeData(id, { address: e.target.value })} 
          />
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" />
      <Handle type="source" position={Position.Right} id="output" />
    </div>
  )
}

// 8. Custom Contact Card Node
const ContactCardNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const deleteNode = useBotFlowStore(state => state.deleteNode)
  const contacts = data.contacts || []

  const addContact = () => {
    const newContact = { firstName: '', lastName: '', phone: '' }
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

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`} style={{ width: '300px' }}>
      <div className="node-header-bar contact-card">
        <span>Contact Card</span>
        <button className="node-delete-btn" onClick={() => deleteNode(id)}>
          <Trash2 size={12} />
        </button>
      </div>
      <div className="node-body-container">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <label style={{ fontSize: '11px', fontWeight: 600, color: '#6b7280' }}>Contacts</label>
          <button 
            type="button" 
            onClick={addContact}
            style={{ background: 'none', border: 'none', color: '#6366f1', fontSize: '11px', cursor: 'pointer', fontWeight: 600 }}
          >
            + Add Contact
          </button>
        </div>

        {contacts.map((c: any, idx: number) => (
          <div key={idx} style={{ border: '1px solid #e5e7eb', padding: '8px', borderRadius: '4px', marginTop: '6px', backgroundColor: '#f9fafb' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ fontSize: '10px', fontWeight: 600, color: '#4b5563' }}>Contact {idx + 1}</span>
              <button type="button" onClick={() => removeContact(idx)} style={{ background: 'none', border: 'none', color: '#ef4444', fontSize: '10px', cursor: 'pointer' }}>Remove</button>
            </div>
            <div style={{ display: 'flex', gap: '4px', marginTop: '4px' }}>
              <input 
                type="text" 
                placeholder="First Name *" 
                value={c.firstName} 
                onChange={(e) => updateContactField(idx, 'firstName', e.target.value)} 
                style={{ flex: 1, padding: '2px 4px', fontSize: '11px' }}
              />
              <input 
                type="text" 
                placeholder="Last Name" 
                value={c.lastName} 
                onChange={(e) => updateContactField(idx, 'lastName', e.target.value)} 
                style={{ flex: 1, padding: '2px 4px', fontSize: '11px' }}
              />
            </div>
            <input 
              type="text" 
              placeholder="Phone number * (e.g. +123...)" 
              value={c.phone} 
              onChange={(e) => updateContactField(idx, 'phone', e.target.value)} 
              style={{ width: '100%', padding: '2px 4px', fontSize: '11px', marginTop: '4px' }}
            />
            <input 
              type="text" 
              placeholder="Email (optional)" 
              value={c.email || ''} 
              onChange={(e) => updateContactField(idx, 'email', e.target.value)} 
              style={{ width: '100%', padding: '2px 4px', fontSize: '11px', marginTop: '4px' }}
            />
          </div>
        ))}
      </div>
      <Handle type="target" position={Position.Left} id="input" />
      <Handle type="source" position={Position.Right} id="output" />
    </div>
  )
}

// 9. Custom AI Assistant Node
const AIAssistantNode = ({ id, data, selected }: any) => {
  const updateNodeData = useBotFlowStore(state => state.updateNodeData)
  const deleteNode = useBotFlowStore(state => state.deleteNode)

  return (
    <div className={`node-card-wrapper ${selected ? 'selected' : ''}`} style={{ width: '300px' }}>
      <div className="node-header-bar ai-personal-assistant">
        <span>AI Assistant</span>
        <button className="node-delete-btn" onClick={() => deleteNode(id)}>
          <Trash2 size={12} />
        </button>
      </div>
      <div className="node-body-container">
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
          <label>System Instructions *</label>
          <textarea
            value={data.instructions || ''}
            placeholder="Enter bot flow instructions context..."
            onChange={(e) => updateNodeData(id, { instructions: e.target.value })}
            rows={3}
          />
        </div>
      </div>
      <Handle type="target" position={Position.Left} id="input" />
      <Handle type="source" position={Position.Right} id="output" />
    </div>
  )
}

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
};

const reverseTypeMapping = Object.fromEntries(
  Object.entries(typeMapping).map(([k, v]) => [v, k])
);

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
};

// 10. Main Canvas Workflow Designer component
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
            }));
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
      }));
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
