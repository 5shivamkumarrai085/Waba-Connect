import React, {
  createContext,
  useCallback,
  useContext,
  useId,
  useRef,
  useState,
} from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'framer-motion'
import { useAnchoredPosition, type Align, type Side } from '../../hooks/useAnchoredPosition'
import { useEscapeKey } from '../../hooks/useEscapeKey'
import { useOnClickOutside } from '../../hooks/useOnClickOutside'
import { menuTransitions } from '../../utils/motion'
import './Menu.css'

/**
 * Portal-rendered, collision-aware dropdown.
 *
 * Exists because `position: absolute` menus inside `.data-table-wrapper`
 * (overflow-x: auto) are clipped by their scroll container, and because
 * `tr:hover { transform }` creates a stacking context that lets a later row
 * paint over an earlier row's open menu. Portaling to <body> at --z-menu
 * removes both failure modes categorically rather than patching z-indexes.
 *
 * Controlled only: every call site in this app already owns its open state.
 */

export interface MenuTriggerProps {
  ref: React.Ref<HTMLButtonElement>
  onClick: (event: React.MouseEvent) => void
  onKeyDown: (event: React.KeyboardEvent) => void
  'aria-haspopup': 'menu'
  'aria-expanded': boolean
  'aria-controls': string | undefined
  id: string
}

interface MenuContextValue {
  close: () => void
  closeOnSelect: boolean
}

const MenuContext = createContext<MenuContextValue | null>(null)

export interface MenuProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  trigger: (props: MenuTriggerProps) => React.ReactNode
  side?: Side
  align?: Align
  offset?: number
  matchTriggerWidth?: boolean
  /** Whether picking an item dismisses the menu. Default true. */
  closeOnSelect?: boolean
  /** Extra class on the surface — call sites keep their own visual styling. */
  className?: string
  ariaLabel?: string
  children: React.ReactNode
}

