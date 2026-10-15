import React from 'react'
import { ArrowLeftRight, MapPin, UserRound, Workflow } from 'lucide-react'

/**
 * Structured messages are stored as text the server writes in one shape, "[Kind|Key: value|…]",
 * so the inbox, exports and logs all read them. This module is the only place that parses them;
 * it used to be done three slightly different ways in the chat page.
 */
export interface RichCard {
  kind: string
  fields: Record<string, string>
}

const CARD_KINDS = ['Location', 'ContactCard', 'FlowSent', 'FlowReply', 'Transfer']

/** "[Kind|Key: value|Key: value]" for one of the known kinds, or null. */
export const parseRichCard = (text: string, kinds: string[] = CARD_KINDS): RichCard | null => {
  if (!text.startsWith('[') || !text.endsWith(']')) return null
  const parts = text.slice(1, -1).split('|')
  const kind = parts[0]
  if (!kinds.includes(kind)) return null
  const fields: Record<string, string> = {}
  for (const part of parts.slice(1)) {
    const idx = part.indexOf(':')
    if (idx > 0) fields[part.substring(0, idx).trim()] = part.substring(idx + 1).trim()
  }
  return { kind, fields }
}

/** A trailing "[Buttons: a | b]" (agent replies) or "[Buttons: a, b]" (bot replies) after the text. */
const BUTTONS_PATTERN = /\n*\[Buttons:\s*([^\]]*)\]\s*$/

export const splitReplyButtons = (text: string): { body: string; buttons: string[] } => {
  const match = BUTTONS_PATTERN.exec(text)
  if (!match) return { body: text, buttons: [] }
  const raw = match[1]
  const buttons = (raw.includes('|') ? raw.split('|') : raw.split(',')).map(b => b.trim()).filter(Boolean)
  return { body: text.slice(0, match.index), buttons }
}

const ATTACHMENT_PATTERN = /\[Attachment:[^\]]+\]\s*/g

/** The visible text of a message: attachment markers removed (the attachment renders itself). */
export const visibleText = (text: string): string => text.replace(ATTACHMENT_PATTERN, '')

/**
 * One line for a list preview. Conversation previews are cut by the server, so a card's closing
 * bracket may be missing; the kind and the fields that survived are still read.
 */
export const messagePreview = (text: string | null | undefined): string => {
  if (!text) return ''
  const card = new RegExp(`^\\[(${CARD_KINDS.join('|')})\\|`).exec(text)
  if (card) {
    const fields: Record<string, string> = {}
    for (const part of text.slice(1).replace(/\]$/, '').split('|').slice(1)) {
      const idx = part.indexOf(':')
      if (idx > 0) fields[part.substring(0, idx).trim()] = part.substring(idx + 1).trim()
    }
    switch (card[1]) {
      case 'Location': return `Location${fields.Name ? `: ${fields.Name}` : ''}`
      case 'ContactCard': return `Contact${fields.Name ? `: ${fields.Name}` : ''}`
      case 'FlowSent': return `Form sent${fields.Flow ? `: ${fields.Flow}` : ''}`
      case 'FlowReply': return `Form submitted${fields.Flow ? `: ${fields.Flow}` : ''}`
      case 'Transfer': return `Transfer${fields.Status ? `: ${fields.Status}` : ''}${fields.Ref ? ` (${fields.Ref})` : ''}`
    }
  }
  const { body, buttons } = splitReplyButtons(visibleText(text))
  const line = body.replace(/\s+/g, ' ').trim()
  return line || (buttons.length ? buttons.join(' / ') : text.startsWith('[Attachment:') ? 'Attachment' : '')
}

/** Escapes the agent's search text so it is matched literally, never as a pattern. */
const escapeForRegExp = (value: string) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

/** Text with the in-thread search term marked. */
export const HighlightedText: React.FC<{ text: string; search: string }> = ({ text, search }) => {
  const term = search.trim()
  if (!term) return <>{text}</>
  const parts = text.split(new RegExp(`(${escapeForRegExp(term)})`, 'gi'))
  return (
    <>
      {parts.map((part, i) =>
        part.toLowerCase() === term.toLowerCase() ? <mark key={i} className="search-highlight">{part}</mark> : part
      )}
    </>
  )
}

