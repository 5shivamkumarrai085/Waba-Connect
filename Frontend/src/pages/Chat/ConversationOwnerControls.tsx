import React, { useEffect, useState } from 'react'
import { ChevronDown, UserRound, Clock } from 'lucide-react'
import toast from 'react-hot-toast'
import Can from '../../components/Can/Can'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import useReference from '../../hooks/useReference'
import { chatService, type AssignableAgent } from '../../services/chat/chatService'
import { referenceService, labelOf } from '../../services/referenceService'
import type { Conversation } from '../../types/chat'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'

/** Badge colour per conversation state: needs reply is the one that asks for action. */
const STATUS_BADGE: Record<string, string> = { Open: 'info', Pending: 'warning', Resolved: 'success', Closed: 'closed' }

const statusOf = (conversation: Conversation) => conversation.conversationStatus ?? 'Open'

/** The conversation's state, labelled from the server catalogue, with its meaning as a tooltip. */
export const ConversationStatusBadge: React.FC<{ conversation: Conversation }> = ({ conversation }) => {
  const options = useReference(referenceService.getChatOptions, 'chat-options')
  const status = statusOf(conversation)
  return (
    <span title={options.data?.conversationStatuses.find(s => s.value === status)?.description ?? undefined}>
      <StatusBadge type={STATUS_BADGE[status] ?? 'closed'} text={labelOf(options.data?.conversationStatuses, status)} />
    </span>
  )
}

interface ConversationOwnerSelectProps {
  conversation: Conversation
  /** Re-read the inbox after a change (the server also pushes it live). */
  onChanged: () => void
}

/** "Assigned to": who owns the open conversation. Read-only text without Chat.Assign. */
export const ConversationOwnerSelect: React.FC<ConversationOwnerSelectProps> = ({ conversation, onChanged }) => {
  const [agents, setAgents] = useState<AssignableAgent[] | null>(null)
  const [busy, setBusy] = useState(false)
  const ownerName = conversation.assignedUserName ?? 'Unassigned'

  useEffect(() => {
    let active = true
    setAgents(null)
    chatService.getAssignableAgents(conversation.connectionId ?? undefined)
      .then(list => { if (active) setAgents(list) })
      .catch(() => { if (active) setAgents([]) })
    return () => { active = false }
  }, [conversation.connectionId])

  const assign = async (userId: number | null) => {
    setBusy(true)
    try {
      await chatService.assignConversation(conversation.id, userId)
      const name = agents?.find(a => a.id === userId)?.name
      toast.success(userId ? `Assigned to ${name ?? 'the selected agent'}.` : 'Conversation unassigned.')
      onChanged()
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'The change could not be saved. Try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="chat-owner">
      <span className="chat-owner-caption">Assigned to</span>
      <Can permission="Chat.Assign" fallback={
        <span className="chat-owner-name" title={ownerName}><UserRound size={14} aria-hidden="true" />{ownerName}</span>
      }>
        {/* The native select keeps keyboard and screen-reader behaviour; it sits invisibly over a
            label that shows only the owner's name. The workload count belongs in the choices. */}
        <span className={`chat-owner-field${busy || agents === null ? ' is-busy' : ''}`}>
          <UserRound size={14} aria-hidden="true" />
          <span className="chat-owner-current" title={ownerName}>{ownerName}</span>
          <ChevronDown size={14} aria-hidden="true" />
          <select
            className="chat-owner-select"
            aria-label="Assigned to"
            disabled={busy || agents === null}
            value={conversation.assignedUserId ?? ''}
            onChange={e => void assign(e.target.value ? Number(e.target.value) : null)}
          >
            {agents === null ? (
              <option value={conversation.assignedUserId ?? ''}>Loading agents…</option>
            ) : (
              <>
                <option value="">Unassigned</option>
                {conversation.assignedUserId && !agents.some(a => a.id === conversation.assignedUserId) && (
                  <option value={conversation.assignedUserId}>{conversation.assignedUserName ?? 'Current owner'}</option>
                )}
                {agents.map(a => (
                  <option key={a.id} value={a.id}>
                    {a.name} · {new Intl.NumberFormat().format(a.openConversations)} open
                  </option>
                ))}
              </>
            )}
          </select>
        </span>
      </Can>
    </div>
  )
}

/** Re-renders every `intervalMs`, so a countdown stays current without a page refresh. */
const useNow = (intervalMs: number) => {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs)
    return () => window.clearInterval(id)
  }, [intervalMs])
  return now
}

/** Minutes before the deadline at which the chip turns amber. */
const DUE_SOON_MINUTES = 15

/** "Reply by 14:30" / "SLA breached" for a conversation. Null when no SLA applies. */
export const SlaChip: React.FC<{ conversation: Conversation }> = ({ conversation }) => {
  const now = useNow(30_000)
  if (conversation.slaBreached) {
    return <span className="chat-sla-chip is-breached" role="status"><Clock size={11} aria-hidden="true" />SLA breached</span>
  }
  if (!conversation.firstResponseDueAt) return null
  const due = new Date(conversation.firstResponseDueAt)
  const minutesLeft = (due.getTime() - now) / 60000
  const overdue = minutesLeft <= 0
  const time = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' }).format(due)
  const cls = overdue ? 'is-breached' : minutesLeft < DUE_SOON_MINUTES ? 'is-soon' : ''
  return (
    <span className={`chat-sla-chip ${cls}`} title={`First reply due ${formatAbsoluteDateTime(conversation.firstResponseDueAt)}`}>
      <Clock size={11} aria-hidden="true" />
      {overdue ? 'Reply overdue' : `Reply by ${time}`}
    </span>
  )
}