export const Menu: React.FC<MenuProps> = ({
  open,
  onOpenChange,
  trigger,
  side = 'bottom',
  align = 'start',
  offset = 6,
  matchTriggerWidth = false,
  closeOnSelect = true,
  className,
  ariaLabel,
  children,
}) => {
  const triggerRef = useRef<HTMLButtonElement>(null)
  const surfaceRef = useRef<HTMLDivElement>(null)
  const openedViaKeyboard = useRef(false)
  const [mounted, setMounted] = useState(false)

  const reduceMotion = useReducedMotion()
  const baseId = useId()
  const surfaceId = `${baseId}-menu`
  const triggerId = `${baseId}-trigger`

  const position = useAnchoredPosition(triggerRef, surfaceRef, open, {
    side,
    align,
    offset,
    matchTriggerWidth,
  })

  const close = useCallback(() => {
    // Only reclaim focus if it currently lives inside the menu. Otherwise a
    // click on some unrelated button would yank the caret back to the trigger.
    const surface = surfaceRef.current
    if (surface && document.activeElement && surface.contains(document.activeElement)) {
      triggerRef.current?.focus({ preventScroll: true })
    }
    onOpenChange(false)
  }, [onOpenChange])

  useEscapeKey(open, (event) => {
    // Stop here so a Menu inside a Dialog dismisses only itself.
    event.stopPropagation()
    event.preventDefault()
    close()
  })

  useOnClickOutside([triggerRef, surfaceRef], () => onOpenChange(false), open)

  const getItems = useCallback((): HTMLElement[] => {
    const surface = surfaceRef.current
    if (!surface) return []
    return Array.from(
      surface.querySelectorAll<HTMLElement>('[data-menu-item]:not([disabled])')
    )
  }, [])

  const handleTriggerClick = useCallback(() => {
    openedViaKeyboard.current = false
    onOpenChange(!open)
  }, [open, onOpenChange])

  const handleTriggerKeyDown = useCallback(
    (event: React.KeyboardEvent) => {
      if (event.key === 'ArrowDown' || event.key === 'Enter' || event.key === ' ') {
        event.preventDefault()
        openedViaKeyboard.current = true
        onOpenChange(true)
      }
    },
    [onOpenChange]
  )

  const handleSurfaceKeyDown = useCallback(
    (event: React.KeyboardEvent) => {
      const items = getItems()
      if (items.length === 0) return

      const index = items.indexOf(document.activeElement as HTMLElement)

      switch (event.key) {
        case 'ArrowDown':
          event.preventDefault()
          items[(index + 1) % items.length]?.focus()
          break
        case 'ArrowUp':
          event.preventDefault()
          items[index <= 0 ? items.length - 1 : index - 1]?.focus()
          break
        case 'Home':
          event.preventDefault()
          items[0]?.focus()
          break
        case 'End':
          event.preventDefault()
          items[items.length - 1]?.focus()
          break
        case 'Tab':
          // Let focus continue naturally out of the menu.
          onOpenChange(false)
          break
      }
    },
    [getItems, onOpenChange]
  )

  // Focus once the surface is measured and placed, so nothing scrolls to 0,0.
  const handleAnimationStart = useCallback(() => {
    if (mounted) return
    setMounted(true)
    if (openedViaKeyboard.current) {
      getItems()[0]?.focus({ preventScroll: true })
    } else {
      surfaceRef.current?.focus({ preventScroll: true })
    }
  }, [mounted, getItems])

  const contextValue: MenuContextValue = { close, closeOnSelect }

  const triggerNode = trigger({
    ref: triggerRef,
    onClick: handleTriggerClick,
    onKeyDown: handleTriggerKeyDown,
    'aria-haspopup': 'menu',
    'aria-expanded': open,
    'aria-controls': open ? surfaceId : undefined,
    id: triggerId,
  })

  const surface = (
    <AnimatePresence onExitComplete={() => setMounted(false)}>
      {open && (
        <motion.div
          ref={surfaceRef}
          id={surfaceId}
          role="menu"
          aria-labelledby={triggerId}
          aria-label={ariaLabel}
          tabIndex={-1}
          className={`oc-menu-surface${className ? ` ${className}` : ''}`}
          onKeyDown={handleSurfaceKeyDown}
          onAnimationStart={handleAnimationStart}
          initial={reduceMotion ? { opacity: 0 } : { opacity: 0, scale: 0.96 }}
          animate={
            reduceMotion
              ? { opacity: 1, transition: { duration: 0.1 } }
              : { opacity: 1, scale: 1, transition: menuTransitions.in }
          }
          exit={
            reduceMotion
              ? { opacity: 0, transition: { duration: 0.1 } }
              : { opacity: 0, scale: 0.96, transition: menuTransitions.out }
          }
          style={{
            // Hidden until measured — one frame at 0,0 would otherwise flash
            // in the top-left corner of the viewport.
            top: position?.top ?? 0,
            left: position?.left ?? 0,
            width: position?.width,
            maxHeight: position?.maxHeight,
            transformOrigin: position?.transformOrigin ?? 'center',
            visibility: position ? 'visible' : 'hidden',
          }}
        >
          {children}
        </motion.div>
      )}
    </AnimatePresence>
  )

  return (
    <MenuContext.Provider value={contextValue}>
      {triggerNode}
      {createPortal(surface, document.body)}
    </MenuContext.Provider>
  )
}

// ─── Items ──────────────────────────────────────────────────────────

export interface MenuItemProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'onSelect'> {
  onSelect?: () => void
  destructive?: boolean
  /** Overrides the parent Menu's closeOnSelect for this item only. */
  closeOnSelect?: boolean
}

export const MenuItem: React.FC<MenuItemProps> = ({
  onSelect,
  destructive = false,
  closeOnSelect,
  className,
  children,
  onClick,
  ...rest
}) => {
  const context = useContext(MenuContext)

  const handleClick = (event: React.MouseEvent<HTMLButtonElement>) => {
    onClick?.(event)
    onSelect?.()
    const shouldClose = closeOnSelect ?? context?.closeOnSelect ?? true
    if (shouldClose) context?.close()
  }

  return (
    <button
      {...rest}
      type="button"
      role="menuitem"
      tabIndex={-1}
      data-menu-item=""
      className={[
        'oc-menu-item',
        destructive ? 'oc-menu-item--destructive' : '',
        className ?? '',
      ]
        .filter(Boolean)
        .join(' ')}
      onClick={handleClick}
    >
      {children}
    </button>
  )
}

export const MenuSeparator: React.FC = () => (
  <div className="oc-menu-separator" role="separator" />
)

export const MenuLabel: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div className="oc-menu-label">{children}</div>
)

export default Menu
