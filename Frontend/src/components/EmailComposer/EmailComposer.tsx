import React, { useEffect, useMemo, useRef, useState } from 'react'
import { Send, X, Loader2, Reply, ReplyAll, Forward, Plus, Paperclip, File, Image, Trash2 } from 'lucide-react'
import toast from 'react-hot-toast'
import { RichTextEditor } from '../RichTextEditor/RichTextEditor'
import { ConfirmationModal } from '../Modal/ConfirmationModal'
import { chatService } from '../../services/chat/chatService'
import type { Message } from '../../types/chat'
import './EmailComposer.css'

/** Which of the four the composer is currently set up for. */
export type EmailComposeMode = 'reply' | 'replyAll' | 'forward' | 'newEmail'

interface AttachedFile {
  fileName: string
  contentType: string
  base64Data: string
  sizeKb: number
}

interface EmailComposerProps {
  conversationId: number
  /** The message being acted on — supplies the recipients, subject and threading header. */
  source: Message
  /** The address this connection sends as, shown so the operator knows who it comes from. */
  fromAddress?: string | null
  mode: EmailComposeMode
  onSent: () => void
  /** Closes the composer. Called once any draft has been confirmed as discarded. */
  onCancel: () => void
}

/** Splits a stored "a@x.com, b@y.com" field back into addresses. */
const splitAddresses = (value?: string | null): string[] =>
  (value ?? '')
    .split(',')
    .map(a => a.trim())
    .filter(a => a.length > 0)

/** Simple email chip input that renders pills and lets the user type to add more. */
const ChipInput: React.FC<{
  label: string
  chips: string[]
  onChange: (chips: string[]) => void
  placeholder?: string
  inputRef?: React.MutableRefObject<HTMLInputElement | null>
}> = ({ label, chips, onChange, placeholder, inputRef }) => {
  const [draft, setDraft] = useState('')
  const defaultRef = useRef<HTMLInputElement>(null)
  const ref = inputRef ?? defaultRef

  const commit = () => {
    const trimmed = draft.trim()
    if (trimmed && !chips.includes(trimmed)) {
      onChange([...chips, trimmed])
    }
    setDraft('')
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' || e.key === ',' || e.key === 'Tab') {
      e.preventDefault()
      commit()
    } else if (e.key === 'Backspace' && draft === '' && chips.length > 0) {
      onChange(chips.slice(0, -1))
    }
  }

  const removeChip = (index: number) => {
    onChange(chips.filter((_, i) => i !== index))
  }

  return (
    <div className="ec-chip-field">
      <span className="ec-field-label">{label}</span>
      <div className="ec-chip-wrap" onClick={() => ref.current?.focus()}>
        {chips.map((chip, i) => (
          <span key={i} className="ec-chip">
            {chip}
            <button type="button" className="ec-chip-remove" onClick={() => removeChip(i)} aria-label={`Remove ${chip}`}>
              <X size={11} />
            </button>
          </span>
        ))}
        <input
          ref={ref}
          type="text"
          className="ec-chip-input"
          value={draft}
          placeholder={chips.length === 0 ? placeholder : ''}
          onChange={e => setDraft(e.target.value)}
          onKeyDown={handleKeyDown}
          onBlur={commit}
        />
      </div>
    </div>
  )
}

/** How the header names each mode. */
const MODE_HEADINGS: Record<EmailComposeMode, { label: string; icon: React.ReactNode }> = {
  reply: { label: 'Reply', icon: <Reply size={14} aria-hidden="true" /> },
  replyAll: { label: 'Reply all', icon: <ReplyAll size={14} aria-hidden="true" /> },
  forward: { label: 'Forward', icon: <Forward size={14} aria-hidden="true" /> },
  newEmail: { label: 'New email', icon: <Plus size={14} aria-hidden="true" /> },
}

/**
 * Reply / Reply All / Forward / New Email, for an email thread. Opened from a message's own
 * buttons or the page's New Email, closed with ✕ or Escape (asking first if there is a draft).
 * Supports file attachments via base64 encoding sent to the backend.
 */
