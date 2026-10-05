import React, { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import {
  CheckCircle2, Copy, ExternalLink, Mail, MessageSquare, Pencil, Phone, Plus, RotateCcw, Trash2, X
} from 'lucide-react'
import { Avatar } from '../../components/Avatar/Avatar'
import Can from '../../components/Can/Can'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import usePermission from '../../hooks/usePermission'
import useReference from '../../hooks/useReference'
import { apiClient } from '../../services/apiClient'
import { activityLogService } from '../../services/activityLogService'
import { contactService } from '../../services/contacts/contactService'
import { referenceService, labelOf } from '../../services/referenceService'
import { CHANNELS, normalizeChannel } from '../../types/channel'
import type { Conversation } from '../../types/chat'
import type { ContactDetails } from '../../types/contacts'
import type { AuditLogModel } from '../../types/reporting'
import { formatAbsoluteDateTime, formatRelativeTime } from '../../utils/dateHelper'
import { badgeStyleFor, type ResolvedLookup } from '../../utils/lookupColors'
import { ContactTypeBadge } from './ContactTypeBadge'
import { ConversationStatusBadge, SlaChip } from './ConversationOwnerControls'
import { statusToggleFor, toggleConversationStatus } from './conversationStatus'
import './ChatContextPanel.css'

type PanelTab = 'customer' | 'conversation' | 'activity'

interface ContactNote {
  id: number
  content: string
  createdAt: string
}

interface ChatContextPanelProps {
  conversation: Conversation
  typeMap: Map<string, ResolvedLookup>
  onClose: () => void
  /** Re-read the inbox after a change made here. */
  onChanged: () => void
  /** Opens the composer for a new email. Absent where that is not possible. */
  onNewEmail?: () => void
  /** Opens the WhatsApp template picker. Absent where that is not possible. */
  onStartWhatsApp?: () => void
}

/** Audit entries per page in the Activity tab. */
const ACTIVITY_PAGE_SIZE = 10

const copyText = async (value: string, what: string) => {
  try {
    await navigator.clipboard.writeText(value)
    toast.success(`${what} copied.`)
  } catch {
    toast.error(`Could not copy the ${what.toLowerCase()}.`)
  }
}

const formatDate = (value?: string | null) =>
  value ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(value)) : null

/** A label/value row that disappears when there is no value, so empty fields do not pad the panel. */
const DetailRow: React.FC<{ label: string; children?: React.ReactNode }> = ({ label, children }) =>
  children == null || children === '' ? null : (
    <div className="ccp-row">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  )

/**
 * The inbox's third pane: who the customer is, the state of this conversation, and what has
 * happened to both. Everything shown is read from the server — the contact record, the
 * conversation, and the audit log — so it matches the Contacts and Audit Log pages exactly.
 */
