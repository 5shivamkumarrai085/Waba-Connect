import React, { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Camera, Check, ChevronDown, Mail, MessageCircle, MessageSquare, ThumbsUp } from 'lucide-react'
import { useAnchoredPosition } from '../../../hooks/useAnchoredPosition'
import useReference from '../../../hooks/useReference'
import { referenceService } from '../../../services/referenceService'
import { ALL_CHANNELS } from '../../../store/chatStore'
import { CHANNELS } from '../../../types/channel'

/** WhatsApp has no lucide icon; its mark is drawn in the channel's token colour. */
const WhatsAppMark: React.FC<{ size?: number }> = ({ size = 16 }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
    <path
      fill="var(--channel-whatsapp)"
      d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347z"
    />
    <path
      fill="var(--channel-whatsapp)"
      d="M12 0C5.373 0 0 5.373 0 12c0 2.128.557 4.122 1.528 5.855L0 24l6.345-1.498A11.956 11.956 0 0012 24c6.627 0 12-5.373 12-12S18.627 0 12 0zm0 22c-1.92 0-3.722-.497-5.28-1.37l-.379-.215-3.766.888.934-3.65-.248-.396A9.935 9.935 0 012 12c0-5.514 4.486-10 10-10s10 4.486 10 10-4.486 10-10 10z"
    />
  </svg>
)

/** Presentation per channel key. Which channels exist, and whether they are available, comes from the server. */
export const channelIcon = (key: string, size = 15): React.ReactNode => {
  switch (key) {
    case 'whatsapp': return <WhatsAppMark size={size + 1} />
    case 'email': return <Mail size={size} color="var(--channel-email)" aria-hidden="true" />
    case 'sms': return <MessageSquare size={size} color="var(--channel-sms)" aria-hidden="true" />
    case 'instagram': return <Camera size={size} color="var(--channel-instagram)" aria-hidden="true" />
    case 'facebook': return <ThumbsUp size={size} color="var(--channel-facebook)" aria-hidden="true" />
    default: return <MessageCircle size={size} aria-hidden="true" />
  }
}

const ALL_LABEL = 'All channels'

/**
 * The inbox's channel filter. The list of channels and whether each is available comes from the
 * server's chat options (with the local registry as a fallback while they load). The menu renders
 * in a portal so no container clips it. Arrow keys move, Enter picks, Escape closes and returns
 * focus to the button.
 */
export const ChannelPicker: React.FC<{
  value: string
  onChange: (value: string) => void
}> = ({ value, onChange }) => {
  const [open, setOpen] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const position = useAnchoredPosition(triggerRef, menuRef, open, { side: 'bottom', align: 'start', offset: 6 })
  const chatOptions = useReference(referenceService.getChatOptions, 'chat-options')

  const channels = chatOptions.data?.channels?.length
    ? chatOptions.data.channels
    : CHANNELS.map(c => ({ key: c.key, label: c.label, available: c.available }))

  const activeLabel = value === ALL_CHANNELS ? ALL_LABEL : channels.find(c => c.key === value)?.label ?? value

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      const target = e.target as Node
      if (!triggerRef.current?.contains(target) && !menuRef.current?.contains(target)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  // Focus the selected option when the menu opens, so the keyboard starts where the value is.
  useEffect(() => {
    if (!open) return
    const frame = requestAnimationFrame(() => {
      const options = menuRef.current?.querySelectorAll<HTMLButtonElement>('[role="option"]:not(:disabled)')
      const selected = menuRef.current?.querySelector<HTMLButtonElement>('[aria-selected="true"]')
      ;(selected ?? options?.[0])?.focus()
    })
    return () => cancelAnimationFrame(frame)
  }, [open])

  const available = channels.filter(c => c.available)
  const planned = channels.filter(c => !c.available)

  const close = (restoreFocus = true) => {
    setOpen(false)
    if (restoreFocus) triggerRef.current?.focus()
  }

  const select = (v: string) => {
    onChange(v)
    close()
  }

  const onMenuKeyDown = (e: React.KeyboardEvent) => {
    const options = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>('[role="option"]:not(:disabled)') ?? [])
    const index = options.indexOf(document.activeElement as HTMLButtonElement)
    if (e.key === 'Escape') { e.preventDefault(); close() }
    else if (e.key === 'ArrowDown') { e.preventDefault(); options[(index + 1) % options.length]?.focus() }
    else if (e.key === 'ArrowUp') { e.preventDefault(); options[(index - 1 + options.length) % options.length]?.focus() }
    else if (e.key === 'Home') { e.preventDefault(); options[0]?.focus() }
    else if (e.key === 'End') { e.preventDefault(); options[options.length - 1]?.focus() }
    else if (e.key === 'Tab') close(false)
  }

  return (
    <div className="channel-picker">
      <button
        ref={triggerRef}
        type="button"
        className={`channel-picker-trigger${open ? ' open' : ''}`}
        onClick={() => setOpen(o => !o)}
        onKeyDown={e => { if (e.key === 'ArrowDown' && !open) { e.preventDefault(); setOpen(true) } }}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={`Channel: ${activeLabel}`}
      >
        <span className="channel-picker-icon-wrap" aria-hidden="true">
          {value === ALL_CHANNELS ? <MessageCircle size={15} /> : channelIcon(value)}
        </span>
        <span className="channel-picker-label">{activeLabel}</span>
        <ChevronDown size={14} className="channel-picker-caret" aria-hidden="true" />
      </button>

      {open && createPortal(
        <div
          ref={menuRef}
          className="channel-picker-menu is-floating"
          role="listbox"
          aria-label="Channel"
          onKeyDown={onMenuKeyDown}
          style={position ? { top: position.top, left: position.left, maxHeight: position.maxHeight, transformOrigin: position.transformOrigin } : { visibility: 'hidden' }}
        >
          <button
            type="button"
            role="option"
            aria-selected={value === ALL_CHANNELS}
            className={`channel-picker-item${value === ALL_CHANNELS ? ' selected' : ''}`}
            onClick={() => select(ALL_CHANNELS)}
          >
            <span className="channel-picker-item-icon" aria-hidden="true"><MessageCircle size={15} /></span>
            <span className="channel-picker-item-label">{ALL_LABEL}</span>
            {value === ALL_CHANNELS && <Check size={14} className="channel-picker-check" aria-hidden="true" />}
          </button>

          {available.map(ch => (
            <button
              key={ch.key}
              type="button"
              role="option"
              aria-selected={value === ch.key}
              className={`channel-picker-item${value === ch.key ? ' selected' : ''}`}
              onClick={() => select(ch.key)}
            >
              <span className="channel-picker-item-icon" aria-hidden="true">{channelIcon(ch.key)}</span>
              <span className="channel-picker-item-label">{ch.label}</span>
              {value === ch.key && <Check size={14} className="channel-picker-check" aria-hidden="true" />}
            </button>
          ))}

          {planned.length > 0 && (
            <>
              <div className="channel-picker-divider" role="presentation">
                <span>Coming soon</span>
              </div>
              {planned.map(ch => (
                <button
                  key={ch.key}
                  type="button"
                  role="option"
                  aria-selected={false}
                  aria-disabled="true"
                  className="channel-picker-item disabled"
                  disabled
                >
                  <span className="channel-picker-item-icon" aria-hidden="true">{channelIcon(ch.key)}</span>
                  <span className="channel-picker-item-label">{ch.label}</span>
                </button>
              ))}
            </>
          )}
        </div>,
        document.body
      )}
    </div>
  )
}
