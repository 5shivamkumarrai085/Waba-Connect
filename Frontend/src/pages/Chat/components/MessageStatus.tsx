import React from 'react'
import { AlertCircle, Check, CheckCheck, Clock3 } from 'lucide-react'
import type { Message } from '../../../types/chat'

/** What each delivery state means, in the agent's words (shown as the tick's tooltip). */
const statusTitle = (message: Message): string => {
  switch (message.status) {
    case 'sending': return 'Sending…'
    case 'pending':
      return message.whatsAppMessageId ? 'Accepted by WhatsApp, waiting for delivery.' : 'Waiting for WhatsApp to accept it.'
    case 'sent': return 'Sent'
    case 'delivered': return 'Delivered'
    case 'read': return 'Read'
    case 'failed': return message.errorMessage || 'Not delivered'
    default: return 'Status pending'
  }
}

const statusIcon = (status?: string) => {
  switch (status) {
    case 'failed': return <AlertCircle size={13} />
    case 'sent': return <Check size={13} />
    case 'delivered':
    case 'read': return <CheckCheck size={13} />
    default: return <Clock3 size={12} />
  }
}

/** The tick(s) under an outgoing message: clock, sent, delivered, read (highlighted), or failed. */
export const MessageStatus: React.FC<{ message: Message }> = ({ message }) => {
  const state = message.status ?? 'pending'
  return (
    <span className="chat-bubble-status" data-status={state} title={statusTitle(message)}>
      {statusIcon(state)}
      <span className="sr-only">{statusTitle(message)}</span>
    </span>
  )
}
