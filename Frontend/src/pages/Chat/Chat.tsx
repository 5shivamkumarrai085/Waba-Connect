import React, { useEffect, useMemo, useRef, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import toast from 'react-hot-toast'
import { useSearchParams, useNavigate } from 'react-router-dom'
import { aiReplyService, type CannedReply } from '../../services/setup/aiReplyService'
import {
  AlertCircle,
  AlertTriangle,
  Check,
  CheckCheck,
  Clock,
  Clock3,
  FileText,
  MessageSquareReply,
  Info,
  Link2,
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
  Phone,
  Lock
} from 'lucide-react'
import { Avatar } from '../../components/Avatar/Avatar'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { useChatStore } from '../../store/chatStore'
import { useConnectionStore } from '../../store/connectionStore'
import { campaignService } from '../../services/campaigns/campaignService'
import { InitiateChatModal } from '../../components/Modal/InitiateChatModal'
import { apiClient } from '../../services/apiClient'
import type { Message } from '../../types/chat'
import './Chat.css'
import Can from '../../components/Can/Can'
import usePermission from '../../hooks/usePermission'
import { resolveMediaUrl } from '../../utils/mediaUrl'
import { buildLookupMap, resolveLookup, badgeStyleFor, type ResolvedLookup } from '../../utils/lookupColors'
import { contactService } from '../../services/contacts/contactService'
import type { ContactType } from '../../types/contacts'
import { matchesSearch } from '../../utils/smartSearch'
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'

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

// Chat had the only working copy of this logic. It now lives in utils/mediaUrl so avatars and
// any future backend-served media resolve the same way instead of each site re-deriving it.
const getFullMediaUrl = resolveMediaUrl

export const Chat: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams()
  const navigate = useNavigate()
  const { has } = usePermission()
  const canSend = has('Chat.Send')
  const canDelete = has('Chat.Delete')

  // WhatsApp-style message selection. `msgMenu` holds the right-click target and where to
  // anchor the menu; `selectionMode` switches the thread into multi-select.
  // Contact types, fetched once. Resolving the badge client-side keeps the join off the
  // conversation query, which is the hottest read in the app.
  const [contactTypes, setContactTypes] = useState<ContactType[]>([])
  const typeMap = useMemo(() => buildLookupMap(contactTypes), [contactTypes])

  useEffect(() => {
    void (async () => {
      try {
        setContactTypes(await contactService.getContactTypes())
      } catch {
        // A failed lookup only costs the badge its colour; the conversation list still renders.
      }
    })()
  }, [])

  const [msgMenu, setMsgMenu] = useState<{ id: number; x: number; y: number } | null>(null)
  const [selectionMode, setSelectionMode] = useState(false)
  const [selectedMessageIds, setSelectedMessageIds] = useState<number[]>([])
  const [confirmDeleteMessages, setConfirmDeleteMessages] = useState(false)
  const longPressTimer = useRef<number | null>(null)
  const { connections, fetchDashboard: fetchConnectionDashboard } = useConnectionStore()
  const {
    accounts,
    conversations,
    activeConversationId,
    selectedConnectionId,
    messages,
    isLoading,
    isLoadingConversations,
    isSending,
    fromNumber,
    conversationsFilter,
    sidebarSearchQuery,
    setSelectedConnectionId,
    loadAccounts,
    loadConversations,
    refreshActiveMessages,
    selectConversation,
    sendMessage,
    deleteActiveConversation,
    deleteMessages,
    setFromNumber,
    setConversationsFilter,
    setSidebarSearchQuery
  } = useChatStore()


  const [messageText, setMessageText] = useState('')
  const [showTimeBanner, setShowTimeBanner] = useState(false)
  const messagesEndRef = useRef<HTMLDivElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const prevMessagesCountRef = useRef(0)
  const prevActiveConvIdRef = useRef<number | null>(null)
  const requestedContactId = Number(searchParams.get('contactId') || 0)

  const selectedConnection = connections.find(c => c.id === selectedConnectionId)
  const isSelectedConnectionDisconnected = selectedConnection ? !selectedConnection.isConnected : false

  // Popover & Upload States
  const [showEmojiPicker, setShowEmojiPicker] = useState(false)
  const [showCannedReplies, setShowCannedReplies] = useState(false)
  const [cannedReplies, setCannedReplies] = useState<CannedReply[]>([])
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

  // Auto-select the first connected connection on load
  useEffect(() => {
    fetchConnectionDashboard()
    loadAccounts()
    loadConversations()
  }, [fetchConnectionDashboard, loadAccounts, loadConversations])

  useEffect(() => {
    if (connections.length === 0) return

    const current = connections.find(c => c.id === selectedConnectionId)
    const firstUsable = connections.find(c => c.isConnected && c.phoneNumber)

    // A selection is only worth keeping if it can actually carry a message. The check used to be
    // "does this connection still exist", which meant a remembered connection that had lost its
    // sender number kept the whole screen in "Setup pending" — with a working line sitting one
    // item down the dropdown, unselected. Existing-but-unusable is not a selection worth honouring.
    const currentIsUsable = Boolean(current?.isConnected && current?.phoneNumber)
    if (currentIsUsable) return

    // Only moved when there is somewhere better to go: if nothing can send, the remembered choice
    // stays put rather than shuffling between equally broken lines on every render.
    if (current && !firstUsable) return

    setSelectedConnectionId(firstUsable ? firstUsable.id : connections[0].id)
  }, [connections, selectedConnectionId, setSelectedConnectionId])

  // Accounts change only when someone connects or disconnects a WABA, which the connection
  // store already triggers a reload for. Retry a few times on a cold start, then stop —
  // this used to poll indefinitely whenever the list came back empty.
  useEffect(() => {
    if (accounts.length > 0) return

    let attempts = 0
    const interval = window.setInterval(() => {
      if (attempts >= ACCOUNT_RETRY_LIMIT) {
        window.clearInterval(interval)
        return
      }
      attempts += 1
      loadAccounts()
    }, ACCOUNT_RETRY_MS)

    return () => window.clearInterval(interval)
  }, [accounts.length, loadAccounts])

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      loadConversations()
    }, 250)

    return () => window.clearTimeout(timeout)
  }, [sidebarSearchQuery, conversationsFilter, loadConversations])

  // Two independent streams at different cadences, replacing a single 2-second timer that
  // refreshed whichever was in front. The open conversation is small and cheap, so it stays
  // near-live; the conversation list is the expensive one, so it ticks slowly.
  //
  // Both pause when the tab is hidden — a background tab polling every 2 seconds was a large
  // share of the load for no one's benefit — and catch up immediately on return.
  useEffect(() => {
    const selectedConn = connections.find(c => c.id === selectedConnectionId)
    if (selectedConn && !selectedConn.phoneNumber) return
    if (!activeConversationId) return

    const tick = () => {
      if (document.visibilityState === 'visible') refreshActiveMessages()
    }

    const interval = window.setInterval(tick, ACTIVE_THREAD_POLL_MS)
    document.addEventListener('visibilitychange', tick)

    return () => {
      window.clearInterval(interval)
      document.removeEventListener('visibilitychange', tick)
    }
  }, [activeConversationId, refreshActiveMessages, connections, selectedConnectionId])

  useEffect(() => {
    const selectedConn = connections.find(c => c.id === selectedConnectionId)
    if (selectedConn && !selectedConn.phoneNumber) return

    const tick = () => {
      if (document.visibilityState === 'visible') loadConversations()
    }

    const interval = window.setInterval(tick, INBOX_POLL_MS)
    document.addEventListener('visibilitychange', tick)

    return () => {
      window.clearInterval(interval)
      document.removeEventListener('visibilitychange', tick)
    }
  }, [loadConversations, connections, selectedConnectionId])

  // "Latest requested contact wins, exactly once" guard. Without this, since the
  // ?contactId= param was never cleared, ANY change to activeConversationId (including
  // the user manually clicking a different conversation, which itself changes
  // activeConversationId) re-ran this effect and snapped the UI straight back to the
  // originally-requested contact — the URL param, not the user, always won.
  // Storing the *consumed id itself* (not a plain boolean) means a fresh, different
  // ?contactId= naturally re-arms the guard with no separate reset effect needed.
  const consumedContactIdRef = useRef<number>(0)

  useEffect(() => {
    if (!requestedContactId || conversations.length === 0) return
    if (consumedContactIdRef.current === requestedContactId) return

    const requestedConversation = conversations.find((conversation) => conversation.contactId === requestedContactId)
    if (!requestedConversation) return

    consumedContactIdRef.current = requestedContactId
    if (requestedConversation.id !== activeConversationId) {
      void selectConversation(requestedConversation.id)
    }

    // Strip the param immediately so no later activeConversationId change (nor a
    // remount/refresh) can re-trigger the auto-select.
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev)
      next.delete('contactId')
      return next
    }, { replace: true })
  }, [activeConversationId, conversations, requestedContactId, selectConversation, setSearchParams])

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

  // The daily message-limit cache is no longer warmed here. This effect fired on every
  // connection change and made blocking Meta Graph calls that nothing on this page consumed;
  // the limit is checked at send time via checkLimitFast, which fetches on a cache miss.

  // 1. Template Modal
  const [isTemplateModalOpen, setIsTemplateModalOpen] = useState(false)

  const handleOpenTemplateModal = () => {
    if (activeConversation && activeConversation.contactIsActive === false) {
      toast.error('Cannot send message to an inactive contact.', { duration: 3000 })
      return
    }
    setIsTemplateModalOpen(true)
  }

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

  // Message search inside the open conversation. The text is what gets highlighted, but an
  // attachment is found by its filename and a failed send by its error -- neither of which lives
  // in the text -- so both are matched too.
  const searchedMessages = useMemo(() => {
    if (!msgSearchQuery.trim()) return messages
    return messages.filter((m) =>
      matchesSearch(msgSearchQuery, [m.text, m.mediaFileName, m.mediaType, m.errorMessage])
    )
  }, [messages, msgSearchQuery])

  // Note CRUD handlers
  const loadNotes = async (contactId: number) => {
    setLoadingNotes(true)
    try {
      const res = await apiClient.get(`/Contacts/${contactId}/notes`)
      setNotes(res.data?.data || [])
    } catch (err) {
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
      toast.error('Failed to delete note')
    }
  }

  const [showDeleteChatModal, setShowDeleteChatModal] = useState(false)

  const handleDeleteChat = () => {
    setShowDeleteMenu(false)
    if (!activeConversationId) return
    setShowDeleteChatModal(true)
  }

  const confirmDeleteChat = async () => {
    setShowDeleteChatModal(false)
    if (!activeConversationId) return
    try {
      await deleteActiveConversation()
      toast.success("Chat deleted successfully!")
    } catch (err) {
      toast.error("Failed to delete conversation.")
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

  const renderRichMessageContent = (text: string, searchQuery: string) => {
    if (!text) return '';
    
    if (text.startsWith('[Location|') && text.endsWith(']')) {
      const parts = text.slice(10, -1).split('|');
      const locationData: Record<string, string> = {};
      parts.forEach(p => {
        const idx = p.indexOf(':');
        if (idx !== -1) {
          const key = p.substring(0, idx).trim();
          const val = p.substring(idx + 1).trim();
          locationData[key] = val;
        }
      });

      const name = locationData['Name'] || 'Location Shared';
      const addr = locationData['Addr'] || '';
      const lat = locationData['Lat'] || '';
      const lng = locationData['Lng'] || '';
      const mapUrl = (lat && lng) 
        ? `https://www.google.com/maps/search/?api=1&query=${lat},${lng}`
        : `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(name + ' ' + addr)}`;

      return (
        <div className="chat-rich-location-card" style={{ padding: '4px', minWidth: '200px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '6px' }}>
            <span style={{ fontSize: '18px' }}>📍</span>
            <div style={{ display: 'flex', flexDirection: 'column', textAlign: 'left' }}>
              <span style={{ fontSize: '13px', fontWeight: 700, color: '#1f2937' }}>{name}</span>
              {addr && <span style={{ fontSize: '11px', color: '#4b5563' }}>{addr}</span>}
            </div>
          </div>
          <a 
            href={mapUrl} 
            target="_blank" 
            rel="noopener noreferrer" 
            style={{ 
              display: 'block', 
              textAlign: 'center', 
              backgroundColor: '#10b981', 
              color: 'white', 
              fontSize: '11.5px', 
              fontWeight: 600, 
              padding: '6px 12px', 
              borderRadius: '4px', 
              textDecoration: 'none',
              marginTop: '8px'
            }}
          >
            View on Google Maps
          </a>
        </div>
      );
    }

    if (text.startsWith('[ContactCard|') && text.endsWith(']')) {
      const parts = text.slice(13, -1).split('|');
      const contactData: Record<string, string> = {};
      parts.forEach(p => {
        const idx = p.indexOf(':');
        if (idx !== -1) {
          const key = p.substring(0, idx).trim();
          const val = p.substring(idx + 1).trim();
          contactData[key] = val;
        }
      });

      const name = contactData['Name'] || 'Contact Shared';
      const phone = contactData['Phone'] || '';
      const email = contactData['Email'] || '';
      const org = contactData['Org'] || '';

      return (
        <div className="chat-rich-contact-card" style={{ padding: '4px', minWidth: '200px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px', borderBottom: '1px solid #e5e7eb', paddingBottom: '6px', textAlign: 'left' }}>
            <span style={{ fontSize: '20px' }}>👤</span>
            <div style={{ display: 'flex', flexDirection: 'column' }}>
              <span style={{ fontSize: '13px', fontWeight: 700, color: '#1f2937' }}>{name}</span>
              {org && org.trim() !== '-' && <span style={{ fontSize: '11px', color: '#4b5563' }}>{org}</span>}
            </div>
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', fontSize: '11px', color: '#374151', textAlign: 'left' }}>
            {phone && <div><strong>Phone:</strong> {phone}</div>}
            {email && <div><strong>Email:</strong> {email}</div>}
          </div>
        </div>
      );
    }

    return renderMessageText(text, searchQuery);
  };

  // Load notes when opening drawer
  useEffect(() => {
    if (showInfoDrawer && activeConversationId) {
      const conv = conversations.find(c => c.id === activeConversationId)
      if (conv) {
        loadNotes(conv.contactId)
      }
    }
  }, [showInfoDrawer, activeConversationId, conversations])

  const selectedAccount = accounts.find(account => account.phoneNumberId === fromNumber)

  const sendCurrentMessage = async () => {
    const text = messageText.trim()
    if (!text || isSending) return

    if (activeConversation && activeConversation.contactIsActive === false) {
      toast.error('Cannot send message to an inactive contact.', { duration: 3000 })
      return
    }

    setMessageText('')
    await sendMessage(text)
  }

  // --- Message selection & deletion (WhatsApp-style) -------------------------------------

  const exitSelection = () => {
    setSelectionMode(false)
    setSelectedMessageIds([])
    setMsgMenu(null)
  }

  // Leaving a conversation must drop the selection: keeping ids across a switch would let a
  // confirmed delete hit messages the user is no longer looking at.
  useEffect(() => {
    exitSelection()
  }, [activeConversationId])

  const toggleMessageSelected = (id: number) => {
    setSelectedMessageIds((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]
    )
  }

  const openMessageMenu = (event: React.MouseEvent, id: number) => {
    if (!canDelete) return
    event.preventDefault()
    setMsgMenu({ id, x: event.clientX, y: event.clientY })
  }

  // Touch has no right-click, so a ~500ms press opens the same menu. The timer is cleared on
  // move as well as release, or scrolling the thread would trigger it.
  const startLongPress = (event: React.TouchEvent, id: number) => {
    if (!canDelete) return
    const touch = event.touches[0]
    longPressTimer.current = window.setTimeout(() => {
      setMsgMenu({ id, x: touch.clientX, y: touch.clientY })
    }, 500)
  }

  const cancelLongPress = () => {
    if (longPressTimer.current !== null) {
      window.clearTimeout(longPressTimer.current)
      longPressTimer.current = null
    }
  }

  useEffect(() => cancelLongPress, [])

  // Any click outside the menu dismisses it, matching every other popover in the app.
  useEffect(() => {
    if (!msgMenu) return
    const dismiss = () => setMsgMenu(null)
    document.addEventListener('click', dismiss)
    document.addEventListener('scroll', dismiss, true)
    return () => {
      document.removeEventListener('click', dismiss)
      document.removeEventListener('scroll', dismiss, true)
    }
  }, [msgMenu])

  const handleConfirmDeleteMessages = async () => {
    try {
      await deleteMessages(selectedMessageIds)
      toast.success(
        selectedMessageIds.length === 1
          ? 'Message deleted.'
          : `${selectedMessageIds.length} messages deleted.`
      )
      exitSelection()
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Could not delete the messages.')
    } finally {
      setConfirmDeleteMessages(false)
    }
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

  // Auto-resize textarea like WhatsApp
  useEffect(() => {
    const textarea = textareaRef.current
    if (textarea) {
      textarea.style.height = 'auto'
      textarea.style.height = Math.min(textarea.scrollHeight, 200) + 'px'
    }
  }, [messageText])

  // Close all popovers on Escape key or click outside
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setShowEmojiPicker(false)
        setShowAttachmentMenu(false)
        setShowDeleteMenu(false)
        setShowMsgSearch(false)
        setShowTimeBanner(false)
        setShowDeleteChatModal(false)
        setIsTemplateModalOpen(false)
      }
    }

    const handleClickOutside = (e: MouseEvent) => {
      const target = e.target as HTMLElement
      if (showEmojiPicker && !target.closest('.chat-composer-popover-anchor')) {
        setShowEmojiPicker(false)
      }
      if (showCannedReplies && !target.closest('.chat-composer-popover-anchor')) {
        setShowCannedReplies(false)
      }
      if (showAttachmentMenu && !target.closest('.chat-composer-popover-anchor')) {
        setShowAttachmentMenu(false)
      }
      if (showDeleteMenu && !target.closest('.chat-header-more-menu-wrapper')) {
        setShowDeleteMenu(false)
      }
    }

    document.addEventListener('keydown', handleEscape)
    document.addEventListener('mousedown', handleClickOutside)
    return () => {
      document.removeEventListener('keydown', handleEscape)
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [showEmojiPicker, showAttachmentMenu, showDeleteMenu, showCannedReplies])

  // Loaded once. activeOnly=true so the composer only offers replies that are switched on —
  // the management list is where inactive ones remain visible.
  useEffect(() => {
    void aiReplyService.getCannedReplies(true).then(setCannedReplies)
  }, [])

  /**
   * Inserts a canned reply at the caret rather than replacing the box, so an agent can type a
   * greeting, drop in a saved paragraph, and keep going.
   */
  const insertCannedReply = (text: string) => {
    const textarea = textareaRef.current
    const start = textarea?.selectionStart ?? messageText.length
    const end = textarea?.selectionEnd ?? messageText.length

    const next = messageText.slice(0, start) + text + messageText.slice(end)
    setMessageText(next)
    setShowCannedReplies(false)

    // Restore focus and put the caret after the inserted text, once React has committed.
    requestAnimationFrame(() => {
      textarea?.focus()
      const caret = start + text.length
      textarea?.setSelectionRange(caret, caret)
    })
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
    <motion.div className="chat-container-layout" {...pageTransitionProps}>
      <div className="chat-sidebar">
        <div className="chat-sidebar-header">
          {/* Connection Filter Dropdown (matching Image 2) */}
          <div className="chat-connection-select-wrapper">
            <label className="chat-sidebar-field-label">
              Active Connection
            </label>
            <SearchableSelect
              label="Active connection"
              placeholder="Select a connection"
              hideAllOption
              className="chat-connection-select"
              value={selectedConnectionId != null ? String(selectedConnectionId) : ''}
              options={connections.map((conn) => ({
                value: String(conn.id),
                label: `${conn.name} (${conn.phoneNumber || 'Setup pending'})`,
                keywords: conn.phoneNumber ?? ''
              }))}
              onChange={(val) => setSelectedConnectionId(val ? Number(val) : null)}
            />
          </div>

          <div className="chat-account-display-row">
            <Avatar name={selectedAccount?.verifiedName || selectedAccount?.phoneNumber || 'From Account'} size="small" />
            <div className="chat-dropdown-full">
              <span className="upload-sub-text">Sender Line:</span>
              <SearchableSelect
                label="Sender line"
                placeholder={accounts.length === 0 ? 'No WABA numbers connected' : 'Select a sender line'}
                hideAllOption
                disabled={accounts.length === 0}
                value={fromNumber}
                options={accounts.map((account) => ({
                  value: account.phoneNumberId,
                  label: account.phoneNumber || account.verifiedName || account.phoneNumberId,
                  keywords: account.verifiedName ?? ''
                }))}
                onChange={setFromNumber}
              />
            </div>
          </div>

          <SearchableSelect
            label="Chat filter"
            placeholder="All Chats"
            allValue="All Chats"
            hideAllOption
            value={conversationsFilter}
            options={[
              { value: 'All Chats', label: 'All Chats' },
              { value: 'Unread Chats', label: 'Unread Chats' }
            ]}
            onChange={setConversationsFilter}
          />
        </div>

        <div className="chat-sidebar-search">
          <SearchBar
            value={sidebarSearchQuery}
            onChange={setSidebarSearchQuery}
            placeholder="Search name, phone, message, group..."
          />
        </div>

        <div className="conversation-list-scroll">
          {(() => {
            const selectedConn = connections.find(c => c.id === selectedConnectionId);
            if (selectedConn && !selectedConn.phoneNumber) {
              return (
                <div className="chat-sidebar-empty-state">
                  <MessageSquare size={36} className="chat-sidebar-empty-icon" />
                  <span className="chat-sidebar-empty-title">Setup pending</span>
                  <p className="chat-sidebar-empty-desc">No WABA number connected</p>
                </div>
              );
            }
            return isLoadingConversations && conversations.length === 0 ? (
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
                  : 'Try adjusting your search query or connection filter.'}
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
                      <div className="conversation-name-wrap">
                        <span className="conversation-contact-name">{conversation.name}</span>
                      </div>
                      <ContactTypeBadge value={conversation.status} typeMap={typeMap} />
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
          )})()}
        </div>
      </div>

      <div className="chat-window">
        {isSelectedConnectionDisconnected ? (
          <div className="chat-disconnected-overlay">
            <div className="chat-disconnected-card">
              <div className="chat-disconnected-icon">
                <AlertCircle size={48} />
              </div>
              <h2 className="chat-disconnected-title">Your Account Is Disconnected!</h2>
              <p className="chat-disconnected-desc">
                Your account is no longer connected to our system. This may be due to an expired token, a disconnected webhook, invalid token, or changes in your Meta account settings.
              </p>
              <button
                className="chat-disconnected-connect-btn"
                onClick={() => navigate(`/connect-waba?connectionId=${selectedConnectionId}`)}
              >
                <Link2 size={16} />
                Connect Account
              </button>
            </div>
          </div>
        ) : activeConversation ? (
          <div className="chat-window-inner-layout">
            <div className="chat-window-header">
              <div className="chat-header-user-info">
                <Avatar name={activeConversation.name} size="medium" />
                <div>
                  <div className="chat-header-name-row">
                    <span className="conversation-contact-name">{activeConversation.name}</span>
                    <ContactTypeBadge value={activeConversation.status} typeMap={typeMap} />
                  </div>
                  <p className="upload-sub-text margin-zero">{activeConversation.phone}</p>
                </div>
              </div>

              <div className="chat-header-actions">
                <button
                  type="button"
                  className="chat-icon-btn"
                  title="Search Messages"
                  aria-label="Search messages"
                  onClick={() => setShowMsgSearch(!showMsgSearch)}
                >
                  <Search size={18} />
                </button>

                {windowStatus.active && (
                  <button
                    type="button"
                    className="chat-header-window-dot active"
                    title="Click to view time remaining"
                    aria-label="Messaging window active — click to view time remaining"
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
                    <User size={18} className="assigned-user-icon" />
                  </div>
                )}

                <button
                  type="button"
                  className={`chat-icon-btn ${showInfoDrawer ? 'active' : ''}`}
                  title="User Information"
                  aria-label="Toggle contact information"
                  onClick={() => setShowInfoDrawer(!showInfoDrawer)}
                >
                  <Info size={18} />
                </button>
                <Can permission="Chat.InitiateChat">
                  <button
                    type="button"
                    className="chat-icon-btn whatsapp-green"
                    title="Initiate Chat"
                    aria-label="Initiate chat with a template"
                    onClick={handleOpenTemplateModal}
                  >
                    <MessageSquare size={18} />
                  </button>
                </Can>

                <div className="chat-header-more-menu-wrapper">
                  <button
                    type="button"
                    className="chat-icon-btn"
                    title="More options"
                    aria-label="More options"
                    onClick={() => setShowDeleteMenu(!showDeleteMenu)}
                  >
                    <MoreVertical size={18} />
                  </button>
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

                      const isSelected = selectedMessageIds.includes(message.id)

                      return (
                        <div
                          key={message.id}
                          className={`chat-bubble-row${selectionMode ? ' selectable' : ''}${isSelected ? ' selected' : ''}`}
                          onContextMenu={(e) => openMessageMenu(e, message.id)}
                          onTouchStart={(e) => startLongPress(e, message.id)}
                          onTouchEnd={cancelLongPress}
                          onTouchMove={cancelLongPress}
                          onClick={selectionMode ? () => toggleMessageSelected(message.id) : undefined}
                        >
                          {showDateDivider && (
                            <div className="chat-date-divider">{formatDateDivider(message.createdAt)}</div>
                          )}

                          {selectionMode && (
                            <span className={`chat-bubble-check${isSelected ? ' checked' : ''}`} aria-hidden="true">
                              {isSelected && <Check size={12} />}
                            </span>
                          )}

                          <div className={getBubbleClass(message)}>
                            {message.mediaUrl && (
                              <div className="chat-bubble-media-wrapper">
                                {message.mediaType === 'image' && (
                                  <img
                                    src={getFullMediaUrl(message.mediaUrl)}
                                    alt={message.mediaFileName || 'Image'}
                                    className="chat-bubble-media-image"
                                    onClick={() => window.open(getFullMediaUrl(message.mediaUrl) || undefined, '_blank')}
                                  />
                                )}
                                {message.mediaType === 'video' && (
                                  <video
                                    src={getFullMediaUrl(message.mediaUrl)}
                                    controls
                                    className="chat-bubble-media-video"
                                  />
                                )}
                                {message.mediaType === 'document' && (
                                  <a
                                    href={getFullMediaUrl(message.mediaUrl)}
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
                                {renderRichMessageContent(message.text, msgSearchQuery)}
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
                    <Can permission="Chat.InitiateChat">
                      <button
                        type="button"
                        className="chat-window-limit-btn"
                        onClick={handleOpenTemplateModal}
                      >
                        <MessageSquare size={16} />
                        <span>Initiate Chat</span>
                      </button>
                    </Can>
                  </div>
                ) : !canSend ? (
                  // Read-only viewer: say so rather than showing a composer that 403s on send.
                  <div className="chat-composer-readonly">
                    <Lock size={15} />
                    <span>You have read-only access to this conversation.</span>
                  </div>
                ) : (
                  <form onSubmit={handleSend} className="chat-composer-container">
                    <div className="chat-composer-input-row">
                      <textarea
                        ref={textareaRef}
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
                          <button
                            type="button"
                            className="chat-icon-btn"
                            title="Emoji"
                            aria-label="Insert emoji"
                            onClick={() => { setShowEmojiPicker(!showEmojiPicker); setShowAttachmentMenu(false); }}
                          >
                            <Smile size={18} />
                          </button>
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
                          <button
                            type="button"
                            className="chat-icon-btn"
                            title="Attach"
                            aria-label="Attach a file"
                            onClick={() => { setShowAttachmentMenu(!showAttachmentMenu); setShowEmojiPicker(false); }}
                          >
                            <Paperclip size={18} />
                          </button>
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

                        <div className="chat-composer-popover-anchor">
                          <button
                            type="button"
                            className="chat-icon-btn"
                            title="Canned replies"
                            aria-label="Canned replies"
                            onClick={() => {
                              setShowCannedReplies(!showCannedReplies)
                              setShowEmojiPicker(false)
                              setShowAttachmentMenu(false)
                            }}
                          >
                            <MessageSquareReply size={18} />
                          </button>
                          {showCannedReplies && (
                            <div className="canned-replies-popover">
                              <div className="canned-replies-head">Canned Replies</div>
                              {cannedReplies.length === 0 ? (
                                <div className="canned-replies-empty">
                                  No canned replies yet. Add them under Setup → Canned Reply.
                                </div>
                              ) : (
                                <div className="canned-replies-list">
                                  {cannedReplies.map((reply) => (
                                    <button
                                      key={reply.id}
                                      type="button"
                                      className="canned-reply-item"
                                      onClick={() => insertCannedReply(reply.description)}
                                    >
                                      <div className="canned-reply-item-head">
                                        <span className="canned-reply-title">{reply.title}</span>
                                        {reply.isPublic && <span className="canned-reply-badge">Public</span>}
                                      </div>
                                      <span className="canned-reply-desc">{reply.description}</span>
                                    </button>
                                  ))}
                                </div>
                              )}
                            </div>
                          )}
                        </div>

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
                      <ContactTypeBadge value={activeConversation.status} typeMap={typeMap} />
                    </div>

                    <div className="info-drawer-section">
                      <h4 className="info-drawer-section-title">Details</h4>
                      <div className="info-details-list">
                        <div className="info-detail-item">
                          <div className="info-detail-label-row">
                            <Phone size={14} className="info-detail-icon text-emerald" />
                            <span className="info-detail-label">Connection</span>
                            <span className="info-detail-value text-blue inline">{activeConversation.connectionName || 'Connection 1'}</span>
                          </div>
                        </div>
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
              connectionId={selectedConnectionId}
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

      {/* Right-click / long-press menu on a message bubble. Fixed-positioned at the pointer,
          the way a native context menu behaves. */}
      {msgMenu && (
        <div
          className="chat-message-context-menu"
          style={{ top: msgMenu.y, left: msgMenu.x }}
          onClick={(e) => e.stopPropagation()}
          role="menu"
        >
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setSelectedMessageIds([msgMenu.id])
              setMsgMenu(null)
              setConfirmDeleteMessages(true)
            }}
          >
            <Trash2 size={14} />
            <span>Delete message</span>
          </button>
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              setSelectionMode(true)
              setSelectedMessageIds([msgMenu.id])
              setMsgMenu(null)
            }}
          >
            <Check size={14} />
            <span>Select messages</span>
          </button>
        </div>
      )}

      {/* Selection header, shown only while multi-select is active. */}
      {selectionMode && (
        <div className="chat-selection-bar">
          <button type="button" className="chat-selection-close" onClick={exitSelection} aria-label="Cancel selection">
            <X size={16} />
          </button>
          <span className="chat-selection-count">
            {selectedMessageIds.length} selected
          </span>
          <button
            type="button"
            className="chat-selection-delete"
            disabled={selectedMessageIds.length === 0}
            onClick={() => setConfirmDeleteMessages(true)}
          >
            <Trash2 size={15} />
            <span>Delete</span>
          </button>
        </div>
      )}

      {/* Delete Chat Confirmation Modal */}
      <ConfirmationModal
        isOpen={showDeleteChatModal}
        title="Delete Chat"
        message="Are you sure you want to delete this chat? This will remove all messages from the database."
        confirmText="Delete"
        cancelText="Cancel"
        onConfirm={confirmDeleteChat}
        onCancel={() => setShowDeleteChatModal(false)}
        isDestructive={true}
        showWarningIcon={true}
      />

      {/* Says plainly what deletion does and does not do. WhatsApp gives us no way to unsend
          from the recipient's device, and implying otherwise would be worse than useless. */}
      <ConfirmationModal
        isOpen={confirmDeleteMessages}
        title={selectedMessageIds.length === 1 ? 'Delete message' : `Delete ${selectedMessageIds.length} messages`}
        message={
          `This removes ${selectedMessageIds.length === 1 ? 'the message' : 'these messages'} from OmniConnect only. ` +
          'The recipient still has their copy on WhatsApp — this cannot unsend it.'
        }
        confirmText="Delete"
        cancelText="Cancel"
        onConfirm={handleConfirmDeleteMessages}
        onCancel={() => setConfirmDeleteMessages(false)}
        isDestructive={true}
        showWarningIcon={true}
      />
    </motion.div>
  )
}

/**
 * Polling cadences, named rather than sprinkled as literals.
 *
 * The open conversation is a small indexed read, so it can stay near-live. The conversation
 * list is the expensive one — it joins contacts, groups and connections for every row — so it
 * ticks far more slowly. Both were previously a single 2000ms timer, which meant the expensive
 * query ran thirty times a minute per open tab.
 *
 * These are the seam for real-time push: replacing the timers with a subscription is a change
 * to this file only.
 */
const ACTIVE_THREAD_POLL_MS = 3000
const INBOX_POLL_MS = 15000
const ACCOUNT_RETRY_MS = 5000
const ACCOUNT_RETRY_LIMIT = 6

/**
 * Renders a contact's type using the label and colour configured in Setup → Type.
 *
 * This replaces a `normalizeBadge` helper that hardcoded `lead`/`customer`/`guest`, so every
 * type an administrator added rendered as "guest" in a grey pill. The value arriving from the
 * API is the type's stored Value, which is exactly the key the lookup map is built on.
 */
const ContactTypeBadge: React.FC<{
  value?: string | null
  typeMap: Map<string, ResolvedLookup>
}> = ({ value, typeMap }) => {
  if (!value) return null
  const resolved = resolveLookup(typeMap, value)
  return (
    <span className="conversation-status-badge" style={badgeStyleFor(resolved.color)}>
      {resolved.name}
    </span>
  )
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
