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
  ChevronLeft,
  ChevronRight,
  Clock,
  Clock3,
  FileText,
  MessageSquareReply,
  Info,
  Link2,
  MessageCircle,
  Mail,
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
import { CHANNELS, normalizeChannel } from '../../types/channel'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { EmailThreadMessage } from '../../components/EmailThreadMessage/EmailThreadMessage'
import { EmailComposer } from '../../components/EmailComposer/EmailComposer'
import type { EmailComposeMode } from '../../components/EmailComposer/EmailComposer'
import type { EmailConnection } from '../../types/email'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { useChatStore, ALL_CHANNELS } from '../../store/chatStore'
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
    channelFilter,
    sidebarSearchQuery,
    setSelectedConnectionId,
    setChannelFilter,
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


  // Email connections come from their own endpoint: the WhatsApp connection store is shaped
  // around WABA accounts — its Connection type has no channel at all — so it can never describe
  // one. Kept in local state for the same reason ConnectionsList does: eight other pages read
  // that store and have no interest in email.
  const [emailConnections, setEmailConnections] = useState<EmailConnection[]>([])

  /**
   * The selected channel, or null for "All Channels".
   *
   * Compared through normalizeChannel because the picker writes the frontend's own key, which is
   * lowercase ('email'), while the API spells it 'Email'. A direct === against either spelling is
   * false half the time — which is exactly what went wrong: the comparison never matched, so the
   * pickers below stayed on the WhatsApp lists whatever was chosen.
   */
  const selectedChannel = channelFilter === ALL_CHANNELS ? null : normalizeChannel(channelFilter)

  // "All Channels" means both, so each is in scope unless the other was named specifically.
  const showsWhatsApp = selectedChannel !== 'email'
  const showsEmail = selectedChannel !== 'whatsapp'

  // Kept for the places that ask "is this an email-only view", e.g. the search placeholder.
  const isEmailChannel = selectedChannel === 'email'

  useEffect(() => {
    if (!showsEmail) return

    let isMounted = true
    emailConnectionService.getConnections().then((loaded) => {
      if (isMounted) setEmailConnections(loaded)
    })

    return () => { isMounted = false }
  }, [showsEmail])

  /**
   * The connections the Active Connection picker offers, for whichever channel is selected.
   *
   * Both channels are projected onto one shape so the picker itself does not branch. An email
   * connection is keyed by its parent `connectionId` — the Connection row that
   * ChatConversation.ConnectionId points at — not by its own EmailConfiguration id, which would
   * silently select the wrong thread set.
   */
  const channelConnections = React.useMemo(() => {
    const whatsapp = showsWhatsApp
      ? connections.map(conn => ({
          id: conn.id,
          channel: 'whatsapp' as const,
          label: `${conn.name} (${conn.phoneNumber || 'Setup pending'})`,
          keywords: conn.phoneNumber ?? '',
          usable: Boolean(conn.isConnected && conn.phoneNumber),
          /** Has something to send as. Distinct from `usable`, which also requires connectivity. */
          hasIdentity: Boolean(conn.phoneNumber)
        }))
      : []

    const email = showsEmail
      ? emailConnections
          // Keyed by the parent Connection row, which is what ChatConversation.ConnectionId
          // points at — its own EmailConfiguration id would select the wrong thread set.
          .filter(conn => conn.connectionId != null)
          .map(conn => ({
            id: conn.connectionId as number,
            channel: 'email' as const,
            label: `${conn.connectionName} (${conn.defaultFromEmail || 'Setup pending'})`,
            keywords: conn.defaultFromEmail ?? '',
            usable: conn.isActive && Boolean(conn.defaultFromEmail),
            hasIdentity: Boolean(conn.defaultFromEmail)
          }))
      : []

    return [...whatsapp, ...email]
  }, [showsWhatsApp, showsEmail, connections, emailConnections])

  /** Which channel the currently selected connection belongs to. */
  const selectedConnectionChannel =
    channelConnections.find(c => c.id === selectedConnectionId)?.channel ?? null

  /**
   * The Sender Line options.
   *
   * On email these are the connection's verified sender identities, which is the email analogue
   * of a WABA phone number — the address the recipient sees in the From line.
   */
  /**
   * The Sender Line options.
   *
   * Driven by the selected connection's own channel rather than the header filter, so on
   * "All Channels" — where the list holds both kinds — the sender line matches whichever
   * connection is actually selected instead of guessing from the header.
   */
  const senderOptions = React.useMemo(() => {
    if (selectedConnectionChannel === 'email') {
      return emailConnections
        .filter(conn => conn.connectionId === selectedConnectionId)
        .flatMap(conn => conn.senders
          .filter(sender => sender.isActive)
          .map(sender => ({
            value: sender.emailAddress,
            label: sender.emailAddress,
            keywords: sender.displayName ?? ''
          })))
    }

    return accounts.map(account => ({
      value: account.phoneNumberId,
      label: account.phoneNumber || account.verifiedName || account.phoneNumberId,
      keywords: account.verifiedName ?? ''
    }))
  }, [selectedConnectionChannel, accounts, emailConnections, selectedConnectionId])

  const isEmailConnectionSelected = selectedConnectionChannel === 'email'

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
    // On ALL_CHANNELS startup, loadConversations works without selectedConnectionId.
    // loadAccounts only makes sense for single-channel (it fetches WABA phone accounts).
    loadConversations()
    if (channelFilter !== ALL_CHANNELS) {
      loadAccounts()
    }
  }, [fetchConnectionDashboard, loadAccounts, loadConversations])

  useEffect(() => {
    // ALL_CHANNELS mode intentionally uses no specific connection — selecting one would
    // scope the conversation list to a single connection's channel, turning "All Channels"
    // into "this connection's channel only". Skip the auto-select in this mode.
    if (channelFilter === ALL_CHANNELS) return
    if (channelConnections.length === 0) return

    const current = channelConnections.find(c => c.id === selectedConnectionId)
    const firstUsable = channelConnections.find(c => c.usable)

    // A selection is only worth keeping if it can actually carry a message. The check used to be
    // "does this connection still exist", which meant a remembered connection that had lost its
    // sender number kept the whole screen in "Setup pending" — with a working line sitting one
    // item down the dropdown, unselected. Existing-but-unusable is not a selection worth honouring.
    //
    // Now driven by channelConnections rather than the WhatsApp list, so switching to Email picks
    // an email connection instead of leaving a phone number selected above an empty inbox.
    if (current?.usable) return

    // Only moved when there is somewhere better to go: if nothing can send, the remembered choice
    // stays put rather than shuffling between equally broken lines on every render.
    if (current && !firstUsable) return

    setSelectedConnectionId(firstUsable ? firstUsable.id : channelConnections[0].id)
  }, [channelFilter, channelConnections, selectedConnectionId, setSelectedConnectionId])

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

  // Auto-select the first sender whenever the available options change and the current value is
  // not in the list. This covers: switching to a new email connection (no fromNumber yet),
  // returning to a connection after a refresh, and the initial load where fromNumber is ''.
  useEffect(() => {
    if (senderOptions.length === 0) return
    const currentInList = senderOptions.some(o => o.value === fromNumber)
    if (!currentInList) {
      setFromNumber(senderOptions[0].value)
    }
  }, [senderOptions, fromNumber, setFromNumber])

  // Two independent streams at different cadences, replacing a single 2-second timer that
  // refreshed whichever was in front. The open conversation is small and cheap, so it stays
  // near-live; the conversation list is the expensive one, so it ticks slowly.
  //
  // Both pause when the tab is hidden — a background tab polling every 2 seconds was a large
  // share of the load for no one's benefit — and catch up immediately on return.
  useEffect(() => {
    // Email threads have no phoneNumber check — polling must work for both channels.
    // The old guard blocked all email thread refresh: email connections never have a phoneNumber.
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
  }, [activeConversationId, refreshActiveMessages])

  useEffect(() => {
    // Inbox polling works for all channels — the phoneNumber guard was
    // skipping email-connection inbox refreshes entirely.
    const tick = () => {
      if (document.visibilityState === 'visible') loadConversations()
    }

    const interval = window.setInterval(tick, INBOX_POLL_MS)
    document.addEventListener('visibilitychange', tick)

    return () => {
      window.clearInterval(interval)
      document.removeEventListener('visibilitychange', tick)
    }
  }, [loadConversations])

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

  // Paged in the client. The endpoint returns one connection-and-channel's conversations in a
  // single response — a few dozen rows, not a contact list — so slicing here is honest rather
  // than standing in for server paging that ought to exist.
  /** How many emails stay expanded before the rest collapse behind a "show previous" control. */
  const EMAIL_THREAD_VISIBLE = 3
  const [showAllEmails, setShowAllEmails] = useState(false)

  /**
   * The message the composer is responding to, and how.
   *
   * Held here rather than in the composer so the per-message Reply buttons further up the thread
   * can open it — replying to the third message from the bottom is a normal thing to do.
   */
  const [emailCompose, setEmailCompose] =
    useState<{ messageId: number; mode: EmailComposeMode } | null>(null)

  /** The message the composer is responding to, resolved from the open thread. */
  const activeEmailSource = useMemo(
    () => messages.find(m => m.id === emailCompose?.messageId) ?? null,
    [messages, emailCompose])

  /**
   * The address this thread sends as.
   *
   * Priority:
   *   1. The sidebar From: picker (fromNumber, when it's a valid email)
   *   2. The last outgoing message's fromAddress
   *   3. The selected email connection's defaultFromEmail
   *
   * Taken from the sidebar picker first: the user may have changed it after the thread opened.
   */
  const emailThreadFromAddress = useMemo(() => {
    // fromNumber holds the selected sender for email connections
    if (fromNumber && fromNumber.includes('@')) return fromNumber
    // Fall back to the last outgoing message's recorded from address
    const lastOutgoing = [...messages].reverse().find(m => m.type === 'outgoing' && m.fromAddress)
    if (lastOutgoing?.fromAddress) return lastOutgoing.fromAddress
    // Last resort: the connection's default
    const conn = emailConnections.find(c => c.connectionId === selectedConnectionId)
    return conn?.defaultFromEmail ?? null
  }, [fromNumber, messages, emailConnections, selectedConnectionId])

  const [conversationPage, setConversationPage] = useState(1)
  const [conversationPageSize, setConversationPageSize] = useState(8)

  const filteredConversations = useMemo(() => {
    return conversations.filter((conversation) => {
      if (sidebarSearchQuery) {
        const q = sidebarSearchQuery.toLowerCase()
        if (!conversation.name.toLowerCase().includes(q) && !conversation.phone.includes(q)) return false
      }

      if (conversationsFilter === 'Unread Chats' && conversation.unreadCount === 0) {
        return false
      }

      // No channel predicate here any more. The server scopes the fetch by channel, so a
      // second filter over the response could only ever remove rows the response never had —
      // which is exactly how selecting Email used to empty an inbox that had email in it.
      return true
    })
  }, [conversations, conversationsFilter, sidebarSearchQuery])

  const conversationPageCount = Math.max(
    1, Math.ceil(filteredConversations.length / conversationPageSize))
  const currentConversationPage = Math.min(conversationPage, conversationPageCount)

  const pagedConversations = filteredConversations.slice(
    (currentConversationPage - 1) * conversationPageSize,
    currentConversationPage * conversationPageSize)

  // Back to page one whenever the list underneath changes shape. Without this the footer can
  // describe a page that no longer exists — "showing 17 to 24 of 6".
  useEffect(() => {
    setConversationPage(1)
  }, [channelFilter, selectedConnectionId, conversationsFilter, sidebarSearchQuery])

  // Each thread opens collapsed. Carrying "expanded" across to the next one would show a
  // different conversation's full history without being asked.
  useEffect(() => {
    setShowAllEmails(false)
    setEmailCompose(null)
  }, [activeConversationId])

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


  // Taken from the conversation, not from the header filter: on "All Channels" both kinds of
  // thread are in the list, and how a thread renders is a property of the thread.
  const isEmailThread =
    activeConversation != null && normalizeChannel(activeConversation.channel) === 'email'

  return (
    <motion.div className="chat-page" {...pageTransitionProps}>
      {/*
        The channel selector belongs to the page, not to the conversation list. It governs both
        panes — which threads are listed *and* which composer the thread shows — so presenting it
        as one more sidebar filter understated what it does.

        Unavailable channels are listed but disabled, which is deliberate: it answers "does this
        product do SMS?" without pretending that it does.
      */}
      <div className="chat-page-header">
        <div className="chat-page-heading">
          <h1>Omnichannel Inbox</h1>
          <p>Manage customer conversations across all your communication channels in one place.</p>
        </div>

        <div className="chat-page-channel">
          <label className="chat-page-channel-label">Channel</label>
          <ChannelPicker
            value={channelFilter}
            onChange={(value) => {
              setChannelFilter(value.startsWith('__unavailable_') ? 'All Channels' : value)
            }}
          />
        </div>
      </div>

      <div className="chat-container-layout">
      <div className="chat-sidebar">
        <div className="chat-sidebar-header">

          {/* Connection picker — hidden in ALL_CHANNELS mode where the list spans all connections */}
          {channelFilter === ALL_CHANNELS ? (
            <div className="chat-connection-select-wrapper">
              <label className="chat-sidebar-field-label">Active Connection</label>
              <div className="chat-all-connections-badge">
                <span className="chat-all-connections-label">All Connections</span>
              </div>
            </div>
          ) : (
          <div className="chat-connection-select-wrapper">
            <label className="chat-sidebar-field-label">
              Active Connection
            </label>
            <SearchableSelect
              label="Active connection"
              placeholder={
                channelConnections.length === 0
                  ? (isEmailChannel ? 'No email connections' : 'Select a connection')
                  : 'Select a connection'
              }
              hideAllOption
              className="chat-connection-select"
              value={selectedConnectionId != null ? String(selectedConnectionId) : ''}
              options={channelConnections.map((conn) => ({
                value: String(conn.id),
                label: conn.label,
                keywords: conn.keywords
              }))}
              onChange={(val) => setSelectedConnectionId(val ? Number(val) : null)}
            />
          </div>
          )}

          {/* From/Sender row — only shown when a specific connection is selected */}
          {channelFilter !== ALL_CHANNELS && (
          <div className="chat-account-display-row">
            <Avatar
              name={
                isEmailConnectionSelected
                  ? (fromNumber || 'From Address')
                  : (selectedAccount?.verifiedName || selectedAccount?.phoneNumber || 'From Account')
              }
              size="small"
            />
            <div className="chat-dropdown-full">
              <span className="upload-sub-text">{isEmailConnectionSelected ? 'From:' : 'Sender Line:'}</span>
              <SearchableSelect
                label={isEmailConnectionSelected ? 'From address' : 'Sender line'}
                placeholder={
                  senderOptions.length === 0
                    ? (isEmailConnectionSelected ? 'No verified senders' : 'No WABA numbers connected')
                    : (isEmailConnectionSelected ? 'Select a from address' : 'Select a sender line')
                }
                hideAllOption
                disabled={senderOptions.length === 0}
                value={fromNumber}
                options={senderOptions}
                onChange={setFromNumber}
              />
            </div>
          </div>
          )}

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
            placeholder={
              channelFilter === ALL_CHANNELS
                ? 'Search name, phone, email, message...'
                : isEmailChannel
                  ? 'Search name, email, subject...'
                  : 'Search name, phone, message, group...'
            }
          />
        </div>

        <div className="conversation-list-scroll">
          {(() => {
            // Judged against the selected channel's own connections. This used to test the
            // WhatsApp list unconditionally, so choosing Email put a "No WABA number connected"
            // error over a fully configured email account.
            const selectedConn = channelConnections.find(c => c.id === selectedConnectionId);
            if (selectedConn && !selectedConn.hasIdentity) {
              return (
                <div className="chat-sidebar-empty-state">
                  {isEmailChannel
                    ? <Mail size={36} className="chat-sidebar-empty-icon" />
                    : <MessageSquare size={36} className="chat-sidebar-empty-icon" />}
                  <span className="chat-sidebar-empty-title">Setup pending</span>
                  <p className="chat-sidebar-empty-desc">
                    {isEmailChannel
                      ? 'This email connection has no sender address yet'
                      : 'No WABA number connected'}
                  </p>
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
            pagedConversations.map((conversation) => {
              const isActive = conversation.id === activeConversationId
              return (
                <button
                  key={conversation.id}
                  type="button"
                  className={`conversation-item ${isActive ? 'active' : ''}`}
                  onClick={() => selectConversation(conversation.id)}
                >
                  {/*
                    The avatar carries a small channel marker, as in the reference designs. It
                    sits on the avatar rather than beside the name because the name row already
                    competes with the relation badge, and the channel is something an operator
                    scans down the list for rather than reads.
                  */}
                  <span
                    className="conversation-avatar-wrap"
                    data-channel={normalizeChannel(conversation.channel)}
                  >
                    <Avatar name={conversation.name} size="medium" />
                    <span className="conversation-channel-dot" aria-hidden="true">
                      {normalizeChannel(conversation.channel) === 'email'
                        ? <Mail size={10} />
                        : <MessageCircle size={10} />}
                    </span>
                    <span className="sr-only">
                      {normalizeChannel(conversation.channel) === 'email' ? 'Email' : 'WhatsApp'}
                    </span>
                  </span>

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

        {/* Compact single-row pagination footer — always visible once there are results */}
        {filteredConversations.length > 0 && (
          <div className="chat-sidebar-footer">
            <span className="chat-sidebar-footer-count">
              {((currentConversationPage - 1) * conversationPageSize + 1).toLocaleString()}–
              {Math.min(currentConversationPage * conversationPageSize, filteredConversations.length).toLocaleString()}
              {' '}of{' '}
              {filteredConversations.length.toLocaleString()}
            </span>

            <div className="chat-sidebar-footer-nav">
              <button
                type="button"
                className="chat-sidebar-footer-nav-btn"
                onClick={() => setConversationPage(currentConversationPage - 1)}
                disabled={currentConversationPage <= 1}
                aria-label="Previous page"
              >
                <ChevronLeft size={14} />
              </button>
              <span className="chat-sidebar-footer-page">
                {currentConversationPage}/{conversationPageCount}
              </span>
              <button
                type="button"
                className="chat-sidebar-footer-nav-btn"
                onClick={() => setConversationPage(currentConversationPage + 1)}
                disabled={currentConversationPage >= conversationPageCount}
                aria-label="Next page"
              >
                <ChevronRight size={14} />
              </button>
            </div>

            <div className="chat-sidebar-footer-rows">
              <span className="chat-sidebar-footer-rows-label">Rows</span>
              <select
                value={conversationPageSize}
                onChange={(e) => { setConversationPageSize(Number(e.target.value)); setConversationPage(1) }}
                aria-label="Rows per page"
              >
                {[8, 15, 25, 50].map(s => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </div>
          </div>
        )}
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
                  <p className="upload-sub-text margin-zero">
                    {isEmailThread
                      ? (activeConversation.email || activeConversation.phone)
                      : activeConversation.phone}
                  </p>
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

                {/* 24h window indicator — WhatsApp only */}
                {!isEmailThread && windowStatus.active && (
                  <button
                    type="button"
                    className="chat-header-window-dot active"
                    title="Click to view time remaining"
                    aria-label="Messaging window active — click to view time remaining"
                    onClick={() => setShowTimeBanner(true)}
                  />
                )}
                {!isEmailThread && !windowStatus.active && (
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
                {/* Initiate Chat (WhatsApp templates) — only for WhatsApp threads */}
                {!isEmailThread && (
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
                )}

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

                <div className={`chat-messages-container${isEmailThread ? ' email-mode' : ''}`}>
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

                      // Emails are documents, not a running conversation: a campaign contact can
                      // accumulate many, and showing every one expanded buries the newest under
                      // months of older sends. Collapsed to the last few, expandable in place.
                      //
                      // Not applied to WhatsApp, where hiding the middle of a conversation breaks
                      // the sequence the operator is reading.
                      if (isEmailThread && !showAllEmails
                          && index < searchedMessages.length - EMAIL_THREAD_VISIBLE) {
                        if (index > 0) return null
                        return (
                          <button
                            key="email-thread-expand"
                            type="button"
                            className="email-thread-expand"
                            onClick={() => setShowAllEmails(true)}
                          >
                            ··· Show previous messages (
                            {searchedMessages.length - EMAIL_THREAD_VISIBLE})
                          </button>
                        )
                      }
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

                          {normalizeChannel(message.channel) === 'email' ? (
                            <EmailThreadMessage
                              message={message}
                              onRespond={(mode) => setEmailCompose({ messageId: message.id, mode })}
                            />
                          ) : (
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
                          )}

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

                {isEmailThread ? (
                  emailCompose && (activeEmailSource || emailCompose.mode === 'newEmail') ? (
                    <EmailComposer
                      conversationId={activeConversationId!}
                      source={activeEmailSource ?? messages[messages.length - 1] ?? ({} as Message)}
                      fromAddress={emailThreadFromAddress}
                      mode={emailCompose.mode}
                      onModeChange={(mode) => setEmailCompose({ ...emailCompose, mode })}
                      onSent={() => {
                        setEmailCompose(null)
                        refreshActiveMessages()
                        // Also refresh the inbox so the preview + timestamp update immediately
                        void loadConversations()
                      }}
                      onCancel={() => setEmailCompose(null)}
                    />
                  ) : (
                    /*
                     * Closed by default. An email thread is read far more often than it is replied
                     * to, and a composer permanently occupying a third of the pane pushes the mail
                     * itself off screen — so it opens from the Reply/New Email buttons.
                     */
                    <div className="chat-composer-readonly email">
                      <div className="chat-composer-readonly-actions">
                        <button
                          type="button"
                          className="chat-composer-readonly-btn"
                          onClick={() => {
                            const lastMsg = messages[messages.length - 1]
                            if (lastMsg) setEmailCompose({ messageId: lastMsg.id, mode: 'reply' })
                          }}
                        >
                          <Mail size={14} />
                          Reply
                        </button>
                        <button
                          type="button"
                          className="chat-composer-readonly-btn"
                          onClick={() => {
                            const lastMsg = messages[messages.length - 1]
                            if (lastMsg) setEmailCompose({ messageId: lastMsg.id, mode: 'replyAll' })
                          }}
                        >
                          <Mail size={14} />
                          Reply All
                        </button>
                        <button
                          type="button"
                          className="chat-composer-readonly-btn"
                          onClick={() => {
                            const lastMsg = messages[messages.length - 1]
                            if (lastMsg) setEmailCompose({ messageId: lastMsg.id, mode: 'forward' })
                          }}
                        >
                          <Mail size={14} />
                          Forward
                        </button>
                        <button
                          type="button"
                          className="chat-composer-readonly-btn new-email"
                          onClick={() => setEmailCompose({ messageId: -1, mode: 'newEmail' })}
                        >
                          <Plus size={14} />
                          New Email
                        </button>
                      </div>
                    </div>
                  )
                ) : !isEmailThread && !windowStatus.active ? (
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
                ) : !isEmailThread && !canSend ? (
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
      </div>

    </motion.div>
  )
}

/**
 * Polling cadences, named rather than sprinkled as literals.
 *
 * The open conversation is a small indexed read, so it can stay near-live (1 s). The
 * conversation list joins contacts, groups and connections for every row, so it ticks at 5 s.
 * The old 15 s inbox cadence meant a sent email could take 15 s to appear — tightened to 5 s
 * here so the inbox preview updates quickly without hammering the server.
 */
const ACTIVE_THREAD_POLL_MS = 1000
const INBOX_POLL_MS = 5000
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

/* ──────────────────────────────────────────────────────────────────────────────
   ChannelPicker — custom dropdown matching Image 3.

   Shows "All Channels" on top (with a checkmark when active), then each available
   channel with its icon, then a divider + "Coming Soon" section for planned ones.
   ────────────────────────────────────────────────────────────────────────────── */

const CHANNEL_ICON_MAP: Record<string, React.ReactNode> = {
  whatsapp: (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none">
      <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347z" fill="#25D366"/>
      <path d="M12 0C5.373 0 0 5.373 0 12c0 2.128.557 4.122 1.528 5.855L0 24l6.345-1.498A11.956 11.956 0 0012 24c6.627 0 12-5.373 12-12S18.627 0 12 0zm0 22c-1.92 0-3.722-.497-5.28-1.37l-.379-.215-3.766.888.934-3.65-.248-.396A9.935 9.935 0 012 12c0-5.514 4.486-10 10-10s10 4.486 10 10-4.486 10-10 10z" fill="#25D366"/>
    </svg>
  ),
  email: <Mail size={15} color="#f97316" />,
  sms: <MessageSquare size={15} color="#8b5cf6" />,
  instagram: <span style={{ fontSize: 13 }}>📷</span>,
  facebook: <span style={{ fontSize: 13 }}>👍</span>,
}

const ChannelPicker: React.FC<{
  value: string
  onChange: (value: string) => void
}> = ({ value, onChange }) => {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  const activeLabel = value === ALL_CHANNELS
    ? 'All Channels'
    : CHANNELS.find(c => c.key === value)?.label ?? value

  const activeIcon = value !== ALL_CHANNELS && CHANNEL_ICON_MAP[value]
    ? CHANNEL_ICON_MAP[value]
    : null

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  const available = CHANNELS.filter(c => c.available)
  const planned = CHANNELS.filter(c => !c.available)

  const select = (v: string) => {
    onChange(v)
    setOpen(false)
  }

  return (
    <div className="channel-picker" ref={ref}>
      <button
        type="button"
        className={`channel-picker-trigger${open ? ' open' : ''}`}
        onClick={() => setOpen(o => !o)}
        aria-haspopup="listbox"
        aria-expanded={open}
      >
        <span className="channel-picker-icon-wrap">
          {activeIcon ?? <MessageCircle size={15} color="#64748b" />}
        </span>
        <span className="channel-picker-label">{activeLabel}</span>
        <svg className="channel-picker-caret" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M6 9l6 6 6-6"/></svg>
      </button>

      {open && (
        <div className="channel-picker-menu" role="listbox">
          {/* All Channels */}
          <button
            type="button"
            role="option"
            aria-selected={value === ALL_CHANNELS}
            className={`channel-picker-item${value === ALL_CHANNELS ? ' selected' : ''}`}
            onClick={() => select(ALL_CHANNELS)}
          >
            <span className="channel-picker-item-icon"><MessageCircle size={15} color="#64748b" /></span>
            <span className="channel-picker-item-label">All Channels</span>
            {value === ALL_CHANNELS && <Check size={14} className="channel-picker-check" />}
          </button>

          {/* Available channels */}
          {available.map(ch => (
            <button
              key={ch.key}
              type="button"
              role="option"
              aria-selected={value === ch.key}
              className={`channel-picker-item${value === ch.key ? ' selected' : ''}`}
              onClick={() => select(ch.key)}
            >
              <span className="channel-picker-item-icon">{CHANNEL_ICON_MAP[ch.key]}</span>
              <span className="channel-picker-item-label">{ch.label}</span>
              {value === ch.key && <Check size={14} className="channel-picker-check" />}
            </button>
          ))}

          {/* Coming Soon divider + planned channels */}
          {planned.length > 0 && (
            <>
              <div className="channel-picker-divider">
                <span>Coming Soon</span>
              </div>
              {planned.map(ch => (
                <button
                  key={ch.key}
                  type="button"
                  role="option"
                  aria-selected={false}
                  className="channel-picker-item disabled"
                  disabled
                >
                  <span className="channel-picker-item-icon">{CHANNEL_ICON_MAP[ch.key]}</span>
                  <span className="channel-picker-item-label">{ch.label}</span>
                  <span className="channel-picker-coming-soon">Soon</span>
                </button>
              ))}
            </>
          )}
        </div>
      )}
    </div>
  )
}
