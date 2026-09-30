import React, { useEffect, useState } from 'react'
import { CheckCircle2, RotateCcw, UserRound, Clock } from 'lucide-react'
import toast from 'react-hot-toast'
import Can from '../../components/Can/Can'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import useReference from '../../hooks/useReference'
import { chatService, type AssignableAgent } from '../../services/chat/chatService'
import { referenceService, labelOf } from '../../services/referenceService'
import type { Conversation } from '../../types/chat'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'

interface ConversationOwnerControlsProps {
  conversation: Conversation
  /** Re-read the inbox after a change (the server also pushes it live). */
  onChanged: () => void
}

/** Badge colour per conversation state: needs reply is the one that asks for action. */
const STATUS_BADGE: Record<string, string> = { Open: 'info', Pending: 'warning', Resolved: 'success', Closed: 'closed' }

/** Owner and status of the open conversation: assign it, resolve it, reopen it. */
export const ConversationOwnerControls: React.FC<ConversationOwnerControlsProps> = ({ conversation, onChanged }) => {
  const options = useReference(referenceService.getChatOptions, 'chat-options')
  const [agents, setAgents] = useState<AssignableAgent[] | null>(null)
  const [busy, setBusy] = useState(false)
  const status = conversation.conversationStatus ?? 'Open'
  const isDone = status === 'Resolved' || status === 'Closed'
  const statusLabel = labelOf(options.data?.conversationStatuses, status)

  useEffect(() => {
    let active = true
    setAgents(null)
    chatService.getAssignableAgents(conversation.connectionId ?? undefined)
      .then(list => { if (active) setAgents(list) })
      .catch(() => { if (active) setAgents([]) })
    return () => { active = false }
  }, [conversation.connectionId])

  const run = async (action: () => Promise<unknown>, success: string) => {
    setBusy(true)
    try {
      await action()
      toast.success(success)
      onChanged()
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'The change could not be saved. Try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="chat-owner-controls">
      <span title={options.data?.conversationStatuses.find(s => s.value === status)?.description ?? undefined}>
        <StatusBadge type={STATUS_BADGE[status] ?? 'closed'} text={statusLabel} />
      </span>

      <Can permission="Chat.Assign" fallback={
        conversation.assignedUserName
          ? <span className="chat-owner-name" title="Assigned to"><UserRound size={14} aria-hidden="true" /> {conversation.assignedUserName}</span>
          : null
      }>
        <select
          className="chat-owner-select"
          aria-label="Assigned to"
          disabled={busy || agents === null}
          value={conversation.assignedUserId ?? ''}
          onChange={e => {
            const userId = e.target.value ? Number(e.target.value) : null
            const name = agents?.find(a => a.id === userId)?.name
            void run(() => chatService.assignConversation(conversation.id, userId),
              userId ? `Assigned to ${name ?? 'the selected agent'}.` : 'Conversation unassigned.')
          }}
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
      </Can>

      <Can permission="Chat.Send">
        {isDone ? (
          <button type="button" className="chat-icon-btn" title="Reopen conversation" aria-label="Reopen conversation" disabled={busy}
            onClick={() => run(() => chatService.setConversationStatus(conversation.id, 'Open'), 'Conversation reopened.')}>
            <RotateCcw size={18} aria-hidden="true" />
          </button>
        ) : (
          <button type="button" className="chat-icon-btn" title="Mark resolved" aria-label="Mark conversation resolved" disabled={busy}
            onClick={() => run(() => chatService.setConversationStatus(conversation.id, 'Resolved'), 'Conversation resolved.')}>
            <CheckCircle2 size={18} aria-hidden="true" />
          </button>
        )}
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

export default ConversationOwnerControls
