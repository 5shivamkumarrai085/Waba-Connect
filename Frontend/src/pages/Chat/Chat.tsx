import React, { useEffect, useMemo, useRef, useState } from 'react'
import toast from 'react-hot-toast'
import { useSearchParams } from 'react-router-dom'
import {
  AlertCircle,
  Check,
  CheckCheck,
  Clock3,
  FileText,
  Info,
  MessageCircle,
  MessageSquare,
  MoreVertical,
  Paperclip,
  Search,
  Send,
  Smile
} from 'lucide-react'
import { Avatar } from '../../components/Avatar/Avatar'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { useChatStore } from '../../store/chatStore'
import type { Message } from '../../types/chat'
import './Chat.css'

export const Chat: React.FC = () => {
  const [searchParams] = useSearchParams()
  const {
    accounts,
    conversations,
    activeConversationId,
    messages,
    isLoading,
    isSending,
    fromNumber,
    conversationsFilter,
    sidebarSearchQuery,
    loadAccounts,
    loadConversations,
    refreshActiveMessages,
    selectConversation,
    sendMessage,
    setFromNumber,
    setConversationsFilter,
    setSidebarSearchQuery
  } = useChatStore()

  const [messageText, setMessageText] = useState('')
  const messagesEndRef = useRef<HTMLDivElement>(null)
  const requestedContactId = Number(searchParams.get('contactId') || 0)

  useEffect(() => {
    loadAccounts()
    loadConversations()
  }, [loadAccounts, loadConversations])

  useEffect(() => {
    if (accounts.length > 0) return

    const interval = window.setInterval(() => {
      loadAccounts()
    }, 5000)

    return () => window.clearInterval(interval)
  }, [accounts.length, loadAccounts])

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      loadConversations()
    }, 250)

    return () => window.clearTimeout(timeout)
  }, [sidebarSearchQuery, conversationsFilter, loadConversations])

  useEffect(() => {
    const interval = window.setInterval(() => {
      if (activeConversationId) {
        refreshActiveMessages()
      } else {
        loadConversations()
      }
    }, 2000)

    return () => window.clearInterval(interval)
  }, [activeConversationId, refreshActiveMessages, loadConversations])

  useEffect(() => {
    if (!requestedContactId || conversations.length === 0) return

    const requestedConversation = conversations.find((conversation) => conversation.contactId === requestedContactId)
    if (requestedConversation && requestedConversation.id !== activeConversationId) {
      void selectConversation(requestedConversation.id)
    }
  }, [activeConversationId, conversations, requestedContactId, selectConversation])

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const filteredConversations = useMemo(() => {
    return conversations.filter((conversation) => {
      if (sidebarSearchQuery) {
        const q = sidebarSearchQuery.toLowerCase()
        if (!conversation.name.toLowerCase().includes(q) && !conversation.phone.includes(q)) return false
      }

      if (conversationsFilter === 'Unread Chats' && conversation.unreadCount === 0) {
        return false
      }

      return true
    })
  }, [conversations, conversationsFilter, sidebarSearchQuery])

  const activeConversation = conversations.find(c => c.id === activeConversationId)
  const selectedAccount = accounts.find(account => account.phoneNumberId === fromNumber)

  const sendCurrentMessage = async () => {
    const text = messageText.trim()
    if (!text || isSending) return

    setMessageText('')
    await sendMessage(text)
  }

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault()
    await sendCurrentMessage()
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void sendCurrentMessage()
    }
  }

  const EmptyStateIllustration = () => (
    <svg className="chat-empty-state-illustration" viewBox="0 0 200 200" fill="none" xmlns="http://www.w3.org/2000/svg">
      <rect x="65" y="20" width="70" height="140" rx="12" fill="#E2E8F0" stroke="#94A3B8" strokeWidth="3" />
      <line x1="90" y1="26" x2="110" y2="26" stroke="#94A3B8" strokeWidth="2" strokeLinecap="round" />
      <circle cx="100" cy="150" r="5" fill="#94A3B8" />
      <rect x="25" y="50" width="35" height="15" rx="6" fill="#D9FDD3" stroke="#A7F3D0" />
      <rect x="30" y="55" width="20" height="2" rx="1" fill="#047857" opacity="0.3" />
      <rect x="30" y="60" width="10" height="2" rx="1" fill="#047857" opacity="0.3" />
      <line x1="57" y1="62" x2="65" y2="65" stroke="#A7F3D0" />
      <rect x="140" y="80" width="35" height="15" rx="6" fill="#FFFFFF" stroke="#CBD5E1" />
      <rect x="145" y="85" width="20" height="2" rx="1" fill="#475569" opacity="0.2" />
      <rect x="145" y="90" width="15" height="2" rx="1" fill="#475569" opacity="0.2" />
      <line x1="140" y1="92" x2="135" y2="95" stroke="#CBD5E1" />
    </svg>
  )

  return (
    <div className="fade-in chat-container-layout">
      <div className="chat-sidebar">
        <div className="chat-sidebar-header">
          <div className="chat-account-display-row">
            <Avatar name={selectedAccount?.verifiedName || selectedAccount?.phoneNumber || 'From Account'} size="small" />
            <div className="chat-dropdown-full">
              <span className="upload-sub-text">From:</span>
              <select
                className="form-control"
                value={fromNumber}
                onChange={(e) => setFromNumber(e.target.value)}
                disabled={accounts.length === 0}
              >
                {accounts.length === 0 ? (
                  <option value="">No WABA numbers connected</option>
                ) : (
                  accounts.map((account) => (
                    <option key={account.phoneNumberId} value={account.phoneNumberId}>
                      {account.phoneNumber || account.verifiedName || account.phoneNumberId}
                    </option>
                  ))
                )}
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

        <div className="chat-sidebar-search">
          <SearchBar
            value={sidebarSearchQuery}
            onChange={setSidebarSearchQuery}
            placeholder="Searching..."
          />
        </div>

        <div className="conversation-list-scroll">
          {isLoading && conversations.length === 0 ? (
            <div className="page-loader">
              <p className="upload-sub-text">Loading chats...</p>
            </div>
          ) : filteredConversations.length === 0 ? (
            <div className="data-table-empty">
              <p className="upload-sub-text">No chats found</p>
            </div>
          ) : (
            filteredConversations.map((conversation) => {
              const isActive = conversation.id === activeConversationId
              return (
                <button
                  key={conversation.id}
                  type="button"
                  className={`conversation-item ${isActive ? 'active' : ''}`}
                  onClick={() => selectConversation(conversation.id)}
                >
                  <Avatar name={conversation.name} size="medium" />
                  <div className="conversation-info-row">
                    <div className="conversation-name-badge-row">
                      <span className="conversation-contact-name">{conversation.name}</span>
                      <span className={`conversation-status-badge ${normalizeBadge(conversation.status)}`}>
                        {conversation.status || 'contact'}
                      </span>
                    </div>
                    <div className="conversation-msg-preview-row">
                      <span className="conversation-preview-text">{conversation.lastMessage || 'No messages yet'}</span>
                      <div className="contacts-controls-left">
                        <span className="conversation-time">{conversation.lastMessageTime}</span>
                        {conversation.unreadCount > 0 && (
                          <div className="unread-count-bubble">{conversation.unreadCount}</div>
                        )}
                      </div>
                    </div>
                  </div>
                </button>
              )
            })
          )}
        </div>
      </div>

      <div className="chat-window">
        {activeConversation ? (
          <div className="chat-window-inner-layout">
            <div className="chat-window-header">
              <div className="chat-header-user-info">
                <Avatar name={activeConversation.name} size="medium" />
                <div>
                  <span className="conversation-contact-name">{activeConversation.name}</span>
                  <p className="upload-sub-text margin-zero">{activeConversation.phone}</p>
                </div>
                <span className={`conversation-status-badge ${normalizeBadge(activeConversation.status)}`}>
                  {activeConversation.status || 'contact'}
                </span>
              </div>

              <div className="chat-header-actions">
                <Search size={18} className="chat-header-action-icon" />
                <Info size={18} className="chat-header-action-icon" />
                <MessageSquare size={18} className="chat-header-action-icon whatsapp-green" />
                <MoreVertical size={18} className="chat-header-action-icon" />
              </div>
            </div>

            <div className="chat-messages-container">
              {messages.length === 0 ? (
                <div className="chat-empty-thread">
                  <MessageCircle size={28} />
                  <span>No messages yet</span>
                </div>
              ) : (
                messages.map((message, index) => {
                  const previous = messages[index - 1]
                  const showDateDivider = shouldShowDateDivider(message, previous)

                  return (
                    <div key={message.id} className="chat-bubble-row">
                      {showDateDivider && (
                        <div className="chat-date-divider">{formatDateDivider(message.createdAt)}</div>
                      )}

                      <div className={getBubbleClass(message)}>
                        <p className="chat-bubble-text-outgoing">{message.text}</p>
                        <div className="chat-bubble-time-row">
                          <span className="conversation-time">{message.time}</span>
                          {message.type === 'outgoing' && (
                            <span
                              className={`chat-bubble-status-icon ${getMessageStatusClass(message.status)}`}
                              title={getMessageStatusTitle(message)}
                            >
                              {getStatusIcon(message)}
                            </span>
                          )}
                        </div>
                      </div>

                      {message.errorMessage && (
                        <div className="chat-system-error-text">
                          {message.errorMessage}
                        </div>
                      )}
                    </div>
                  )
                })
              )}
              <div ref={messagesEndRef} />
            </div>

            <form onSubmit={handleSend} className="chat-composer-container">
              <div className="chat-composer-input-row">
                <textarea
                  className="chat-composer-textarea"
                  rows={1}
                  placeholder={`Message to ${activeConversation.name} - Shift + Enter for newline`}
                  value={messageText}
                  onChange={(e) => setMessageText(e.target.value)}
                  onKeyDown={handleKeyDown}
                  disabled={isSending}
                />
              </div>

              <div className="chat-composer-actions-row">
                <div className="chat-composer-left-actions">
                  <Smile size={18} className="chat-composer-icon" onClick={() => toast.success('Emoji picker coming soon')} />
                  <Paperclip size={18} className="chat-composer-icon" onClick={() => toast.success('Attachments coming soon')} />
                  <FileText size={18} className="chat-composer-icon" onClick={() => toast.success('Template picker coming soon')} />
                  <MessageCircle size={18} className="chat-composer-icon" onClick={() => toast.success('Bot flows coming soon')} />
                </div>

                <button
                  type="submit"
                  className="chat-composer-voice-btn"
                  aria-label="Send message"
                  disabled={!messageText.trim() || isSending}
                >
                  <Send size={18} />
                </button>
              </div>
            </form>
          </div>
        ) : (
          <div className="chat-empty-state-container">
            <EmptyStateIllustration />
            <span className="chat-empty-state-text">Click user to chat</span>
          </div>
        )}
      </div>
    </div>
  )
}