export const EmailComposer: React.FC<EmailComposerProps> = ({
  conversationId,
  source,
  fromAddress,
  mode,
  onSent,
  onCancel
}) => {
  const [toChips, setToChips] = useState<string[]>([])
  const [ccChips, setCcChips] = useState<string[]>([])
  const [bccChips, setBccChips] = useState<string[]>([])
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [isSending, setIsSending] = useState(false)
  const [showBcc, setShowBcc] = useState(false)
  // Replies address the thread, so their recipients and subject fold into one summary line;
  // forwarding and new mail start empty, so their fields are open.
  const [showFields, setShowFields] = useState(mode === 'forward' || mode === 'newEmail')
  const [attachments, setAttachments] = useState<AttachedFile[]>([])
  const [isAttaching, setIsAttaching] = useState(false)
  const [confirmDiscard, setConfirmDiscard] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const toInputRef = useRef<HTMLInputElement>(null) as React.MutableRefObject<HTMLInputElement | null>

  /**
   * Who each mode addresses.
   */
  const derived = useMemo(() => {
    const isOutgoing = source.type === 'outgoing'
    const counterparties = isOutgoing
      ? splitAddresses(source.toAddresses)
      : [source.fromAddress].filter((a): a is string => Boolean(a))

    const others = [...splitAddresses(source.toAddresses), ...splitAddresses(source.ccAddresses)]
      .filter(a => a.toLowerCase() !== (fromAddress ?? '').toLowerCase())
      .filter(a => !counterparties.some(c => c.toLowerCase() === a.toLowerCase()))

    const baseSubject = (source.subject ?? '').trim()

    const prefixed = (prefix: string) =>
      baseSubject.toLowerCase().startsWith(prefix.toLowerCase())
        ? baseSubject
        : `${prefix} ${baseSubject}`.trim()

    switch (mode) {
      case 'replyAll':
        return { to: counterparties, cc: others, subject: prefixed('Re:') }
      case 'forward':
        return { to: [], cc: [], subject: prefixed('Fwd:') }
      case 'newEmail':
        return { to: [], cc: [], subject: '' }
      default:
        return { to: counterparties, cc: [], subject: prefixed('Re:') }
    }
  }, [mode, source, fromAddress])

  // Addresses and subject follow the mode; body and attachments are left alone
  useEffect(() => {
    setToChips(derived.to)
    setCcChips(derived.cc)
    setSubject(derived.subject)
    setShowBcc(false)
    const needsAddress = mode === 'forward' || mode === 'newEmail'
    setShowFields(needsAddress)
    // Ready to type where the operator starts: the address for new mail, the body for a reply.
    const timer = window.setTimeout(() => {
      if (needsAddress) toInputRef.current?.focus()
      else rootRef.current?.querySelector<HTMLElement>('.rich-editor-surface')?.focus()
    }, 50)
    return () => window.clearTimeout(timer)
  }, [derived, mode])

  /** Read file as base64, add to attachment list. */
  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? [])
    if (files.length === 0) return

    setIsAttaching(true)
    try {
      const newAttachments = await Promise.all(
        files.map(file => new Promise<AttachedFile>((resolve, reject) => {
          const reader = new FileReader()
          reader.onload = () => {
            const dataUrl = reader.result as string
            // dataUrl is "data:<mime>;base64,<data>" — strip the prefix
            const base64Data = dataUrl.split(',')[1] ?? ''
            resolve({
              fileName: file.name,
              contentType: file.type || 'application/octet-stream',
              base64Data,
              sizeKb: Math.round(file.size / 1024)
            })
          }
          reader.onerror = reject
          reader.readAsDataURL(file)
        }))
      )
      setAttachments(prev => [...prev, ...newAttachments])
    } catch {
      toast.error('Could not read one or more files.')
    } finally {
      setIsAttaching(false)
      // Reset so the same file can be picked again
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  const removeAttachment = (index: number) => {
    setAttachments(prev => prev.filter((_, i) => i !== index))
  }

  const handleSend = async () => {
    if (toChips.length === 0) {
      toast.error('Add at least one recipient.')
      return
    }

    if (body.replace(/<[^>]*>/g, '').trim().length === 0 && attachments.length === 0) {
      toast.error('Write something or attach a file before sending.')
      return
    }

    setIsSending(true)
    try {
      const result = await chatService.sendEmailReply(conversationId, {
        subject,
        bodyHtml: body,
        to: toChips,
        cc: ccChips,
        bcc: bccChips,
        inReplyToMessageId: mode === 'newEmail' ? null : source.id,
        attachments: attachments.map(a => ({
          fileName: a.fileName,
          contentType: a.contentType,
          base64Data: a.base64Data
        }))
      })

      if (!result.success) {
        toast.error(result.message)
        return
      }

      toast.success(result.message)
      setBody('')
      setAttachments([])
      onSent()
    } finally {
      setIsSending(false)
    }
  }

  const isDirty = body.replace(/<[^>]*>/g, '').trim().length > 0 || attachments.length > 0
  const heading = MODE_HEADINGS[mode]

  /** ✕ and Escape: an empty composer just closes; a draft is only thrown away when confirmed. */
  const requestClose = () => {
    if (isDirty) setConfirmDiscard(true)
    else onCancel()
  }

  const discard = () => {
    setConfirmDiscard(false)
    setBody('')
    setAttachments([])
    onCancel()
  }

  return (
    <div
      ref={rootRef}
      className="email-composer"
      role="region"
      aria-label={`${heading.label} composer`}
      onKeyDown={e => {
        if (e.key === 'Escape' && !confirmDiscard) {
          e.stopPropagation()
          requestClose()
        }
      }}
    >
      <div className="email-composer-head">
        <span className="email-composer-heading">
          {heading.icon}
          <strong>{heading.label}</strong>
        </span>
        <button
          type="button"
          className="email-composer-close"
          onClick={requestClose}
          title="Close"
          aria-label="Close composer"
        >
          <X size={16} aria-hidden="true" />
        </button>
      </div>

      {/* Fields, editor and attachments scroll; the header above and the Send bar below never do. */}
      <div className="email-composer-scroll">
      {!showFields ? (
        <div className="ec-summary">
          <span className="ec-summary-text" title={[...toChips, ...ccChips].join(', ')}>
            <span className="ec-field-label">To</span>
            {toChips.length > 0 ? toChips.join(', ') : 'No recipient'}
            {ccChips.length > 0 && <span className="ec-summary-cc"> · Cc {ccChips.join(', ')}</span>}
          </span>
          <button type="button" className="ec-summary-edit" onClick={() => setShowFields(true)}>
            Edit recipients
          </button>
        </div>
      ) : (
      <div className="email-composer-fields">
        <ChipInput
          label="To"
          chips={toChips}
          onChange={setToChips}
          placeholder="name@example.com"
          inputRef={toInputRef}
        />

        <div className="ec-field-row">
          <ChipInput
            label="Cc"
            chips={ccChips}
            onChange={setCcChips}
            placeholder="Optional"
          />
          {!showBcc && (
            <button
              type="button"
              className="email-composer-bcc-toggle"
              onClick={() => setShowBcc(true)}
            >
              Bcc
            </button>
          )}
        </div>

        {showBcc && (
          <ChipInput
            label="Bcc"
            chips={bccChips}
            onChange={setBccChips}
            placeholder="Not shown to other recipients"
          />
        )}

        <div className="ec-subject-row">
          <span className="ec-field-label">Subject</span>
          <input
            type="text"
            className="ec-subject-input"
            value={subject}
            onChange={e => setSubject(e.target.value)}
            placeholder="Subject"
          />
        </div>
      </div>
      )}

      <RichTextEditor
        value={body}
        onChange={setBody}
        placeholder={mode === 'forward' ? 'Add a note to the forwarded message…' : mode === 'newEmail' ? 'Write your message…' : 'Write your reply…'}
      />

      {/* Attachment list */}
      {attachments.length > 0 && (
        <div className="ec-attachments-list">
          {attachments.map((att, i) => {
            const isImage = att.contentType.startsWith('image/')
            return (
              <div key={i} className="ec-attachment-chip">
                {isImage ? <Image size={13} /> : <File size={13} />}
                <span className="ec-attachment-name" title={att.fileName}>{att.fileName}</span>
                <span className="ec-attachment-size">{att.sizeKb}KB</span>
                <button
                  type="button"
                  className="ec-attachment-remove"
                  onClick={() => removeAttachment(i)}
                  aria-label={`Remove ${att.fileName}`}
                >
                  <Trash2 size={11} />
                </button>
              </div>
            )
          })}
        </div>
      )}

      </div>

      <div className="email-composer-actions">
        {fromAddress && <span className="email-composer-from">Sending as {fromAddress}</span>}

        <div className="email-composer-action-btns">
          {/* Hidden file input */}
          <input
            ref={fileInputRef}
            type="file"
            multiple
            accept="image/*,.pdf,.doc,.docx,.xls,.xlsx,.txt,.csv,.zip"
            hidden
            onChange={handleFileChange}
          />

          <button
            type="button"
            className="btn btn-secondary email-composer-attach-btn"
            onClick={() => fileInputRef.current?.click()}
            disabled={isAttaching}
            title="Attach file"
            aria-label="Attach file"
          >
            {isAttaching ? <Loader2 size={15} className="animate-spin" /> : <Paperclip size={15} />}
          </button>

          <button
            type="button"
            className="btn btn-primary email-composer-send"
            onClick={handleSend}
            disabled={isSending}
          >
            {isSending ? <Loader2 size={15} className="animate-spin" /> : <Send size={15} />}
            {isSending ? 'Sending…' : 'Send'}
          </button>
        </div>
      </div>

      <ConfirmationModal
        isOpen={confirmDiscard}
        title="Discard this draft?"
        message="What you have written and any attachments will be lost."
        confirmText="Discard"
        cancelText="Keep writing"
        onConfirm={discard}
        onCancel={() => setConfirmDiscard(false)}
        isDestructive
      />
    </div>
  )
}

export default EmailComposer
