import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { ReplyButtonsPopover } from './ReplyButtonsPopover'
import useReference from '../../hooks/useReference'
import { referenceService, labelOf } from '../../services/referenceService'
import { ConversationOwnerSelect, ConversationStatusBadge, SlaChip } from './ConversationOwnerControls'
import { statusToggleFor, toggleConversationStatus } from './conversationStatus'
import { ChatContextPanel } from './ChatContextPanel'
import { ContactTypeBadge } from './ContactTypeBadge'
import { useAuthStore } from '../../store/authStore'
import { AnimatePresence, motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { useSearchParams, useNavigate } from 'react-router-dom'
import { aiReplyService, type CannedReply } from '../../services/setup/aiReplyService'
import {
  AlertCircle,
  AlertTriangle,
  Check,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock,
  Copy,
  FileText,
  Link2,
  Lock,
  Mail,
  MessageCircle,
  MessageSquare,
  MessageSquareReply,
  MoreVertical,
  PanelRight,
  Paperclip,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  Send,
  Smile,
  Trash2,
  UserRound,
  X,
  SlidersHorizontal
} from 'lucide-react'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import { Avatar } from '../../components/Avatar/Avatar'
import { SearchBar } from '../../components/SearchBar/SearchBar'
import { CHANNELS, normalizeChannel } from '../../types/channel'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { EmailThreadMessage } from '../../components/EmailThreadMessage/EmailThreadMessage'
import { EmailComposer } from '../../components/EmailComposer/EmailComposer'
import type { EmailComposeMode } from '../../components/EmailComposer/EmailComposer'
import type { EmailConnection } from '../../types/email'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import { Menu, MenuItem } from '../../components/Menu/Menu'
import { useChatStore, ALL_CHANNELS } from '../../store/chatStore'
import { useShallow } from 'zustand/react/shallow'
import { realtimeService } from '../../services/campaigns/campaignHubService'
import { useConnectionStore } from '../../store/connectionStore'
import { campaignService } from '../../services/campaigns/campaignService'
import { InitiateChatModal } from '../../components/Modal/InitiateChatModal'
import type { Conversation, Message } from '../../types/chat'
import './Chat.css'
import { useFullBleed } from '../../hooks/useFullBleed'
import { useDocumentTitle } from '../../components/ui'
import { AccountMenu } from '../../components/AccountMenu'
import { ChannelPicker } from './components/ChannelPicker'
import { RichMessage, messagePreview } from './components/RichMessage'
import { MessageStatus } from './components/MessageStatus'
import { startsNewDay, formatDayDivider } from './chatFormat'
import { QUICK_EMOJIS } from './data/emojis'
import { useServiceWindow } from './hooks/useServiceWindow'
import { CONVERSATION_PAGE_SIZE } from '../../services/chat/chatService'
import Can from '../../components/Can/Can'
import usePermission from '../../hooks/usePermission'
import { resolveMediaUrl } from '../../utils/mediaUrl'
import { buildLookupMap } from '../../utils/lookupColors'
import { contactService } from '../../services/contacts/contactService'
import type { ContactType } from '../../types/contacts'
import { matchesSearch } from '../../utils/smartSearch'
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'

export const Chat: React.FC = () => {
  // The inbox takes the whole space beside the sidebar: no top header, no page padding.
  useFullBleed()
  useDocumentTitle('Inbox')
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
    setSidebarSearchQuery,
    stateFilter,
    assigneeFilter,
    setStateFilter,
    setAssigneeFilter,
    hasMoreConversations,
    isLoadingMoreConversations,
    loadMoreConversations,
    hasOlderMessages,
    isLoadingOlderMessages,
    loadOlderMessages,
    resyncActiveMessages,
    threadError,
    sortOrder,
    setSortOrder,
    applyQuickView,
    quickViewCounts
  } = useChatStore(useShallow((state) => ({
    accounts: state.accounts,
    conversations: state.conversations,
    activeConversationId: state.activeConversationId,
    selectedConnectionId: state.selectedConnectionId,
    messages: state.messages,
    isLoading: state.isLoading,
    isLoadingConversations: state.isLoadingConversations,
    isSending: state.isSending,
    fromNumber: state.fromNumber,
    conversationsFilter: state.conversationsFilter,
    channelFilter: state.channelFilter,
    sidebarSearchQuery: state.sidebarSearchQuery,
    setSelectedConnectionId: state.setSelectedConnectionId,
    setChannelFilter: state.setChannelFilter,
    loadAccounts: state.loadAccounts,
    loadConversations: state.loadConversations,
    refreshActiveMessages: state.refreshActiveMessages,
    selectConversation: state.selectConversation,
    sendMessage: state.sendMessage,
    deleteActiveConversation: state.deleteActiveConversation,
    deleteMessages: state.deleteMessages,
    setFromNumber: state.setFromNumber,
    setSidebarSearchQuery: state.setSidebarSearchQuery,
    stateFilter: state.stateFilter,
    assigneeFilter: state.assigneeFilter,
    setStateFilter: state.setStateFilter,
    setAssigneeFilter: state.setAssigneeFilter,
    hasMoreConversations: state.hasMoreConversations,
    isLoadingMoreConversations: state.isLoadingMoreConversations,
    loadMoreConversations: state.loadMoreConversations,
    hasOlderMessages: state.hasOlderMessages,
    isLoadingOlderMessages: state.isLoadingOlderMessages,
    loadOlderMessages: state.loadOlderMessages,
    resyncActiveMessages: state.resyncActiveMessages,
    threadError: state.threadError,
    sortOrder: state.sortOrder,
    setSortOrder: state.setSortOrder,
    applyQuickView: state.applyQuickView,
    quickViewCounts: state.quickViewCounts
  })))


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
  const messagesContainerRef = useRef<HTMLDivElement>(null)

  // Measured only for its width (whether the details panel can dock); the page itself fills
  // the full-bleed container, so no top offset is needed any more.
  const layoutRef = useRef<HTMLDivElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const prevMessagesCountRef = useRef(0)
  const prevActiveConvIdRef = useRef<number | null>(null)
  const requestedContactId = Number(searchParams.get('contactId') || 0)

  const selectedConnection = connections.find(c => c.id === selectedConnectionId)
  const isSelectedConnectionDisconnected = selectedConnection ? !selectedConnection.isConnected : false

  // Popover & Upload States
  const [showEmojiPicker, setShowEmojiPicker] = useState(false)
  const [showCannedReplies, setShowCannedReplies] = useState(false)
  // Reply buttons to send under the next message (WhatsApp interactive, max three).
  const [replyButtons, setReplyButtons] = useState<string[]>([])
  const [showReplyButtons, setShowReplyButtons] = useState(false)
  // Inbox filters and conversation states, as the server defines them.
  const chatOptions = useReference(referenceService.getChatOptions, 'chat-options')
  const [cannedReplies, setCannedReplies] = useState<CannedReply[]>([])
  const currentUser = useAuthStore(state => state.user)
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
    } catch {
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
    // Read once at mount: a later channel change loads its own accounts.
    if (useChatStore.getState().channelFilter !== ALL_CHANNELS) {
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
  }, [sidebarSearchQuery, loadConversations])

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

  // Live inbox. The server pushes "something changed in conversation N"; the page then fetches
  // only what is new. This replaces a 1-second poll of the open thread (which re-downloaded the
  // whole history each time) and a 5-second poll of the whole inbox. While the real-time
  // connection is down, a slow poll stands in, and a reconnect triggers one full re-sync because
  // events sent while offline are not replayed.
  useEffect(() => {
    const unsubscribeInbox = realtimeService.subscribeInbox((event) => {
      const activeId = useChatStore.getState().activeConversationId
      if (event.type === 'messageReceived') {
        if (event.conversationId === activeId) void refreshActiveMessages()
        void loadConversations()
      } else if (event.type === 'messageStatus' && event.conversationId === activeId) {
        void resyncActiveMessages()
      } else if (event.type === 'conversationUpdated') {
        // Owner, status or SLA changed (routing, another agent, the SLA worker).
        void loadConversations()
      }
    })

    let fallback: number | null = null
    const tick = () => {
      if (document.visibilityState !== 'visible') return
      void refreshActiveMessages()
      void loadConversations()
    }

    const unsubscribeState = realtimeService.onStateChange((state, { reconnected }) => {
      if (state === 'connected') {
        if (fallback !== null) window.clearInterval(fallback)
        fallback = null
        if (reconnected) {
          void resyncActiveMessages()
          void loadConversations()
        }
      } else if (fallback === null) {
        fallback = window.setInterval(tick, FALLBACK_POLL_MS)
      }
    })

    // Catch up at once when the tab comes back into view.
    document.addEventListener('visibilitychange', tick)

    return () => {
      if (fallback !== null) window.clearInterval(fallback)
      document.removeEventListener('visibilitychange', tick)
      unsubscribeState()
      unsubscribeInbox()
    }
  }, [loadConversations, refreshActiveMessages, resyncActiveMessages])

  // "Latest requested contact wins, exactly once" guard. Without this, since the
  // ?contactId= param was never cleared, ANY change to activeConversationId (including
  // the user manually clicking a different conversation, which itself changes
  // activeConversationId) re-ran this effect and snapped the UI straight back to the
  // originally-requested contact — the URL param, not the user, always won.
  // Storing the *consumed id itself* (not a plain boolean) means a fresh, different
  // ?contactId= naturally re-arms the guard with no separate reset effect needed.
  const consumedContactIdRef = useRef<number>(0)

  // Opening one of the contact's other conversations from the details panel. If it is not in the
  // list on screen (another channel or connection), the inbox switches to where it lives and
  // narrows the search to the contact; the conversation opens as soon as it is listed.
  const [pendingOpenId, setPendingOpenId] = useState<number | null>(null)
  const openConversationAnywhere = (target: Conversation) => {
    if (conversations.some(c => c.id === target.id)) {
      void selectConversation(target.id)
      return
    }
    const key = normalizeChannel(target.channel)
    if (key) setChannelFilter(key)
    if (target.connectionId) setSelectedConnectionId(target.connectionId)
    setSidebarSearchQuery((key === 'email' ? target.email : target.phone) || target.name)
    setPendingOpenId(target.id)
  }
  useEffect(() => {
    if (pendingOpenId == null || !conversations.some(c => c.id === pendingOpenId)) return
    void selectConversation(pendingOpenId)
    setPendingOpenId(null)
  }, [conversations, pendingOpenId, selectConversation])

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
      // Scrolls the message pane only. scrollIntoView also scrolled every scrollable ancestor,
      // so opening a conversation made the whole page jump.
      const pane = messagesContainerRef.current
      if (pane) pane.scrollTo({ top: pane.scrollHeight, behavior: hasConvChanged ? 'auto' : 'smooth' })
    }

    prevMessagesCountRef.current = messages.length
    prevActiveConvIdRef.current = activeConversationId
  }, [messages, activeConversationId])

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

  /**
   * The composer opens on purpose only — from a message's Reply / Reply All / Forward, or New
   * Email — and closes with its ✕. Docking it permanently repeated those buttons and hid the thread.
   */
  const composeTarget = emailCompose

  /** The message the composer is responding to, resolved from the open thread. */
  const activeEmailSource = useMemo(
    () => messages.find(m => m.id === composeTarget?.messageId) ?? null,
    [messages, composeTarget?.messageId])

  // Opening the composer shrinks the thread; keep the message being answered on screen. Scrolls
  // the thread pane only (scrollIntoView would move the page too).
  useEffect(() => {
    if (!emailCompose || emailCompose.messageId < 0) return
    const frame = requestAnimationFrame(() => {
      const pane = messagesContainerRef.current
      const row = pane?.querySelector<HTMLElement>(`[data-message-id="${emailCompose.messageId}"]`)
      if (!pane || !row) return
      const paneBox = pane.getBoundingClientRect()
      const rowBox = row.getBoundingClientRect()
      if (rowBox.bottom > paneBox.bottom) pane.scrollTop += rowBox.bottom - paneBox.bottom
      else if (rowBox.top < paneBox.top) pane.scrollTop -= paneBox.top - rowBox.top
    })
    return () => cancelAnimationFrame(frame)
  }, [emailCompose])

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

  const [showFilters, setShowFilters] = useState(false)
  // Defaults are the first option of each server list, so changing a default is a catalogue edit.
  const filterDefaults = useMemo(() => ({
    state: chatOptions.data?.stateFilters?.[0]?.value ?? 'active',
    assignee: chatOptions.data?.assigneeFilters?.[0]?.value ?? ''
  }), [chatOptions.data])

  // The tabs are combinations of the read and owner filters, defined by the server.
  const quickViews = chatOptions.data?.quickViews ?? []
  const activeQuickView = quickViews.find(v => v.readFilter === conversationsFilter && v.assigneeFilter === assigneeFilter) ?? null
  // An owner choice no tab expresses ("Unassigned") counts as a filter; one a tab shows does not.
  const ownerIsFiltered = !quickViews.some(v => v.assigneeFilter === assigneeFilter)
  const activeFilterCount = Number(stateFilter !== filterDefaults.state) + Number(ownerIsFiltered)
  const clearFilters = () => {
    setStateFilter(filterDefaults.state)
    if (ownerIsFiltered) setAssigneeFilter(filterDefaults.assignee)
  }

  const [conversationPage, setConversationPage] = useState(1)
  // Page sizes come from the server's chat options (the first is the default); until they load,
  // the list shows one page of what the server returns per request.
  const listPageSizes = chatOptions.data?.listPageSizes ?? []
  const [chosenPageSize, setConversationPageSize] = useState<number | null>(null)
  const conversationPageSize = chosenPageSize ?? listPageSizes[0] ?? CONVERSATION_PAGE_SIZE

  // The server applies every filter, so the list is shown as loaded. When the active tab's count
  // is known it is the total; otherwise the footer says how many are loaded and whether more exist.
  const listTotal = activeQuickView && quickViewCounts ? quickViewCounts[activeQuickView.value] ?? null : null
  const loadedPageCount = Math.max(1, Math.ceil(conversations.length / conversationPageSize))
  const conversationPageCount = listTotal != null
    ? Math.max(1, Math.ceil(listTotal / conversationPageSize))
    : loadedPageCount + (hasMoreConversations ? 1 : 0)
  const currentConversationPage = Math.min(conversationPage, conversationPageCount)

  const pagedConversations = conversations.slice(
    (currentConversationPage - 1) * conversationPageSize,
    currentConversationPage * conversationPageSize)

  // Back to page one whenever the list underneath changes shape. Without this the footer can
  // describe a page that no longer exists — "showing 17 to 24 of 6".
  useEffect(() => {
    setConversationPage(1)
  }, [channelFilter, selectedConnectionId, conversationsFilter, sidebarSearchQuery, stateFilter, assigneeFilter, sortOrder])

  const goToConversationPage = (page: number) => {
    if (page > loadedPageCount) void loadMoreConversations()
    setConversationPage(page)
  }

  const refreshInbox = () => {
    void loadConversations()
    if (channelFilter !== ALL_CHANNELS) void loadAccounts()
  }

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

  // 2. Conversation actions (the header's ⋮ menu)
  const [headerMenuOpen, setHeaderMenuOpen] = useState(false)

  // 3. Details panel. Open by default where it docks beside the conversation (decided once the
  // layout is measured); the operator's choice is remembered per browser, because it is a
  // reading preference rather than data.
  const [showDetails, setShowDetails] = useState<boolean>(() => {
    try {
      const saved = window.localStorage.getItem(DETAILS_PANEL_STORAGE_KEY)
      if (saved !== null) return saved === 'open'
    } catch {
      // Storage can be unavailable (private mode); fall through to the width check.
    }
    return false
  })
  useLayoutEffect(() => {
    try {
      if (window.localStorage.getItem(DETAILS_PANEL_STORAGE_KEY) !== null) return
    } catch {
      // As above.
    }
    // The docking width is defined once, in Chat.css (--details-dock-min), next to the
    // container query that uses it.
    const el = layoutRef.current
    if (!el) return
    const dockMin = Number.parseFloat(getComputedStyle(el).getPropertyValue('--details-dock-min')) || 0
    if (el.getBoundingClientRect().width >= dockMin) setShowDetails(true)
  }, [])
  const setDetailsOpen = (open: boolean) => {
    setShowDetails(open)
    try {
      window.localStorage.setItem(DETAILS_PANEL_STORAGE_KEY, open ? 'open' : 'closed')
    } catch {
      // Same as above: the panel still opens and closes, it just is not remembered.
    }
  }

  // 6. Local Search Bar
  const [showMsgSearch, setShowMsgSearch] = useState(false)
  const [msgSearchQuery, setMsgSearchQuery] = useState('')

  const activeConversation = useMemo(() => {
    return conversations.find(c => c.id === activeConversationId) || null
  }, [conversations, activeConversationId])

  // Canned replies may contain {{first_name}}, {{name}}, {{phone}}, {{email}} and {{agent_name}},
  // filled from the open conversation and the signed-in agent. Unknown fields are left visible.
  const fillCannedVariables = (text: string) => {
    const contactName = activeConversation?.name ?? ''
    const values: Record<string, string> = {
      name: contactName,
      first_name: contactName.split(' ')[0] ?? '',
      phone: activeConversation?.phone ?? '',
      email: activeConversation?.email ?? '',
      agent_name: [currentUser?.firstName, currentUser?.lastName].filter(Boolean).join(' '),
    }
    return text.replace(/\{\{\s*([a-z_]+)\s*\}\}/gi, (whole, key: string) => values[key.toLowerCase()] || whole)
  }

  // WhatsApp's 24-hour customer service window, decided by the server and counted down here.
  const windowStatus = useServiceWindow(activeConversation?.serviceWindowEndsAt)

  // Message search inside the open conversation. The text is what gets highlighted, but an
  // attachment is found by its filename and a failed send by its error -- neither of which lives
  // in the text -- so both are matched too.
  const searchedMessages = useMemo(() => {
    if (!msgSearchQuery.trim()) return messages
    return messages.filter((m) =>
      matchesSearch(msgSearchQuery, [m.text, m.mediaFileName, m.mediaType, m.errorMessage])
    )
  }, [messages, msgSearchQuery])

  const [showDeleteChatModal, setShowDeleteChatModal] = useState(false)

  const handleDeleteChat = () => {
    if (!activeConversationId) return
    setShowDeleteChatModal(true)
  }

  const confirmDeleteChat = async () => {
    setShowDeleteChatModal(false)
    if (!activeConversationId) return
    try {
      await deleteActiveConversation()
      toast.success('Conversation deleted.')
    } catch {
      toast.error('The conversation could not be deleted. Try again.')
    }
  }

  const sendCurrentMessage = async () => {
    const text = messageText.trim()
    if (!text || isSending) return

    if (activeConversation && activeConversation.contactIsActive === false) {
      toast.error('Cannot send message to an inactive contact.', { duration: 3000 })
      return
    }

    const buttons = replyButtons.map(b => b.trim()).filter(Boolean)
    setMessageText('')
    setReplyButtons([])
    setShowReplyButtons(false)
    await sendMessage(text, undefined, undefined, undefined, buttons)
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

  // "/shortcut": typing a slash and part of a canned reply's title offers it; Enter or Tab uses it.
  const slashQuery = /^\/(\S*)$/.exec(messageText)?.[1]?.toLowerCase()
  const slashMatches = slashQuery === undefined
    ? []
    : cannedReplies.filter(r => r.title.toLowerCase().replace(/\s+/g, '').includes(slashQuery)).slice(0, 6)

  const applySlashReply = (reply: CannedReply) => {
    const text = fillCannedVariables(reply.description)
    setMessageText(text)
    requestAnimationFrame(() => {
      textareaRef.current?.focus()
      textareaRef.current?.setSelectionRange(text.length, text.length)
    })
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (slashMatches.length > 0 && (e.key === 'Enter' || e.key === 'Tab') && !e.shiftKey) {
      e.preventDefault()
      applySlashReply(slashMatches[0])
      return
    }
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
        setShowReplyButtons(false)
        setShowMsgSearch(false)
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
      if (showReplyButtons && !target.closest('.chat-composer-popover-anchor')) {
        setShowReplyButtons(false)
      }
    }

    document.addEventListener('keydown', handleEscape)
    document.addEventListener('mousedown', handleClickOutside)
    return () => {
      document.removeEventListener('keydown', handleEscape)
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [showEmojiPicker, showAttachmentMenu, showCannedReplies, showReplyButtons])

  // Loaded once. activeOnly=true so the composer only offers replies that are switched on —
  // the management list is where inactive ones remain visible.
  useEffect(() => {
    void aiReplyService.getCannedReplies(true).then(setCannedReplies)
  }, [])

  /**
   * Inserts a canned reply at the caret rather than replacing the box, so an agent can type a
   * greeting, drop in a saved paragraph, and keep going.
   */
  const insertCannedReply = (raw: string) => {
    const text = fillCannedVariables(raw)
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

  // Taken from the conversation, not from the header filter: on "All Channels" both kinds of
  // thread are in the list, and how a thread renders is a property of the thread.
  const isEmailThread =
    activeConversation != null && normalizeChannel(activeConversation.channel) === 'email'

  const activeChannelKey = activeConversation ? normalizeChannel(activeConversation.channel) : null
  const activeChannelLabel = CHANNELS.find(c => c.key === activeChannelKey)?.label ?? activeConversation?.channel ?? ''
  /** The subject being discussed: the latest email that has one. WhatsApp threads have none. */
  const threadSubject = isEmailThread
    ? [...messages].reverse().find(m => m.subject?.trim())?.subject?.trim() || null
    : null
  const contactAddress = activeConversation
    ? (isEmailThread ? (activeConversation.email || activeConversation.phone) : activeConversation.phone)
    : ''

  const canStartNewEmail = isEmailThread && canSend
  const startNewEmail = () => {
    if (!canStartNewEmail) return
    setEmailCompose({ messageId: -1, mode: 'newEmail' })
  }

  const copyContactAddress = async () => {
    if (!contactAddress) return
    try {
      await navigator.clipboard.writeText(contactAddress)
      toast.success(isEmailThread ? 'Email address copied.' : 'Phone number copied.')
    } catch {
      toast.error('Could not copy to the clipboard.')
    }
  }

  const canInitiate = has('Chat.InitiateChat')

  return (
    <div className="chat-page">
      {/*
        One slim strip for what governs the whole inbox: which channel and connection are shown,
        which number or address replies go out from, and the agent's account. The channel picker
        belongs here, not to the list: it decides both the list and the composer.
      */}
      <header className="chat-topbar">
        <h1 className="sr-only">Inbox</h1>
        <div className="chat-topbar-scope">
          <ChannelPicker
            value={channelFilter}
            onChange={(value) => {
              setChannelFilter(value.startsWith('__unavailable_') ? ALL_CHANNELS : value)
            }}
          />
          {channelFilter !== ALL_CHANNELS && (
            <div className="chat-topbar-field">
              <SearchableSelect
                label="Connection"
                placeholder={
                  channelConnections.length === 0
                    ? (isEmailChannel ? 'No email connections' : 'No connections')
                    : 'Choose a connection'
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
          {channelFilter !== ALL_CHANNELS && (
            <div className="chat-topbar-field">
              <SearchableSelect
                label={isEmailConnectionSelected ? 'From address' : 'Sending number'}
                placeholder={
                  senderOptions.length === 0
                    ? (isEmailConnectionSelected ? 'No verified senders' : 'No numbers connected')
                    : (isEmailConnectionSelected ? 'Choose a from address' : 'Choose a number')
                }
                hideAllOption
                disabled={senderOptions.length === 0}
                value={fromNumber}
                options={senderOptions}
                onChange={setFromNumber}
              />
            </div>
          )}
          <button type="button" className="chat-icon-btn chat-refresh-btn" aria-label="Refresh conversations" title="Refresh conversations"
            onClick={refreshInbox} disabled={isLoadingConversations}>
            <RefreshCw size={16} aria-hidden="true" className={isLoadingConversations ? 'is-spinning' : undefined} />
          </button>
        </div>

        <div className="chat-topbar-actions">
          {showsEmail && canSend && (
            <button
              type="button"
              className="chat-new-email-btn"
              onClick={startNewEmail}
              disabled={!canStartNewEmail}
              title={canStartNewEmail ? 'Write a new email to this contact' : 'Open an email conversation to write a new email'}
              aria-label="New email"
            >
              <Plus size={15} aria-hidden="true" />
              <span className="chat-new-email-label">New email</span>
            </button>
          )}
          <AccountMenu size="sm" />
        </div>
      </header>

      {/* On narrow screens one pane shows at a time: the list, or the open conversation. */}
      <div ref={layoutRef} className={`chat-container-layout${activeConversation ? ' has-active' : ''}`}>
      <div className="chat-sidebar">
        <div className="chat-sidebar-header">
          <div className="chat-search-row">
            <SearchBar
              value={sidebarSearchQuery}
              onChange={setSidebarSearchQuery}
              placeholder={
                channelFilter === ALL_CHANNELS
                  ? 'Search name, email, phone, message…'
                  : isEmailChannel
                    ? 'Search name, email, subject…'
                    : 'Search name, phone, message…'
              }
            />
            <button
              type="button"
              className={`chat-icon-btn chat-filter-btn${showFilters ? ' active' : ''}`}
              aria-expanded={showFilters}
              aria-controls="chat-filter-panel"
              aria-label={activeFilterCount > 0 ? `Filters, ${activeFilterCount} applied` : 'Filters'}
              title="Filters"
              onClick={() => setShowFilters(open => !open)}
            >
              <SlidersHorizontal size={16} aria-hidden="true" />
              {activeFilterCount > 0 && <span className="chat-filter-count" aria-hidden="true">{activeFilterCount}</span>}
            </button>
          </div>

          {showFilters && (
            <div id="chat-filter-panel" className="chat-filter-panel" role="group" aria-label="Filter conversations">
              <div className="chat-filter-field">
                <span className="chat-sidebar-field-label">Status</span>
                <SearchableSelect
                  label="Status"
                  placeholder={labelOf(chatOptions.data?.stateFilters, filterDefaults.state)}
                  allValue="all"
                  hideAllOption
                  value={stateFilter}
                  options={(chatOptions.data?.stateFilters ?? []).map(o => ({ value: o.value, label: o.label }))}
                  onChange={setStateFilter}
                />
              </div>
              <div className="chat-filter-field">
                <span className="chat-sidebar-field-label">Owner</span>
                <SearchableSelect
                  label="Owner"
                  placeholder={labelOf(chatOptions.data?.assigneeFilters, filterDefaults.assignee)}
                  allValue=""
                  hideAllOption
                  value={assigneeFilter}
                  options={(chatOptions.data?.assigneeFilters ?? []).map(o => ({ value: o.value, label: o.label }))}
                  onChange={setAssigneeFilter}
                />
              </div>
              <div className="chat-filter-field">
                <label className="chat-sidebar-field-label" htmlFor="chat-rows-per-page">Rows per page</label>
                <select
                  id="chat-rows-per-page"
                  className="chat-native-select"
                  value={conversationPageSize}
                  onChange={(e) => { setConversationPageSize(Number(e.target.value)); setConversationPage(1) }}
                >
                  {listPageSizes.map(s => <option key={s} value={s}>{s}</option>)}
                </select>
              </div>
              {activeFilterCount > 0 && (
                <button type="button" className="chat-filter-clear" onClick={clearFilters}>Clear filters</button>
              )}
            </div>
          )}

          {quickViews.length > 0 && (
            <div className="chat-quick-views" role="tablist" aria-label="Inbox views">
              {quickViews.map(view => {
                const selected = activeQuickView?.value === view.value
                const count = quickViewCounts?.[view.value]
                return (
                  <button
                    key={view.value}
                    type="button"
                    role="tab"
                    aria-selected={selected}
                    className={`chat-quick-view${selected ? ' is-active' : ''}`}
                    title={view.description}
                    onClick={() => applyQuickView(view.readFilter, view.assigneeFilter)}
                  >
                    {view.label}
                    {count != null && <span className="chat-quick-count">{new Intl.NumberFormat().format(count)}</span>}
                  </button>
                )
              })}
            </div>
          )}
        </div>

        <div className="conversation-list-scroll">
          {(() => {
            // Judged against the selected channel's own connections, so an email connection is
            // never reported as "No WABA number connected".
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
                <p className="upload-sub-text">Loading conversations…</p>
              </div>
            ) : conversations.length === 0 ? (
              <div className="chat-sidebar-empty-state">
                <MessageSquare size={36} className="chat-sidebar-empty-icon" />
                <span className="chat-sidebar-empty-title">No conversations</span>
                <p className="chat-sidebar-empty-desc">
                  {activeQuickView?.description && activeQuickView.value !== quickViews[0]?.value
                    ? `Nothing here: ${activeQuickView.description.charAt(0).toLowerCase()}${activeQuickView.description.slice(1)}`
                    : 'Try another search, view or filter.'}
                </p>
              </div>
            ) : (
            pagedConversations.map((conversation) => {
              const isActive = conversation.id === activeConversationId
              const channelKey = normalizeChannel(conversation.channel)
              const state = conversation.conversationStatus ?? 'Open'
              return (
                <motion.button
                  key={conversation.id}
                  // A conversation that gets a new message slides to its new place instead of
                  // jumping there, so the agent sees what moved.
                  layout="position"
                  transition={ROW_REORDER}
                  type="button"
                  className={`conversation-item ${isActive ? 'active' : ''}${conversation.unreadCount > 0 ? ' is-unread' : ''}`}
                  aria-current={isActive ? 'true' : undefined}
                  onClick={() => selectConversation(conversation.id)}
                >
                  {/* The channel marker sits on the avatar: an operator scans for it, rather than reads it. */}
                  <span className="conversation-avatar-wrap" data-channel={channelKey}>
                    <Avatar name={conversation.name} size="medium" />
                    <span className="conversation-channel-dot" aria-hidden="true">
                      {channelKey === 'email' ? <Mail size={10} /> : <MessageCircle size={10} />}
                    </span>
                    <span className="sr-only">{CHANNELS.find(c => c.key === channelKey)?.label ?? conversation.channel}</span>
                  </span>

                  <div className="conversation-info-row">
                    <div className="conversation-name-badge-row">
                      <span className="conversation-contact-name">{conversation.name}</span>
                      <span className="conversation-time">{conversation.lastMessageTime}</span>
                    </div>
                    <div className="conversation-msg-preview-row">
                      <span className="conversation-preview-text">{messagePreview(conversation.lastMessage) || 'No messages yet'}</span>
                      {conversation.unreadCount > 0 && (
                        <span className="unread-count-bubble" aria-label={`${conversation.unreadCount} unread`}>
                          {conversation.unreadCount > 99 ? '99+' : conversation.unreadCount}
                        </span>
                      )}
                    </div>
                    <div className="conversation-ops-row">
                      <span className="conversation-state" data-state={state}>
                        {labelOf(chatOptions.data?.conversationStatuses, state)}
                      </span>
                      <ContactTypeBadge value={conversation.status} typeMap={typeMap} />
                      <SlaChip conversation={conversation} />
                      {conversation.assignedUserName && (
                        <span className="conversation-owner" title={`Assigned to ${conversation.assignedUserName}`}>
                          <UserRound size={11} aria-hidden="true" />{conversation.assignedUserName}
                        </span>
                      )}
                    </div>
                  </div>
                </motion.button>
              )
            })
          )})()}
        </div>

        {conversations.length > 0 && (
          <div className="chat-sidebar-footer">
            <span className="chat-sidebar-footer-count">
              {((currentConversationPage - 1) * conversationPageSize + 1).toLocaleString()}–
              {Math.min(currentConversationPage * conversationPageSize, listTotal ?? conversations.length).toLocaleString()}
              {' '}of{' '}
              {listTotal != null
                ? listTotal.toLocaleString()
                : `${conversations.length.toLocaleString()}${hasMoreConversations ? '+' : ''}`}
            </span>

            <div className="chat-sidebar-footer-nav">
              <button
                type="button"
                className="chat-sidebar-footer-nav-btn"
                onClick={() => setConversationPage(currentConversationPage - 1)}
                disabled={currentConversationPage <= 1}
                aria-label="Previous page"
              >
                <ChevronLeft size={14} aria-hidden="true" />
              </button>
              <button
                type="button"
                className="chat-sidebar-footer-nav-btn"
                onClick={() => goToConversationPage(currentConversationPage + 1)}
                disabled={currentConversationPage >= conversationPageCount || isLoadingMoreConversations}
                aria-label="Next page"
              >
                <ChevronRight size={14} aria-hidden="true" />
              </button>
            </div>

            {(chatOptions.data?.sortOrders?.length ?? 0) > 1 && (
              <select
                className="chat-sort-select"
                aria-label="Sort conversations"
                value={sortOrder || chatOptions.data?.sortOrders?.[0]?.value}
                onChange={(e) => setSortOrder(e.target.value)}
              >
                {chatOptions.data?.sortOrders.map(o => (
                  <option key={o.value} value={o.value} title={o.description ?? undefined}>{o.label}</option>
                ))}
              </select>
            )}
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
              <h2 className="chat-disconnected-title">This connection is disconnected</h2>
              <p className="chat-disconnected-desc">
                {selectedConnectionChannel === 'email'
                  ? 'This email connection is inactive or its credentials no longer work. Reconnect it to send and receive email.'
                  : 'This WhatsApp account is no longer connected. This can be caused by an expired or invalid token, a disconnected webhook, or a change in the Meta account settings.'}
              </p>
              <button
                className="chat-disconnected-connect-btn"
                onClick={() => navigate(selectedConnectionChannel === 'email'
                  ? '/connections'
                  : `/connect-waba?connectionId=${selectedConnectionId}`)}
              >
                <Link2 size={16} />
                Connect Account
              </button>
            </div>
          </div>
        ) : activeConversation ? (
          <AnimatePresence mode="wait" initial={false}>
          <motion.div
            key={activeConversation.id}
            className="chat-window-inner-layout"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={THREAD_SWITCH}
          >
            <div className="chat-window-header">
              <div className="chat-header-user-info">
                <button type="button" className="chat-icon-btn chat-mobile-back" aria-label="Back to conversations"
                  onClick={() => void selectConversation(null)}>
                  <ChevronLeft size={18} aria-hidden="true" />
                </button>
                <Avatar name={activeConversation.name} size="medium" />
                <div className="chat-header-identity">
                  <div className="chat-header-name-row">
                    <span className="conversation-contact-name">{activeConversation.name}</span>
                    <ContactTypeBadge value={activeConversation.status} typeMap={typeMap} />
                  </div>
                  {contactAddress && (
                    <div className="chat-header-address">
                      <span title={contactAddress}>{contactAddress}</span>
                      <button type="button" className="chat-copy-btn" onClick={() => void copyContactAddress()}
                        aria-label={isEmailThread ? 'Copy email address' : 'Copy phone number'} title="Copy">
                        <Copy size={12} aria-hidden="true" />
                      </button>
                    </div>
                  )}
                </div>
              </div>

              <div className="chat-header-actions">
                <ConversationOwnerSelect conversation={activeConversation} onChanged={() => void loadConversations()} />
                <button
                  type="button"
                  className={`chat-icon-btn${showMsgSearch ? ' active' : ''}`}
                  title="Search messages"
                  aria-label="Search messages"
                  aria-pressed={showMsgSearch}
                  onClick={() => setShowMsgSearch(!showMsgSearch)}
                >
                  <Search size={18} aria-hidden="true" />
                </button>
                <button
                  type="button"
                  className={`chat-icon-btn${showDetails ? ' active' : ''}`}
                  title={showDetails ? 'Hide details' : 'Show details'}
                  aria-label={showDetails ? 'Hide customer details' : 'Show customer details'}
                  aria-pressed={showDetails}
                  onClick={() => setDetailsOpen(!showDetails)}
                >
                  <PanelRight size={18} aria-hidden="true" />
                </button>
                <Menu
                  open={headerMenuOpen}
                  onOpenChange={setHeaderMenuOpen}
                  align="end"
                  ariaLabel="Conversation actions"
                  trigger={(props) => (
                    <button {...props} type="button" className="chat-icon-btn" title="More actions" aria-label="More actions">
                      <MoreVertical size={18} aria-hidden="true" />
                    </button>
                  )}
                >
                  {canSend && (
                    <MenuItem onSelect={() => void toggleConversationStatus(activeConversation, () => void loadConversations())}>
                      {statusToggleFor(activeConversation).status === 'Open'
                        ? <RotateCcw size={14} aria-hidden="true" />
                        : <CheckCircle2 size={14} aria-hidden="true" />}
                      {statusToggleFor(activeConversation).label}
                    </MenuItem>
                  )}
                  {canStartNewEmail && (
                    <MenuItem onSelect={startNewEmail}>
                      <Mail size={14} aria-hidden="true" />New email
                    </MenuItem>
                  )}
                  {!isEmailThread && canInitiate && (
                    <MenuItem onSelect={handleOpenTemplateModal}>
                      <MessageSquare size={14} aria-hidden="true" />Send template
                    </MenuItem>
                  )}
                  {canDelete && (
                    <MenuItem destructive onSelect={handleDeleteChat}>
                      <Trash2 size={14} aria-hidden="true" />Delete conversation
                    </MenuItem>
                  )}
                </Menu>
              </div>
            </div>

            <div className="chat-subject-bar">
              <div className="chat-subject-main">
                <h2 className="chat-subject-title" title={threadSubject ?? undefined}>
                  {isEmailThread ? (threadSubject ?? 'No subject') : `${activeChannelLabel} conversation`}
                </h2>
                <div className="chat-subject-chips">
                  <ConversationStatusBadge conversation={activeConversation} />
                  <span className="chat-channel-chip" data-channel={activeChannelKey ?? undefined}>
                    {isEmailThread ? <Mail size={11} aria-hidden="true" /> : <MessageCircle size={11} aria-hidden="true" />}
                    {activeChannelLabel}
                  </span>
                  <SlaChip conversation={activeConversation} />
                  {!isEmailThread && (
                    <span className={`chat-window-chip${windowStatus.open ? (windowStatus.closingSoon ? ' is-soon' : ' is-open') : ' is-closed'}`}
                      title={`WhatsApp allows free-form replies for ${chatOptions.data?.serviceWindowHours ?? 24} hours after the customer last wrote.`}>
                      <Clock size={11} aria-hidden="true" />{windowStatus.label}
                    </span>
                  )}
                </div>
              </div>
              {activeConversation.lastMessageAt && (
                <time className="chat-subject-date" dateTime={activeConversation.lastMessageAt}>
                  {formatAbsoluteDateTime(activeConversation.lastMessageAt)}
                </time>
              )}
            </div>

            <div className="chat-window-content-row">
              <div className="chat-window-messages-column">
                {showMsgSearch && (
                  <div className="chat-window-search-banner-floating">
                    <div className="chat-window-search-input-wrapper">
                      <Search size={16} className="chat-window-search-icon" aria-hidden="true" />
                      <input
                        type="search"
                        className="chat-window-search-input"
                        placeholder="Search messages…"
                        aria-label="Search messages in this conversation"
                        value={msgSearchQuery}
                        onChange={(e) => setMsgSearchQuery(e.target.value)}
                        autoFocus
                      />
                    </div>
                    <button
                      type="button"
                      className="chat-window-search-close-btn"
                      aria-label="Close message search"
                      onClick={() => { setMsgSearchQuery(''); setShowMsgSearch(false); }}
                    >
                      <X size={18} aria-hidden="true" />
                    </button>
                  </div>
                )}

                <div ref={messagesContainerRef} className={`chat-messages-container${isEmailThread ? ' email-mode' : ''}`}
                  data-channel={activeChannelKey ?? undefined}>
                  {threadError && (
                    <div className="chat-thread-error" role="status">
                      <AlertCircle size={14} />
                      <span>{threadError} Showing the messages already loaded.</span>
                      <button type="button" onClick={() => void resyncActiveMessages()}>Retry</button>
                    </div>
                  )}
                  {hasOlderMessages && !isLoading && !msgSearchQuery.trim() && (
                    <div className="chat-load-older">
                      <button type="button" onClick={() => void loadOlderMessages()} disabled={isLoadingOlderMessages}>
                        {isLoadingOlderMessages ? 'Loading…' : 'Load earlier messages'}
                      </button>
                    </div>
                  )}
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
                      const showDateDivider = startsNewDay(message, previous)

                      const isSelected = selectedMessageIds.includes(message.id)

                      return (
                        <div
                          key={message.id}
                          data-message-id={message.id}
                          className={`chat-bubble-row${selectionMode ? ' selectable' : ''}${isSelected ? ' selected' : ''}`}
                          onContextMenu={(e) => openMessageMenu(e, message.id)}
                          onTouchStart={(e) => startLongPress(e, message.id)}
                          onTouchEnd={cancelLongPress}
                          onTouchMove={cancelLongPress}
                          onClick={selectionMode ? () => toggleMessageSelected(message.id) : undefined}
                        >
                          {showDateDivider && (
                            <div className="chat-date-divider"><span>{formatDayDivider(message.createdAt)}</span></div>
                          )}

                          {selectionMode && (
                            <span className={`chat-bubble-check${isSelected ? ' checked' : ''}`} aria-hidden="true">
                              {isSelected && <Check size={12} />}
                            </span>
                          )}

                          {normalizeChannel(message.channel) === 'email' ? (
                            <EmailThreadMessage
                              message={message}
                              onRespond={canSend ? (mode) => setEmailCompose({ messageId: message.id, mode }) : undefined}
                            />
                          ) : (
                          <div className={getBubbleClass(message)}>
                            {message.mediaUrl && (
                              <div className="chat-bubble-media-wrapper">
                                {message.mediaType === 'image' && (
                                  <img
                                    src={resolveMediaUrl(message.mediaUrl)}
                                    alt={message.mediaFileName || 'Image'}
                                    className="chat-bubble-media-image"
                                    onClick={() => window.open(resolveMediaUrl(message.mediaUrl) || undefined, '_blank')}
                                  />
                                )}
                                {message.mediaType === 'video' && (
                                  <video
                                    src={resolveMediaUrl(message.mediaUrl)}
                                    controls
                                    className="chat-bubble-media-video"
                                  />
                                )}
                                {message.mediaType === 'document' && (
                                  <a
                                    href={resolveMediaUrl(message.mediaUrl)}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    className="chat-bubble-media-document-card"
                                  >
                                    <FileText size={24} className="document-card-icon" />
                                    <div className="document-card-info">
                                      <span className="document-card-name" title={message.mediaFileName || undefined}>
                                        {message.mediaFileName || 'Document'}
                                      </span>
                                      <span className="document-card-size">
                                        Open document
                                      </span>
                                    </div>
                                  </a>
                                )}
                              </div>
                            )}

                            <RichMessage text={message.text} search={msgSearchQuery} />

                            <div className="chat-bubble-time-row">
                              <span className="conversation-time">{message.time}</span>
                              {message.type === 'outgoing' && <MessageStatus message={message} />}
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
                </div>

                {isEmailThread ? (
                  canSend && composeTarget && (activeEmailSource || composeTarget.mode === 'newEmail') ? (
                    <EmailComposer
                      conversationId={activeConversationId!}
                      source={activeEmailSource ?? messages[messages.length - 1] ?? ({} as Message)}
                      fromAddress={emailThreadFromAddress}
                      mode={composeTarget.mode}
                      onSent={() => {
                        setEmailCompose(null)
                        void refreshActiveMessages()
                        // Also refresh the inbox so the preview + timestamp update immediately
                        void loadConversations()
                      }}
                      onCancel={() => setEmailCompose(null)}
                    />
                  ) : !canSend ? (
                    <div className="chat-composer-readonly">
                      <Lock size={15} aria-hidden="true" />
                      <span>You have read-only access to this conversation.</span>
                    </div>
                  ) : null
                ) : !isEmailThread && !windowStatus.open ? (
                  <div className="chat-window-limit-banner">
                    <div className="chat-window-limit-left">
                      <AlertTriangle size={20} className="chat-window-limit-icon" />
                      <div className="chat-window-limit-text-group">
                        <span className="chat-window-limit-title">Reply window closed</span>
                        <span className="chat-window-limit-description">
                          WhatsApp only allows free-form replies within {chatOptions.data?.serviceWindowHours ?? 24} hours of the customer's last message. Send an approved template to restart the conversation.
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
                        <span>Send template</span>
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
                      {slashMatches.length > 0 && (
                        <ul className="chat-slash-menu" role="listbox" aria-label="Canned replies">
                          {slashMatches.map((reply, i) => (
                            <li key={reply.id} role="option" aria-selected={i === 0}>
                              <button type="button" onMouseDown={e => { e.preventDefault(); applySlashReply(reply) }}>
                                <strong>/{reply.title}</strong>
                                <span>{reply.description}</span>
                              </button>
                            </li>
                          ))}
                        </ul>
                      )}
                      <textarea
                        ref={textareaRef}
                        className="chat-composer-textarea"
                        rows={1}
                        placeholder={`Reply to ${activeConversation.name}…  (Shift + Enter for a new line, / for canned replies)`}
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
                        hidden
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
                              {QUICK_EMOJIS.map(emoji => (
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
                          <ReplyButtonsPopover
                            open={showReplyButtons}
                            onOpenChange={open => {
                              setShowReplyButtons(open)
                              if (open) {
                                setShowCannedReplies(false)
                                setShowEmojiPicker(false)
                                setShowAttachmentMenu(false)
                              }
                            }}
                            value={replyButtons}
                            onChange={setReplyButtons}
                          />
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
                              <div className="canned-replies-head">Canned replies</div>
                              {cannedReplies.length === 0 ? (
                                <div className="canned-replies-empty">
                                  No canned replies yet. Add them in Setup › Canned replies.
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
                          <span className="upload-loading-indicator" role="status">Uploading…</span>
                        )}
                      </div>

                      <button
                        type="submit"
                        className="chat-composer-send-btn"
                        aria-label="Send message"
                        disabled={(!messageText.trim() && !uploadingMedia) || isSending}
                      >
                        <Send size={18} />
                      </button>
                    </div>
                  </form>
                )}
              </div>
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
          </motion.div>
          </AnimatePresence>
        ) : (
          <div className="chat-empty-state-container">
            <EmptyStateIllustration />
            <span className="chat-empty-state-text">Choose a conversation to read and reply</span>
          </div>
        )}
      </div>

      {activeConversation && showDetails && !isSelectedConnectionDisconnected && (
        <ChatContextPanel
          conversation={activeConversation}
          typeMap={typeMap}
          onClose={() => setDetailsOpen(false)}
          onChanged={() => void loadConversations()}
          onNewEmail={canStartNewEmail ? startNewEmail : undefined}
          onStartWhatsApp={!isEmailThread && canInitiate ? handleOpenTemplateModal : undefined}
          onOpenConversation={openConversationAnywhere}
        />
      )}

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

      <ConfirmationModal
        isOpen={showDeleteChatModal}
        title="Delete conversation"
        message="Delete this conversation? All of its messages are removed from OmniConnect."
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
    </div>
  )
}

/** A row moving to its new place in the list; quick enough not to delay reading. */
const ROW_REORDER = { type: 'tween', ease: [0.23, 1, 0.32, 1], duration: 0.28 } as const
/** Switching conversation: a short crossfade so the new thread is clearly a different one. */
const THREAD_SWITCH = { duration: 0.16, ease: 'easeOut' } as const

/** Only while the real-time connection is down; normally the server pushes changes. */
const FALLBACK_POLL_MS = 20_000
/** Where the details panel's open/closed choice is remembered. */
const DETAILS_PANEL_STORAGE_KEY = 'chat.detailsPanel'
const ACCOUNT_RETRY_MS = 5000
const ACCOUNT_RETRY_LIMIT = 6

const getBubbleClass = (message: Message) => {
  if (message.type === 'incoming') return 'chat-bubble-incoming'
  if (message.type === 'system' || message.status === 'failed') return 'chat-bubble-failed'
  return 'chat-bubble-outgoing'
}

export default Chat

/** The empty thread: a phone with two bubbles, drawn in the theme's colours (see Chat.css). */
const EmptyStateIllustration: React.FC = () => (
  <svg className="chat-empty-state-illustration" viewBox="0 0 200 200" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
    <rect className="ces-phone" x="65" y="20" width="70" height="140" rx="12" strokeWidth="3" />
    <line className="ces-phone-detail" x1="90" y1="26" x2="110" y2="26" strokeWidth="2" strokeLinecap="round" />
    <circle className="ces-phone-button" cx="100" cy="150" r="5" />
    <rect className="ces-bubble-out" x="25" y="50" width="35" height="15" rx="6" />
    <rect className="ces-bubble-out-line" x="30" y="55" width="20" height="2" rx="1" />
    <rect className="ces-bubble-out-line" x="30" y="60" width="10" height="2" rx="1" />
    <line className="ces-bubble-out-tail" x1="57" y1="62" x2="65" y2="65" />
    <rect className="ces-bubble-in" x="140" y="80" width="35" height="15" rx="6" />
    <rect className="ces-bubble-in-line" x="145" y="85" width="20" height="2" rx="1" />
    <rect className="ces-bubble-in-line" x="145" y="90" width="15" height="2" rx="1" />
    <line className="ces-bubble-in-tail" x1="140" y1="92" x2="135" y2="95" />
  </svg>
)
