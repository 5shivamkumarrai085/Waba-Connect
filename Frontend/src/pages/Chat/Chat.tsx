import React, { useEffect, useState, useRef } from 'react'
import { useChatStore } from '../../store/chatStore'
import { Avatar } from '../../components/Avatar/Avatar'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import toast from 'react-hot-toast'
import { 
  Search, 
  Info, 
  MessageSquare, 
  MoreVertical, 
  Smile, 
  Paperclip, 
  FileText, 
  MessageCircle,
  Mic, 
  CheckCheck, 
  AlertCircle 
} from 'lucide-react'
import './Chat.css'

export const Chat: React.FC = () => {
  const {
    conversations,
    activeConversationId,
    messages,
    isLoading,
    fromNumber,
    conversationsFilter,
    sidebarSearchQuery,
    
    loadConversations,
    selectConversation,
    sendMessage,
    setFromNumber,
    setConversationsFilter,
    setSidebarSearchQuery
  } = useChatStore()

  const [messageText, setMessageText] = useState('')
  const messagesEndRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    loadConversations()
  }, [])

  // Auto scroll to bottom when message arrives
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  // Filter sidebar conversation rows
  const filteredConversations = conversations.filter((c) => {
    // 1. Sidebar Search query
    if (sidebarSearchQuery) {
      const q = sidebarSearchQuery.toLowerCase()
      if (!c.name.toLowerCase().includes(q) && !c.phone.includes(q)) return false
    }
    
    // 2. Select dropdown filter
    if (conversationsFilter === 'Unread Chats') {
      if (c.unreadCount === 0) return false
    }
    
    return true
  })

  // Handle composer submit
  const handleSend = (e: React.FormEvent) => {
    e.preventDefault()
    if (!messageText.trim()) return
    sendMessage(messageText.trim())
    setMessageText('')
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      if (messageText.trim()) {
        sendMessage(messageText.trim())
        setMessageText('')
      }
    }
  }

  // Get active conversation metadata details
  const activeConversation = conversations.find(c => c.id === activeConversationId)

  // Empty State Phone drawing SVG illustration
  const EmptyStateIllustration = () => (
    <svg className="chat-empty-state-illustration" viewBox="0 0 200 200" fill="none" xmlns="http://www.w3.org/2000/svg">
      {/* Smartphone frame */}
      <rect x="65" y="20" width="70" height="140" rx="12" fill="#E2E8F0" stroke="#94A3B8" strokeWidth="3" />
      <line x1="90" y1="26" x2="110" y2="26" stroke="#94A3B8" strokeWidth="2" strokeLinecap="round" />
      <circle cx="100" cy="150" r="5" fill="#94A3B8" />
      
      {/* Outgoing bubble left */}
      <rect x="25" y="50" width="35" height="15" rx="6" fill="#D9FDD3" stroke="#A7F3D0" />
      <rect x="30" y="55" width="20" height="2" rx="1" fill="#047857" opacity="0.3" />
      <rect x="30" y="60" width="10" height="2" rx="1" fill="#047857" opacity="0.3" />
      <line x1="57" y1="62" x2="65" y2="65" stroke="#A7F3D0" />

      {/* Incoming bubble right */}
      <rect x="140" y="80" width="35" height="15" rx="6" fill="#FFFFFF" stroke="#CBD5E1" />
      <rect x="145" y="85" width="20" height="2" rx="1" fill="#475569" opacity="0.2" />
      <rect x="145" y="90" width="15" height="2" rx="1" fill="#475569" opacity="0.2" />
      <line x1="140" y1="92" x2="135" y2="95" stroke="#CBD5E1" />
    </svg>
  )

  return (
    <div className="fade-in chat-container-layout">
      
      {/* Left Conversations Sidebar */}
      <div className="chat-sidebar">
        <div className="chat-sidebar-header">
          <div className="chat-account-display-row">
            <Avatar name="From Account" size="small" />
            <div className="chat-dropdown-full">
              <span className="upload-sub-text">From:</span>
              <select
                className="form-control"
                value={fromNumber}
                onChange={(e) => setFromNumber(e.target.value)}
              >
                <option value="+60108052877">+60108052877</option>
                <option value="+919499373415">+919499373415</option>
              </select>
            </div>
          </div>

          <select
            className="form-control"
            value={conversationsFilter}
            onChange={(e) => setConversationsFilter(e.target.value)}
          >
            <option value="All Chats">All Chats</option>
            <option value="Unread Chats">Unread Chats</option>
          </select>
        </div>

        {/* Sidebar Search query input */}
        <div className="chat-sidebar-search">
          <SearchBar
            value={sidebarSearchQuery}
            onChange={setSidebarSearchQuery}
            placeholder="Searching..."
          />
        </div>

        {/* Conversation rows list */}
        <div className="conversation-list-scroll">
          {isLoading && conversations.length === 0 ? (
            <div className="page-loader">
              <p className="upload-sub-text">Loading...</p>
            </div>
          ) : filteredConversations.length === 0 ? (
            <div className="data-table-empty">
              <p className="upload-sub-text">No chats found</p>
            </div>
          ) : (
            filteredConversations.map((conv) => {
              const isActive = conv.id === activeConversationId
              return (
                <div
                  key={conv.id}
                  className={`conversation-item ${isActive ? 'active' : ''}`}
                  onClick={() => selectConversation(conv.id)}
                >
                  <Avatar name={conv.name} size="medium" />
                  
                  <div className="conversation-info-row">
                    <div className="conversation-name-badge-row">
                      <span className="conversation-contact-name">{conv.name}</span>
                      <span className={`conversation-status-badge ${
                        conv.status === 'lead' ? 'lead' : conv.status === 'customer' ? 'customer' : 'guest'
                      }`}>
                        {conv.status}
                      </span>
                    </div>

                    <div className="conversation-msg-preview-row">
                      <span className="conversation-preview-text">{conv.lastMessage}</span>
                      <div className="contacts-controls-left">
                        <span className="conversation-time">{conv.lastMessageTime}</span>
                        {conv.unreadCount > 0 && (
                          <div className="unread-count-bubble">{conv.unreadCount}</div>
                        )}
                      </div>
                    </div>
                  </div>
                </div>
              )
            })
          )}
        </div>
      </div>

      {/* Right Messages chat viewport */}
      <div className="chat-window">
        {activeConversation ? (
          <div className="chat-window-inner-layout">
            {/* Active Header row */}
            <div className="chat-window-header">
              <div className="chat-header-user-info">
                <Avatar name={activeConversation.name} size="medium" />
                <div>
                  <span className="conversation-contact-name">{activeConversation.name}</span>
                  <p className="upload-sub-text margin-zero">{activeConversation.phone}</p>
                </div>
                <span className={`conversation-status-badge ${
                  activeConversation.status === 'lead' ? 'lead' : activeConversation.status === 'customer' ? 'customer' : 'guest'
                }`}>
                  {activeConversation.status}
                </span>
              </div>

              {/* Header icons list */}
              <div className="chat-header-actions">
                <Search size={18} className="chat-header-action-icon" />
                <Info size={18} className="chat-header-action-icon" />
                <MessageSquare size={18} className="chat-header-action-icon whatsapp-green" />
                <MoreVertical size={18} className="chat-header-action-icon" />
              </div>
            </div>

            {/* Chat background messages cards scroll */}
            <div className="chat-messages-container">
              {messages.map((msg, index) => {
                const isIncoming = msg.type === 'incoming'
                const isFailed = msg.status === 'failed'
                
                // Dynamic injection of date dividers mimicking Screenshot 3
                const showDate25 = index === 0
                const showDate7 = msg.id === 4

                return (
                  <div key={msg.id} className="chat-bubble-row">
                    {/* Date Dividers */}
                    {showDate25 && (
                      <div className="chat-date-divider">25-June-2026</div>
                    )}
                    {showDate7 && (
                      <div className="chat-date-divider">7-July-2026</div>
                    )}

                    {/* Chat Bubble card */}
                    <div className={
                      isIncoming 
                        ? 'chat-bubble-incoming' 
                        : isFailed 
                          ? 'chat-bubble-failed' 
                          : 'chat-bubble-outgoing'
                    }>
                      <p className="chat-bubble-text-outgoing">{msg.text}</p>
                      
                      <div className="chat-bubble-time-row">
                        <span className="conversation-time">{msg.time}</span>
                        {!isIncoming && (
                          <span className={`chat-bubble-status-icon ${isFailed ? 'red-warning' : 'blue-ticks'}`}>
                            {isFailed ? <AlertCircle size={12} /> : <CheckCheck size={12} />}
                          </span>
                        )}
                      </div>
                    </div>

                    {/* Red system warning billing notification box */}
                    {msg.errorMessage && (
                      <div className="chat-system-error-text">
                        <span>Message failed to send because your WhatsApp Business account has unsettled payments. Visit </span>
                        <a 
                          href="https://business.facebook.com/billing_hub/accounts/details/?business_id=4537482142127&asset_id=577285893804774&wizard_name=PAY_NOW&account_type=whatsapp-business-account"
                          target="_blank"
                          rel="noopener noreferrer"
                        >
                          business.facebook.com/billing_hub
                        </a>
                        <span> to resolve this issue.</span>
                      </div>
                    )}
                  </div>
                )
              })}
              <div ref={messagesEndRef} />
            </div>

            {/* Chat composer bottom bar */}
            <form onSubmit={handleSend} className="chat-composer-container">
              <div className="chat-composer-input-row">
                <textarea
                  className="chat-composer-textarea"
                  rows={1}
                  placeholder={`Message to ${activeConversation.name} → Shift + Enter for newline, use @ to mention`}
                  value={messageText}
                  onChange={(e) => setMessageText(e.target.value)}
                  onKeyDown={handleKeyDown}
                />
              </div>

              <div className="chat-composer-actions-row">
                {/* Action buttons list */}
                <div className="chat-composer-left-actions">
                  <Smile size={18} className="chat-composer-icon" onClick={() => toast.success('Opening Emoji Picker...')} />
                  <Paperclip size={18} className="chat-composer-icon" onClick={() => toast.success('Opening Attachments...')} />
                  <FileText size={18} className="chat-composer-icon" onClick={() => toast.success('Opening Templates list...')} />
                  <MessageCircle size={18} className="chat-composer-icon" onClick={() => toast.success('Opening Bot Flows...')} />
                </div>

                <button type="submit" className="chat-composer-voice-btn" aria-label="Send Message or Voice">
                  <Mic size={18} />
                </button>
              </div>
            </form>
          </div>
        ) : (
          /* Empty Chat state */
          <div className="chat-empty-state-container">
            <EmptyStateIllustration />
            <span className="chat-empty-state-text">Click user to chat</span>
          </div>
        )}
      </div>

    </div>
  )
}
export default Chat
