import toast from 'react-hot-toast'
import { chatService } from '../../services/chat/chatService'
import type { Conversation } from '../../types/chat'

type ConversationStatus = 'Open' | 'Pending' | 'Resolved' | 'Closed'

const statusOf = (conversation: Conversation) => conversation.conversationStatus ?? 'Open'

/**
 * The one status change an agent makes by hand: resolve an active conversation, or reopen a
 * finished one. Pending and Closed are set by the system (an agent reply, the quiet-period job).
 */
export const statusToggleFor = (conversation: Conversation): { status: ConversationStatus; label: string; done: string } => {
  const status = statusOf(conversation)
  return status === 'Resolved' || status === 'Closed'
    ? { status: 'Open', label: 'Reopen conversation', done: 'Conversation reopened.' }
    : { status: 'Resolved', label: 'Mark as resolved', done: 'Conversation resolved.' }
}

/** Applies statusToggleFor, with the outcome as a toast. */
export const toggleConversationStatus = async (conversation: Conversation, onChanged: () => void) => {
  const next = statusToggleFor(conversation)
  try {
    await chatService.setConversationStatus(conversation.id, next.status)
    toast.success(next.done)
    onChanged()
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'The change could not be saved. Try again.')
  }
}
