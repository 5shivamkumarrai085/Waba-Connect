import React, { useEffect, useId, useRef } from 'react'
import { SquareMousePointer, X } from 'lucide-react'
import useReference from '../../hooks/useReference'
import { referenceService } from '../../services/referenceService'

interface ReplyButtonsPopoverProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  value: string[]
  onChange: (value: string[]) => void
}

/**
 * Up to N tappable answers sent under the next WhatsApp message. The count and label length come
 * from the server (GET api/reference/chat-options), which enforces the same limits.
 */
export const ReplyButtonsPopover: React.FC<ReplyButtonsPopoverProps> = ({ open, onOpenChange, value, onChange }) => {
  const options = useReference(referenceService.getChatOptions, 'chat-options')
  const titleId = useId()
  const hintId = useId()
  const firstInput = useRef<HTMLInputElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)

  const max = options.data?.maxReplyButtons ?? 0
  const maxLength = options.data?.maxReplyButtonLength ?? 0
  const filled = value.filter(v => v.trim()).length

  useEffect(() => {
    if (open && options.data) firstInput.current?.focus()
  }, [open, options.data])

  const close = () => {
    onOpenChange(false)
    trigger.current?.focus()
  }

  const setAt = (index: number, text: string) => {
    const next = [...value]
    next[index] = text
    onChange(next)
  }

  return (
    <>
      <button
        ref={trigger}
        type="button"
        className={`chat-icon-btn${filled > 0 ? ' active' : ''}`}
        title={filled > 0 ? `${filled} reply button${filled === 1 ? '' : 's'} added` : 'Add reply buttons'}
        aria-label={filled > 0 ? `Reply buttons (${filled} added)` : 'Add reply buttons'}
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => onOpenChange(!open)}
      >
        <SquareMousePointer size={18} aria-hidden="true" />
      </button>
      {open && (
        <div
          className="canned-replies-popover chat-reply-buttons-popover"
          role="dialog"
          aria-labelledby={titleId}
          aria-describedby={hintId}
          onKeyDown={e => {
            if (e.key === 'Escape') {
              e.stopPropagation()
              close()
            }
          }}
        >
          <div className="canned-replies-head chat-reply-buttons-head">
            <span id={titleId}>Reply Buttons</span>
            <button type="button" className="chat-reply-buttons-close" aria-label="Close reply buttons" onClick={close}>
              <X size={14} aria-hidden="true" />
            </button>
          </div>
          {options.error ? (
            <p className="chat-reply-buttons-hint chat-reply-buttons-error" id={hintId}>
              {options.error} <button type="button" className="chat-reply-buttons-link" onClick={options.retry}>Try again</button>
            </p>
          ) : (
            <>
              <p className="chat-reply-buttons-hint" id={hintId}>
                {options.data
                  ? `Up to ${max} tappable answers under your message, ${maxLength} characters each. Sent only inside WhatsApp's 24-hour window.`
                  : 'Loading…'}
              </p>
              {Array.from({ length: max }, (_, i) => (
                <div key={i} className="chat-reply-buttons-field">
                  <input
                    ref={i === 0 ? firstInput : undefined}
                    className="chat-reply-buttons-input"
                    aria-label={`Reply button ${i + 1}`}
                    placeholder={`Button ${i + 1}…`}
                    maxLength={maxLength}
                    autoComplete="off"
                    value={value[i] ?? ''}
                    onChange={e => setAt(i, e.target.value)}
                  />
                  <span className="chat-reply-buttons-count" aria-hidden="true">{(value[i] ?? '').length}/{maxLength}</span>
                </div>
              ))}
              {filled > 0 && (
                <button type="button" className="chat-reply-buttons-clear" onClick={() => onChange([])}>Clear Buttons</button>
              )}
            </>
          )}
        </div>
      )}
    </>
  )
}

export default ReplyButtonsPopover