const normalizeBadge = (status: string) => {
  const lower = status?.toLowerCase()
  if (lower === 'lead') return 'lead'
  if (lower === 'customer') return 'customer'
  return 'guest'
}

const getBubbleClass = (message: Message) => {
  if (message.type === 'incoming') return 'chat-bubble-incoming'
  if (message.type === 'system' || message.status === 'failed') return 'chat-bubble-failed'
  return 'chat-bubble-outgoing'
}

const getMessageStatusClass = (status?: string) => {
  if (status === 'failed') return 'red-warning'
  if (status === 'read') return 'blue-ticks'
  if (status === 'sending') return 'pending-clock'
  if (status === 'pending') return 'pending-clock'
  return 'sent-ticks'
}

const getStatusIcon = (message: Message) => {
  switch (message.status) {
    case 'failed':
      return <AlertCircle size={12} />
    case 'sending':
      return <Clock3 size={12} />
    case 'pending':
      return <Clock3 size={12} />
    case 'sent':
      return <Check size={12} />
    case 'delivered':
    case 'read':
      return <CheckCheck size={12} />
    default:
      return <Clock3 size={12} />
  }
}

const getMessageStatusTitle = (message: Message) => {
  switch (message.status) {
    case 'sending':
      return 'Sending to Meta...'
    case 'pending':
      return message.whatsAppMessageId
        ? 'Accepted by Meta. Waiting for WhatsApp delivery webhook.'
        : 'Waiting for Meta response.'
    case 'sent':
      return 'Sent by Meta.'
    case 'delivered':
      return 'Delivered on WhatsApp.'
    case 'read':
      return 'Read on WhatsApp.'
    case 'failed':
      return message.errorMessage || 'Message failed.'
    default:
      return 'Message status pending.'
  }
}

const shouldShowDateDivider = (message: Message, previous?: Message) => {
  if (!previous) return true
  return new Date(message.createdAt).toDateString() !== new Date(previous.createdAt).toDateString()
}

const formatDateDivider = (value: string) => {
  return new Intl.DateTimeFormat('en', {
    day: '2-digit',
    month: 'long',
    year: 'numeric'
  }).format(new Date(value))
}

export default Chat