const mapsUrl = (f: Record<string, string>) => {
  const { Lat: lat, Lng: lng, Name: name = '', Addr: addr = '' } = f
  return lat && lng
    ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${lat},${lng}`)}`
    : `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${name} ${addr}`.trim())}`
}

const CardHead: React.FC<{ icon: React.ReactNode; title: string; subtitle?: string; divider?: boolean }> = ({ icon, title, subtitle, divider }) => (
  <div className={`chat-rich-card-head${divider ? ' has-divider' : ''}`}>
    <span className="chat-rich-card-icon" aria-hidden="true">{icon}</span>
    <div className="chat-rich-card-title">
      <span>{title}</span>
      {subtitle && <small translate="no">{subtitle}</small>}
    </div>
  </div>
)

const Details: React.FC<{ rows: [string, string | undefined][] }> = ({ rows }) => {
  const shown = rows.filter(([, value]) => value && value.trim() && value.trim() !== '-')
  if (shown.length === 0) return null
  return (
    <dl className="chat-rich-card-details">
      {shown.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
    </dl>
  )
}

const CardBody: React.FC<{ card: RichCard }> = ({ card }) => {
  const f = card.fields
  switch (card.kind) {
    case 'Location':
      return (
        <div className="chat-rich-card">
          <CardHead icon={<MapPin size={16} />} title={f.Name || 'Shared location'} subtitle={f.Addr} />
          <a className="chat-rich-card-action" href={mapsUrl(f)} target="_blank" rel="noopener noreferrer">Open in Maps</a>
        </div>
      )
    case 'ContactCard':
      return (
        <div className="chat-rich-card">
          <CardHead icon={<UserRound size={16} />} title={f.Name || 'Shared contact'} subtitle={f.Org && f.Org.trim() !== '-' ? f.Org : undefined} divider />
          <Details rows={[['Phone', f.Phone], ['Email', f.Email]]} />
        </div>
      )
    case 'FlowSent':
    case 'FlowReply':
      return (
        <div className="chat-rich-card">
          <CardHead icon={<Workflow size={16} />} title={f.Flow || 'WhatsApp form'} subtitle={card.kind === 'FlowSent' ? 'Form sent' : 'Form submitted'} divider />
          {f.Text && <p className="chat-rich-card-text">{f.Text}</p>}
          {card.kind === 'FlowSent' && f.CTA && <span className="chat-rich-card-action is-static" aria-hidden="true">{f.CTA}</span>}
        </div>
      )
    case 'Transfer': {
      const link = f.Link && /^https?:\/\//i.test(f.Link) ? f.Link : null
      return (
        <div className="chat-rich-card">
          <CardHead icon={<ArrowLeftRight size={16} />} title={f.Status || 'Transfer'} subtitle={f.Ref} divider />
          <Details rows={[['Total', f.Amount], ['Recipient gets', f.Receive], ['Detail', f.Detail]]} />
          {link && <a className="chat-rich-card-action" href={link} target="_blank" rel="noopener noreferrer">Payment page</a>}
        </div>
      )
    }
    default:
      return null
  }
}

/**
 * A message's content: a structured card, or the text (with the search term marked) and any
 * reply buttons that were sent with it.
 */
export const RichMessage: React.FC<{ text: string; search: string }> = ({ text, search }) => {
  if (!text) return null

  const card = parseRichCard(text)
  if (card) return <CardBody card={card} />

  const { body, buttons } = splitReplyButtons(visibleText(text))
  return (
    <>
      {body.trim() && (
        <p className="chat-bubble-text">
          <HighlightedText text={body} search={search} />
        </p>
      )}
      {buttons.length > 0 && (
        <div className="chat-bubble-buttons" aria-label="Reply buttons sent">
          {buttons.map(b => <span key={b} className="chat-bubble-button">{b}</span>)}
        </div>
      )}
    </>
  )
}