export const ChatContextPanel: React.FC<ChatContextPanelProps> = ({
  conversation, typeMap, onClose, onChanged, onNewEmail, onStartWhatsApp
}) => {
  const { has } = usePermission()
  const canSeeActivity = has('ActivityLog.View')
  const chatOptions = useReference(referenceService.getChatOptions, 'chat-options')
  const baseId = useId()
  const [tab, setTab] = useState<PanelTab>('customer')

  const tabs = useMemo<{ key: PanelTab; label: string }[]>(() => [
    { key: 'customer', label: 'Customer' },
    { key: 'conversation', label: 'Conversation' },
    ...(canSeeActivity ? [{ key: 'activity' as const, label: 'Activity' }] : [])
  ], [canSeeActivity])
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({})

  const onTabKeyDown = (event: React.KeyboardEvent, index: number) => {
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
    if (!step) return
    event.preventDefault()
    const next = tabs[(index + step + tabs.length) % tabs.length]
    setTab(next.key)
    tabRefs.current[next.key]?.focus()
  }

  // ── Contact record ────────────────────────────────────────────────────────
  const [contact, setContact] = useState<ContactDetails | null>(null)
  const [contactState, setContactState] = useState<'loading' | 'ready' | 'unavailable'>('loading')

  useEffect(() => {
    let active = true
    setContact(null)
    setContactState('loading')
    contactService.getContactDetails(conversation.contactId)
      .then(details => { if (active) { setContact(details); setContactState('ready') } })
      .catch(() => { if (active) setContactState('unavailable') })
    return () => { active = false }
  }, [conversation.contactId])

  // ── Notes ─────────────────────────────────────────────────────────────────
  const [notes, setNotes] = useState<ContactNote[]>([])
  const [loadingNotes, setLoadingNotes] = useState(true)
  const [noteDraft, setNoteDraft] = useState('')
  const [addingNote, setAddingNote] = useState(false)
  const [savingNote, setSavingNote] = useState(false)
  const [noteToDelete, setNoteToDelete] = useState<number | null>(null)

  useEffect(() => {
    let active = true
    setLoadingNotes(true)
    setNotes([])
    setAddingNote(false)
    setNoteDraft('')
    apiClient.get(`/Contacts/${conversation.contactId}/notes`)
      .then(res => { if (active) setNotes(res.data?.data ?? []) })
      .catch(() => { /* The section says "No notes yet"; the rest of the panel still works. */ })
      .finally(() => { if (active) setLoadingNotes(false) })
    return () => { active = false }
  }, [conversation.contactId])

  const saveNote = async () => {
    const content = noteDraft.trim()
    if (!content || savingNote) return
    setSavingNote(true)
    try {
      const res = await apiClient.post(`/Contacts/${conversation.contactId}/notes`, { content })
      if (res.data?.success) {
        setNotes(prev => [res.data.data, ...prev])
        setNoteDraft('')
        setAddingNote(false)
        toast.success('Note added.')
      }
    } catch {
      toast.error('The note could not be saved. Try again.')
    } finally {
      setSavingNote(false)
    }
  }

  const deleteNote = async (noteId: number) => {
    setNoteToDelete(null)
    try {
      const res = await apiClient.delete(`/Contacts/${conversation.contactId}/notes/${noteId}`)
      if (res.data?.success) {
        setNotes(prev => prev.filter(n => n.id !== noteId))
        toast.success('Note deleted.')
      }
    } catch {
      toast.error('The note could not be deleted. Try again.')
    }
  }

  // ── Activity (audit log of this contact and this conversation) ─────────────
  const [activity, setActivity] = useState<AuditLogModel[]>([])
  const [activityPage, setActivityPage] = useState(1)
  const [activityPages, setActivityPages] = useState(0)
  const [activityState, setActivityState] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle')
  const activitySeq = useRef(0)

  const loadActivity = useCallback(async (page: number) => {
    const seq = ++activitySeq.current
    setActivityState('loading')
    try {
      const result = await activityLogService.getEntityHistory(
        [`Contact:${conversation.contactId}`, `ChatConversation:${conversation.id}`], page, ACTIVITY_PAGE_SIZE)
      if (seq !== activitySeq.current) return
      setActivity(prev => page === 1 ? result.items : [...prev, ...result.items])
      setActivityPage(page)
      setActivityPages(result.totalPages)
      setActivityState('ready')
    } catch {
      if (seq === activitySeq.current) setActivityState('error')
    }
  }, [conversation.contactId, conversation.id])

  // Read fresh each time the tab is opened or the conversation changes; it is a short indexed read.
  useEffect(() => {
    if (tab === 'activity' && canSeeActivity) void loadActivity(1)
  }, [tab, canSeeActivity, loadActivity])

  useEffect(() => {
    if (!tabs.some(t => t.key === tab)) setTab('customer')
  }, [tabs, tab])

  const navigate = useNavigate()
  const channel = normalizeChannel(conversation.channel)
  const channelLabel = CHANNELS.find(c => c.key === channel)?.label ?? conversation.channel ?? ''
  const status = conversation.conversationStatus ?? 'Open'
  const toggle = statusToggleFor(conversation)
  const email = contact?.email ?? conversation.email ?? null
  const phone = contact?.phone ?? conversation.phone ?? null
  const location = [contact?.city, contact?.state, contact?.country].filter(Boolean).join(', ')
  const tags = (contact?.tags ?? '').split(',').map(t => t.trim()).filter(Boolean)
  const groups = contact?.groups ?? (conversation.contactGroups ?? []).map((name, i) => ({ id: -i - 1, name, color: null }))

  return (
    <aside className="ccp" aria-label="Conversation details">
      <div className="ccp-head">
        <div className="ccp-tabs" role="tablist" aria-label="Details">
          {tabs.map((t, i) => (
            <button
              key={t.key}
              ref={el => { tabRefs.current[t.key] = el }}
              type="button"
              role="tab"
              id={`${baseId}-tab-${t.key}`}
              aria-selected={tab === t.key}
              aria-controls={`${baseId}-panel`}
              tabIndex={tab === t.key ? 0 : -1}
              className={`ccp-tab${tab === t.key ? ' is-active' : ''}`}
              onClick={() => setTab(t.key)}
              onKeyDown={e => onTabKeyDown(e, i)}
            >
              {t.label}
            </button>
          ))}
        </div>
        <button type="button" className="chat-icon-btn ccp-close" aria-label="Close details" onClick={onClose}>
          <X size={16} aria-hidden="true" />
        </button>
      </div>

      <div className="ccp-body" role="tabpanel" id={`${baseId}-panel`} aria-labelledby={`${baseId}-tab-${tab}`}>
        {tab === 'customer' && (
          <>
            <section className="ccp-identity">
              <Avatar name={conversation.name} size="large" />
              <div className="ccp-identity-text">
                <span className="ccp-name">{contact?.name ?? conversation.name}</span>
                <ContactTypeBadge value={contact?.type ?? conversation.status} typeMap={typeMap} />
              </div>
            </section>

            <section className="ccp-section">
              <ul className="ccp-contact-lines">
                {email && (
                  <li>
                    <Mail size={14} aria-hidden="true" />
                    <span className="ccp-ellipsis" title={email}>{email}</span>
                    <button type="button" className="ccp-mini-btn" aria-label="Copy email address" onClick={() => void copyText(email, 'Email address')}>
                      <Copy size={13} aria-hidden="true" />
                    </button>
                  </li>
                )}
                {phone && (
                  <li>
                    <Phone size={14} aria-hidden="true" />
                    <span className="ccp-ellipsis">{phone}</span>
                    <button type="button" className="ccp-mini-btn" aria-label="Copy phone number" onClick={() => void copyText(phone, 'Phone number')}>
                      <Copy size={13} aria-hidden="true" />
                    </button>
                  </li>
                )}
              </ul>
            </section>

            <section className="ccp-section">
              <h3 className="ccp-section-title">About</h3>
              {contactState === 'loading' ? (
                <p className="ccp-muted">Loading details…</p>
              ) : contactState === 'unavailable' ? (
                <p className="ccp-muted">The full contact record is not available to you.</p>
              ) : (
                <dl className="ccp-list">
                  <DetailRow label="Company">{contact?.company}</DetailRow>
                  <DetailRow label="Website">{contact?.website}</DetailRow>
                  <DetailRow label="Location">{location}</DetailRow>
                  <DetailRow label="Time zone">{contact?.timeZone}</DetailRow>
                  <DetailRow label="Source">{contact?.source || conversation.source}</DetailRow>
                  <DetailRow label="Customer since">{formatDate(contact?.createdAt ?? conversation.contactCreatedAt)}</DetailRow>
                  <DetailRow label="About">{contact?.description}</DetailRow>
                </dl>
              )}
            </section>

            <section className="ccp-section">
              <h3 className="ccp-section-title">Tags</h3>
              {tags.length === 0 ? <p className="ccp-muted">No tags</p> : (
                <ul className="ccp-chips">{tags.map(tag => <li key={tag} className="ccp-chip">{tag}</li>)}</ul>
              )}
            </section>

            <section className="ccp-section">
              <h3 className="ccp-section-title">Groups</h3>
              {groups.length === 0 ? <p className="ccp-muted">Not in any group</p> : (
                <ul className="ccp-chips">
                  {groups.map(g => <li key={g.id} className="ccp-chip" style={badgeStyleFor(g.color)}>{g.name}</li>)}
                </ul>
              )}
            </section>

            <section className="ccp-section">
              <div className="ccp-section-head">
                <h3 className="ccp-section-title">Notes</h3>
                <Can permission="Contact.Edit">
                  <button type="button" className="ccp-mini-btn" aria-label="Add a note" aria-expanded={addingNote}
                    onClick={() => setAddingNote(open => !open)}>
                    <Plus size={14} aria-hidden="true" />
                  </button>
                </Can>
              </div>
              {addingNote && (
                <div className="ccp-note-form">
                  <textarea
                    className="form-control"
                    aria-label="Note"
                    placeholder="Write a note…"
                    rows={3}
                    value={noteDraft}
                    onChange={e => setNoteDraft(e.target.value)}
                  />
                  <div className="ccp-note-actions">
                    <button type="button" className="btn btn-sm btn-light" onClick={() => setAddingNote(false)}>Cancel</button>
                    <button type="button" className="btn btn-sm btn-primary" disabled={!noteDraft.trim() || savingNote} onClick={() => void saveNote()}>
                      {savingNote ? 'Saving…' : 'Save'}
                    </button>
                  </div>
                </div>
              )}
              {loadingNotes ? <p className="ccp-muted">Loading notes…</p> : notes.length === 0 ? (
                <p className="ccp-muted">No notes yet</p>
              ) : (
                <ul className="ccp-notes">
                  {notes.map(note => (
                    <li key={note.id} className="ccp-note">
                      <div className="ccp-note-meta">
                        <time dateTime={note.createdAt} title={formatAbsoluteDateTime(note.createdAt)}>{formatRelativeTime(note.createdAt)}</time>
                        <Can permission="Contact.Edit">
                          <button type="button" className="ccp-mini-btn is-danger" aria-label="Delete this note" onClick={() => setNoteToDelete(note.id)}>
                            <Trash2 size={12} aria-hidden="true" />
                          </button>
                        </Can>
                      </div>
                      <p>{note.content}</p>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </>
        )}

        {tab === 'conversation' && (
          <>
            <section className="ccp-section">
              <h3 className="ccp-section-title">Status</h3>
              <div className="ccp-status">
                <ConversationStatusBadge conversation={conversation} />
                <SlaChip conversation={conversation} />
              </div>
              <p className="ccp-muted">{chatOptions.data?.conversationStatuses.find(s => s.value === status)?.description}</p>
            </section>
            <section className="ccp-section">
              <h3 className="ccp-section-title">Details</h3>
              <dl className="ccp-list">
                <DetailRow label="Assigned to">{conversation.assignedUserName ?? 'Unassigned'}</DetailRow>
                <DetailRow label="Channel">{channelLabel}</DetailRow>
                <DetailRow label="Connection">{conversation.connectionName}</DetailRow>
                <DetailRow label="Status">{labelOf(chatOptions.data?.conversationStatuses, status)}</DetailRow>
                <DetailRow label="Unread">{conversation.unreadCount > 0 ? new Intl.NumberFormat().format(conversation.unreadCount) : 'None'}</DetailRow>
                <DetailRow label="First reply due">{conversation.firstResponseDueAt ? formatAbsoluteDateTime(conversation.firstResponseDueAt) : null}</DetailRow>
                <DetailRow label="Resolve by">{conversation.resolveDueAt ? formatAbsoluteDateTime(conversation.resolveDueAt) : null}</DetailRow>
                <DetailRow label="Last activity">{conversation.lastMessageAt ? formatAbsoluteDateTime(conversation.lastMessageAt) : conversation.lastMessageTime}</DetailRow>
              </dl>
            </section>
          </>
        )}

        {tab === 'activity' && canSeeActivity && (
          <section className="ccp-section">
            <div className="ccp-section-head">
              <h3 className="ccp-section-title">Recent activity</h3>
              <button type="button" className="ccp-link" onClick={() => navigate('/audit-log?tab=audits')}>
                Audit log <ExternalLink size={12} aria-hidden="true" />
              </button>
            </div>
            {activityState === 'error' ? (
              <p className="ccp-muted">The activity could not be loaded. <button type="button" className="ccp-link" onClick={() => void loadActivity(1)}>Retry</button></p>
            ) : activity.length === 0 ? (
              <p className="ccp-muted">{activityState === 'loading' ? 'Loading activity…' : 'Nothing recorded for this customer yet.'}</p>
            ) : (
              <>
                <ol className="ccp-timeline">
                  {activity.map(entry => (
                    <li key={entry.id} className={entry.status === 'Failed' ? 'is-failed' : undefined}>
                      <p className="ccp-timeline-text">{entry.description || entry.event}</p>
                      <p className="ccp-timeline-meta">
                        {entry.user || 'System'} · <time dateTime={entry.time} title={formatAbsoluteDateTime(entry.time)}>{formatRelativeTime(entry.time)}</time>
                      </p>
                    </li>
                  ))}
                </ol>
                {activityPage < activityPages && (
                  <button type="button" className="ccp-more" disabled={activityState === 'loading'} onClick={() => void loadActivity(activityPage + 1)}>
                    {activityState === 'loading' ? 'Loading…' : 'Show older activity'}
                  </button>
                )}
              </>
            )}
          </section>
        )}
      </div>

      <div className="ccp-actions">
        <h3 className="ccp-section-title">Quick actions</h3>
        <div className="ccp-action-grid">
          <Can permission="Contact.Edit">
            <button type="button" className="ccp-action" onClick={() => navigate(`/contacts/contact/edit/${conversation.contactId}`)}>
              <Pencil size={14} aria-hidden="true" />Edit contact
            </button>
          </Can>
          {onNewEmail && (
            <button type="button" className="ccp-action" onClick={onNewEmail}>
              <Mail size={14} aria-hidden="true" />New email
            </button>
          )}
          {onStartWhatsApp && (
            <button type="button" className="ccp-action" onClick={onStartWhatsApp}>
              <MessageSquare size={14} aria-hidden="true" />Send template
            </button>
          )}
          <Can permission="Chat.Send">
            <button type="button" className="ccp-action" onClick={() => void toggleConversationStatus(conversation, onChanged)}>
              {toggle.status === 'Open' ? <RotateCcw size={14} aria-hidden="true" /> : <CheckCircle2 size={14} aria-hidden="true" />}
              {toggle.label}
            </button>
          </Can>
        </div>
      </div>

      <ConfirmationModal
        isOpen={noteToDelete !== null}
        title="Delete Note"
        message="Delete this note? It is removed for everyone and cannot be recovered."
        confirmText="Delete Note"
        cancelText="Cancel"
        onConfirm={() => { if (noteToDelete !== null) void deleteNote(noteToDelete) }}
        onCancel={() => setNoteToDelete(null)}
        isDestructive
        showWarningIcon
      />
    </aside>
  )
}

export default ChatContextPanel
