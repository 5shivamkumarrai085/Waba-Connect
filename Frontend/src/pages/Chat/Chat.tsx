import React, { useEffect, useMemo, useRef, useState } from 'react'
import toast from 'react-hot-toast'
import { useSearchParams } from 'react-router-dom'
import {
  AlertCircle,
  AlertTriangle,
  Check,
  CheckCheck,
  Clock,
  Clock3,
  FileText,
  Info,
  MessageCircle,
  MessageSquare,
  MoreVertical,
  Paperclip,
  Search,
  Send,
  Smile,
  X,
  User,
  Plus,
  Trash2,
  Calendar,
  Users,
  Phone
} from 'lucide-react'
import { Avatar } from '../../components/Avatar/Avatar'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { useChatStore } from '../../store/chatStore'
import { campaignService } from '../../services/campaigns/campaignService'
import { InitiateChatModal } from '../../components/Modal/InitiateChatModal'
import { apiClient } from '../../services/apiClient'
import type { Message } from '../../types/chat'
import './Chat.css'

const EMOJIS = [
  '😀', '😃', '😄', '😁', '😆', '😅', '😂', '🤣', '😊', '😇',
  '🙂', '🙃', '😉', '😌', '😍', '🥰', '😘', '😗', '😙', '😚',
  '😋', '😛', '😝', '😜', '🤪', '🤨', '🧐', '🤓', '😎', '🥸',
  '🤩', '🥳', '😏', '😒', '😞', '😔', '😟', '😕', '🙁', '☹️',
  '😣', '😖', '😫', '😩', '🥺', '😢', '😭', '😤', '😠', '😡',
  '🤬', '🤯', '😳', '🥵', '🥶', '😱', '😨', '😰', '😥', '😓',
  '🤗', '🤔', '🫣', '🤭', '🫢', '🫡', '🤫', '🫠', '✍️', '👍',
  '👎', '👊', '✊', '🤛', '🤜', '👏', '🙌', '👐', '🤝', '🙏',
  '❤️', '🧡', '💛', '💚', '💙', '💜', '🖤', '🤍', '🤎', '💔',
  '🔥', '✨', '🎉', '🚀', '💡', '💯', '💬', '📞', '🔔', '🔒'
]

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
    deleteActiveConversation,
    setFromNumber,
    setConversationsFilter,
    setSidebarSearchQuery
  } = useChatStore()

  const [messageText, setMessageText] = useState('')
  const [showTimeBanner, setShowTimeBanner] = useState(false)
  const messagesEndRef = useRef<HTMLDivElement>(null)
  const prevMessagesCountRef = useRef(0)
  const prevActiveConvIdRef = useRef<number | null>(null)
  const requestedContactId = Number(searchParams.get('contactId') || 0)

  // Popover & Upload States
  const [showEmojiPicker, setShowEmojiPicker] = useState(false)
  const [showAttachmentMenu, setShowAttachmentMenu] = useState(false)
  const [uploadingMedia, setUploadingMedia] = useState(false)
  const mediaFileInputRef = useRef<HTMLInputElement>(null)
  const [attachmentType, setAttachmentType] = useState<'image' | 'video' | 'document' | null>(null)

  const insertEmoji = (emoji: string) => {
    setMessageText(prev => prev + emoji)
    setShowEmojiPicker(false)
  }

  const triggerMediaUpload = (type: 'image' | 'video' | 'document') => {
    setAttachmentType(type)
    setShowAttachmentMenu(false)
    setTimeout(() => {
      mediaFileInputRef.current?.click()
    }, 50)
  }

  const handleMediaFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (!e.target.files || !e.target.files[0] || !attachmentType) return
    const file = e.target.files[0]
    setUploadingMedia(true)
    try {
      const res = await campaignService.uploadFile(file)
      await sendMessage('', res.url, attachmentType, res.fileName)
      toast.success(`${attachmentType} sent successfully!`)
    } catch (err) {
      console.error(err)
      toast.error('Failed to upload and send attachment.')
    } finally {
      setUploadingMedia(false)
      setAttachmentType(null)
      if (mediaFileInputRef.current) mediaFileInputRef.current.value = ''
    }
  }

  const getAcceptTypes = (type: 'image' | 'video' | 'document' | null) => {
    if (type === 'image') return 'image/*'
    if (type === 'video') return 'video/*'
    if (type === 'document') return 'application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document,text/plain'
    return undefined
  }

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
    const hasConvChanged = activeConversationId !== prevActiveConvIdRef.current
    const hasNewMessage = messages.length > prevMessagesCountRef.current

    if (hasConvChanged || hasNewMessage) {
      messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
    }

    prevMessagesCountRef.current = messages.length
    prevActiveConvIdRef.current = activeConversationId
  }, [messages, activeConversationId])

  useEffect(() => {
    if (showTimeBanner) {
      const timer = setTimeout(() => {
        setShowTimeBanner(false)
      }, 5000)
      return () => clearTimeout(timer)
    }
  }, [showTimeBanner])

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

  // 1. Template Modal
  const [isTemplateModalOpen, setIsTemplateModalOpen] = useState(false)

  // 2. Delete Menu
  const [showDeleteMenu, setShowDeleteMenu] = useState(false)



  // 3. User Info Drawer & Notes
  const [showInfoDrawer, setShowInfoDrawer] = useState(false)
  const [notes, setNotes] = useState<any[]>([])
  const [loadingNotes, setLoadingNotes] = useState(false)
  const [newNoteContent, setNewNoteContent] = useState('')
  const [showAddNoteInput, setShowAddNoteInput] = useState(false)

  // 6. Local Search Bar
  const [showMsgSearch, setShowMsgSearch] = useState(false)
  const [msgSearchQuery, setMsgSearchQuery] = useState('')

  const activeConversation = useMemo(() => {
    return conversations.find(c => c.id === activeConversationId) || null
  }, [conversations, activeConversationId])

  const lastActiveMessage = useMemo(() => {
    for (let i = messages.length - 1; i >= 0; i--) {
      const m = messages[i]
      if (m.type === 'incoming' || (m.type === 'outgoing' && m.isTemplate && m.status !== 'failed')) {
        return m
      }
    }
    return null
  }, [messages])

  const windowStatus = useMemo(() => {
    if (!lastActiveMessage) return { active: false, text: 'No messages exchange yet' }
    
    const lastTime = new Date(lastActiveMessage.createdAt).getTime()
    const limit = lastTime + 24 * 60 * 60 * 1000
    const now = Date.now()
    const remainingMs = limit - now

    if (remainingMs <= 0) {
      return { active: false, text: '24h customer window expired' }
    }

    const hours = Math.floor(remainingMs / (60 * 60 * 1000))
    const minutes = Math.floor((remainingMs % (60 * 60 * 1000)) / (60 * 1000))
    return { 
      active: true, 
      text: `Reply within ${hours} hours and ${minutes} minutes remaining` 
    }
  }, [lastActiveMessage])

  const searchedMessages = useMemo(() => {
    if (!msgSearchQuery.trim()) return messages
    const q = msgSearchQuery.toLowerCase()
    return messages.filter(m => m.text.toLowerCase().includes(q))
  }, [messages, msgSearchQuery])

  // Note CRUD handlers
  const loadNotes = async (contactId: number) => {
    setLoadingNotes(true)
    try {
      const res = await apiClient.get(`/Contacts/${contactId}/notes`)
      setNotes(res.data?.data || [])
    } catch (err) {
      console.error('Failed to load notes', err)
    } finally {
      setLoadingNotes(false)
    }
  }

  const handleAddNote = async () => {
    if (!newNoteContent.trim() || !activeConversation) return
    try {
      const res = await apiClient.post(`/Contacts/${activeConversation.contactId}/notes`, {
        content: newNoteContent.trim()
      })
      if (res.data?.success) {
        setNotes(prev => [res.data.data, ...prev])
        setNewNoteContent('')
        setShowAddNoteInput(false)
        toast.success('Note added successfully')
      }
    } catch (err) {
      console.error(err)
      toast.error('Failed to add note')
    }
  }

  const handleDeleteNote = async (noteId: number) => {
    if (!activeConversation) return
    try {
      const res = await apiClient.delete(`/Contacts/${activeConversation.contactId}/notes/${noteId}`)
      if (res.data?.success) {
        setNotes(prev => prev.filter(n => n.id !== noteId))
        toast.success('Note deleted successfully')
      }
    } catch (err) {
      console.error(err)
      toast.error('Failed to delete note')
    }
  }

  const handleDeleteChat = async () => {
    setShowDeleteMenu(false)
    if (!activeConversationId) return
    
    if (window.confirm("Are you sure you want to delete this chat? This will remove all messages from the database.")) {
      try {
        await deleteActiveConversation()
        toast.success("Chat deleted successfully!")
      } catch (err) {
        console.error(err)
        toast.error("Failed to delete conversation.")
      }
    }
  }

  const renderMessageText = (text: string, search: string) => {
    let cleanText = text
    const attachmentRegex = /\[Attachment:[^\]]+\]\s*/g
    if (cleanText.match(attachmentRegex)) {
      cleanText = cleanText.replace(attachmentRegex, '')
    }

    if (!search.trim()) return cleanText

    const parts = cleanText.split(new RegExp(`(${search})`, 'gi'))
    return (
      <>
        {parts.map((part, i) => 
          part.toLowerCase() === search.toLowerCase() 
            ? <mark key={i} className="search-highlight">{part}</mark> 
            : part
        )}
      </>
    )
  }

  // Load notes when opening drawer
  useEffect(() => {
    if (showInfoDrawer && activeConversation) {
      loadNotes(activeConversation.contactId)
    }
  }, [showInfoDrawer, activeConversationId])

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
            <div className="chat-sidebar-empty-state">
              <MessageSquare size={36} className="chat-sidebar-empty-icon" />
              <span className="chat-sidebar-empty-title">No chats found</span>
              <p className="chat-sidebar-empty-desc">
                {conversationsFilter === 'Unread Chats'
                  ? 'There are no unread chats.'
                  : 'Try adjusting your search query.'}
              </p>
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
                <span title="Search Messages">
                  <Search size={18} className="chat-header-action-icon" onClick={() => setShowMsgSearch(!showMsgSearch)} />
                </span>
                
                {windowStatus.active && (
                  <div 
                    className="chat-header-window-dot active" 
                    title="Click to view time remaining"
                    onClick={() => setShowTimeBanner(true)}
                  />
                )}
                {!windowStatus.active && (
                  <div 
                    className="chat-header-window-dot expired" 
                    title={windowStatus.text}
                  />
                )}

                {activeConversation.assignedTo && (
                  <div className="chat-header-assigned-user" title={`Assigned Member: ${activeConversation.assignedTo}`}>
                    <User size={18} className="chat-header-action-icon assigned-user-icon" />
                  </div>
                )}

                <span title="User Information">
                  <Info size={18} className={`chat-header-action-icon ${showInfoDrawer ? 'active' : ''}`} onClick={() => setShowInfoDrawer(!showInfoDrawer)} />
                </span>
                <span title="Initiate Chat">
                  <MessageSquare size={18} className="chat-header-action-icon whatsapp-green" onClick={() => setIsTemplateModalOpen(true)} />
                </span>
                
                <div className="chat-header-more-menu-wrapper">
                  <MoreVertical size={18} className="chat-header-action-icon" onClick={() => setShowDeleteMenu(!showDeleteMenu)} />
                  {showDeleteMenu && (
                    <div className="chat-header-delete-menu">
                      <button type="button" className="chat-header-delete-btn" onClick={handleDeleteChat}>
                        <Trash2 size={14} />
                        Delete Chat
                      </button>
                    </div>
                  )}
                </div>
              </div>
            </div>

            <div className="chat-window-content-row">
              <div className="chat-window-messages-column">
                {windowStatus.active && showTimeBanner && (
                  <div className="chat-window-time-remaining-banner-floating">
                    <Clock size={14} className="chat-window-time-remaining-icon" />
                    <span>{windowStatus.text}</span>
                  </div>
                )}

                {showMsgSearch && (
                  <div className="chat-window-search-banner-floating">
                    <div className="chat-window-search-input-wrapper">
                      <Search size={16} className="chat-window-search-icon" />
                      <input
                        type="text"
                        className="chat-window-search-input"
                        placeholder="Search Messages..."
                        value={msgSearchQuery}
                        onChange={(e) => setMsgSearchQuery(e.target.value)}
                        autoFocus
                      />
                      {msgSearchQuery && (
                        <X 
                          size={16} 
                          className="chat-window-search-clear-icon" 
                          onClick={() => setMsgSearchQuery('')} 
                        />
                      )}
                    </div>
                    <button 
                      type="button" 
                      className="chat-window-search-close-btn" 
                      onClick={() => { setMsgSearchQuery(''); setShowMsgSearch(false); }}
                    >
                      <X size={18} />
                    </button>
                  </div>
                )}

                <div className="chat-messages-container">
                  {isLoading ? (
                    <div className="chat-thread-loader">
                      <div className="chat-spinner" />
                      <span>Loading messages...</span>
                    </div>
                  ) : searchedMessages.length === 0 ? (
                    <div className="chat-empty-thread">
                      <MessageCircle size={28} />
                      <span>{msgSearchQuery.trim() ? 'No matching messages found' : 'No messages yet'}</span>
                    </div>
                  ) : (
                    searchedMessages.map((message, index) => {
                      const previous = searchedMessages[index - 1]
                      const showDateDivider = shouldShowDateDivider(message, previous)

                      return (
                        <div key={message.id} className="chat-bubble-row">
                          {showDateDivider && (
                            <div className="chat-date-divider">{formatDateDivider(message.createdAt)}</div>
                          )}

                          <div className={getBubbleClass(message)}>
                            {message.mediaUrl && (
                              <div className="chat-bubble-media-wrapper">
                                {message.mediaType === 'image' && (
                                  <img
                                    src={message.mediaUrl}
                                    alt={message.mediaFileName || 'Image'}
                                    className="chat-bubble-media-image"
                                    onClick={() => window.open(message.mediaUrl || undefined, '_blank')}
                                  />
                                )}
                                {message.mediaType === 'video' && (
                                  <video
                                    src={message.mediaUrl}
                                    controls
                                    className="chat-bubble-media-video"
                                  />
                                )}
                                {message.mediaType === 'document' && (
                                  <a
                                    href={message.mediaUrl}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="chat-bubble-media-document-card"
                                  >
                                    <FileText size={24} className="document-card-icon" />
                                    <div className="document-card-info">
                                      <span className="document-card-name" title={message.mediaFileName || undefined}>
                                        {message.mediaFileName || 'document.pdf'}
                                      </span>
                                      <span className="document-card-size">
                                        Click to View/Download
                                      </span>
                                    </div>
                                  </a>
                                )}
                              </div>
                            )}

                            {(!message.mediaUrl || (message.text && message.text.replace(/\[Attachment:[^\]]+\]\s*/g, '').trim().length > 0)) && (
                              <p className="chat-bubble-text-outgoing">
                                {renderMessageText(message.text, msgSearchQuery)}
                              </p>
                            )}

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

                {!windowStatus.active ? (
                  <div className="chat-window-limit-banner">
                    <div className="chat-window-limit-left">
                      <AlertTriangle size={20} className="chat-window-limit-icon" />
                      <div className="chat-window-limit-text-group">
                        <span className="chat-window-limit-title">24 hours limit</span>
                        <span className="chat-window-limit-description">
                          WhatsApp blocks messages 24 hours after the customer last replied.
                        </span>
                      </div>
                    </div>
                    <button 
                      type="button" 
                      className="chat-window-limit-btn"
                      onClick={() => setIsTemplateModalOpen(true)}
                    >
                      <MessageSquare size={16} />
                      <span>Initiate Chat</span>
                    </button>
                  </div>
                ) : (
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
                      <input
                        ref={mediaFileInputRef}
                        type="file"
                        style={{ display: 'none' }}
                        onChange={handleMediaFileChange}
                        accept={getAcceptTypes(attachmentType)}
                      />

                      <div className="chat-composer-left-actions">
                        <div className="chat-composer-popover-anchor">
                          <Smile size={18} className="chat-composer-icon" onClick={() => { setShowEmojiPicker(!showEmojiPicker); setShowAttachmentMenu(false); }} />
                          {showEmojiPicker && (
                            <div className="emoji-picker-popover">
                              {EMOJIS.map(emoji => (
                                <button key={emoji} type="button" className="emoji-btn" onClick={() => insertEmoji(emoji)}>
                                  {emoji}
                                </button>
                              ))}
                            </div>
                          )}
                        </div>

                        <div className="chat-composer-popover-anchor">
                          <Paperclip size={18} className="chat-composer-icon" onClick={() => { setShowAttachmentMenu(!showAttachmentMenu); setShowEmojiPicker(false); }} />
                          {showAttachmentMenu && (
                            <div className="attachment-menu-popover">
                              <button type="button" className="attachment-menu-item" onClick={() => triggerMediaUpload('image')}>
                                Image
                              </button>
                              <button type="button" className="attachment-menu-item" onClick={() => triggerMediaUpload('document')}>
                                Document
                              </button>
                              <button type="button" className="attachment-menu-item" onClick={() => triggerMediaUpload('video')}>
                                Video
                              </button>
                            </div>
                          )}
                        </div>

                        <FileText size={18} className="chat-composer-icon" onClick={() => toast.success('Template picker coming soon')} />
                        <MessageCircle size={18} className="chat-composer-icon" onClick={() => toast.success('Bot flows coming soon')} />
                        
                        {uploadingMedia && (
                          <span className="upload-loading-indicator">Uploading media...</span>
                        )}
                      </div>

                      <button
                        type="submit"
                        className="chat-composer-voice-btn"
                        aria-label="Send message"
                        disabled={(!messageText.trim() && !uploadingMedia) || isSending}
                      >
                        <Send size={18} />
                      </button>
                    </div>
                  </form>
                )}
              </div>

              {showInfoDrawer && (
                <div className="chat-info-drawer">
                  <div className="info-drawer-header">
                    <h3>User Info</h3>
                    <button type="button" className="info-drawer-close" onClick={() => setShowInfoDrawer(false)}>
                      <X size={18} />
                    </button>
                  </div>

                  <div className="info-drawer-body">
                    <div className="info-drawer-user-card">
                      <Avatar name={activeConversation.name} size="large" />
                      <span className="info-drawer-name">{activeConversation.name}</span>
                      <span className={`conversation-status-badge ${normalizeBadge(activeConversation.status)}`}>
                        {activeConversation.status || 'contact'}
                      </span>
                    </div>

                    <div className="info-drawer-section">
                      <h4 className="info-drawer-section-title">Details</h4>
                      <div className="info-details-list">
                        <div className="info-detail-item">
                          <div className="info-detail-label-row">
                            <MessageSquare size={14} className="info-detail-icon text-orange" />
                            <span className="info-detail-label">Source</span>
                            <span className="info-detail-value text-blue inline">{activeConversation.source || 'Unknown'}</span>
                          </div>
                        </div>
                        <div className="info-detail-item">
                          <div className="info-detail-label-row">
                            <Users size={14} className="info-detail-icon text-purple" />
                            <span className="info-detail-label">groups</span>
                          </div>
                          <div className="info-detail-value text-gray block">
                            {activeConversation.contactGroups && activeConversation.contactGroups.length > 0
                              ? activeConversation.contactGroups.join(', ')
                              : 'No groups assigned'}
                          </div>
                        </div>
                        <div className="info-detail-item">
                          <div className="info-detail-label-row">
                            <Calendar size={14} className="info-detail-icon text-sky" />
                            <span className="info-detail-label">Creation Time</span>
                          </div>
                          <div className="info-detail-value text-purple block">
                            {activeConversation.contactCreatedAt 
                              ? new Date(activeConversation.contactCreatedAt).toLocaleString() 
                              : '-'}
                          </div>
                        </div>
                        <div className="info-detail-item">
                          <div className="info-detail-label-row">
                            <Clock3 size={14} className="info-detail-icon text-amber" />
                            <span className="info-detail-label">Last Activity</span>
                          </div>
                          <div className="info-detail-value text-purple block">
                            {activeConversation.lastMessageAt 
                              ? new Date(activeConversation.lastMessageAt).toLocaleString() 
                              : activeConversation.lastMessageTime || '-'}
                          </div>
                        </div>
                        <div className="info-detail-item">
                          <div className="info-detail-label-row">
                            <Phone size={14} className="info-detail-icon text-green" />
                            <span className="info-detail-label">Phone</span>
                            <span className="info-detail-value text-blue inline">{activeConversation.phone}</span>
                          </div>
                        </div>
                      </div>
                    </div>

                    <div className="info-drawer-section">
                      <div className="info-drawer-section-header">
                        <h4 className="info-drawer-section-title">Notes</h4>
                        <button type="button" className="add-note-btn" onClick={() => setShowAddNoteInput(!showAddNoteInput)}>
                          <Plus size={16} />
                        </button>
                      </div>

                      {showAddNoteInput && (
                        <div className="add-note-input-container">
                          <textarea
                            className="form-control note-textarea"
                            placeholder="Write a note..."
                            value={newNoteContent}
                            onChange={(e) => setNewNoteContent(e.target.value)}
                            rows={3}
                          />
                          <div className="note-input-actions">
                            <button type="button" className="btn btn-sm btn-light" onClick={() => setShowAddNoteInput(false)}>
                              Cancel
                            </button>
                            <button type="button" className="btn btn-sm btn-primary" onClick={handleAddNote}>
                              Save
                            </button>
                          </div>
                        </div>
                      )}

                      {loadingNotes ? (
                        <p className="loading-notes-text">Loading notes...</p>
                      ) : notes.length === 0 ? (
                        <p className="no-notes-text">No notes yet</p>
                      ) : (
                        <div className="notes-list">
                          {notes.map(note => (
                            <div key={note.id} className="note-item">
                              <div className="note-item-header">
                                <span className="note-date">
                                  {new Date(note.createdAt).toLocaleDateString()}
                                </span>
                                <button type="button" className="delete-note-btn" onClick={() => handleDeleteNote(note.id)}>
                                  <Trash2 size={12} />
                                </button>
                              </div>
                              <p className="note-content">{note.content}</p>
                            </div>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              )}
            </div>

            <InitiateChatModal
              isOpen={isTemplateModalOpen}
              onClose={() => setIsTemplateModalOpen(false)}
              contact={{
                id: activeConversation.contactId,
                name: activeConversation.name,
                phone: activeConversation.phone
              }}
              onSuccess={() => {
                setIsTemplateModalOpen(false)
                void refreshActiveMessages()
              }}
            />
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
