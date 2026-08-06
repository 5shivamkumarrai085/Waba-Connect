import { useEffect, useRef } from 'react'

const FOCUSABLE = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ')

/** react-hot-toast renders its own container; never fight it for focus. */
const isExempt = (node: Element | null): boolean =>
  !!node?.closest('[id^="_rht_"]')

function getFocusable(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
    // offsetParent is null for display:none and for elements inside one.
    (el) => el.offsetParent !== null || el === document.activeElement
  )
}

/**
 * Traps Tab focus inside `containerRef` while `active`, then restores focus to
 * whatever was focused before.
 *
 * Initial focus deliberately lands on the container itself rather than the
 * first focusable element: auto-focusing the "Delete" button of a confirmation
 * dialog is a genuine hazard, and focusing the dialog is also what makes
 * screen readers announce its title.
 */
export function useFocusTrap(
  containerRef: React.RefObject<HTMLElement | null>,
  active: boolean,
  options: { initialFocusRef?: React.RefObject<HTMLElement | null> } = {}
): void {
  const { initialFocusRef } = options
  const previouslyFocused = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (!active) return
    const container = containerRef.current
    if (!container) return

    previouslyFocused.current = document.activeElement as HTMLElement | null

    const target =
      initialFocusRef?.current ??
      container.querySelector<HTMLElement>('[data-autofocus]') ??
      container
    target.focus({ preventScroll: true })

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') return

      const focusable = getFocusable(container)
      if (focusable.length === 0) {
        event.preventDefault()
        container.focus({ preventScroll: true })
        return
      }

      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      const activeEl = document.activeElement

      if (event.shiftKey && (activeEl === first || activeEl === container)) {
        event.preventDefault()
        last.focus({ preventScroll: true })
      } else if (!event.shiftKey && activeEl === last) {
        event.preventDefault()
        first.focus({ preventScroll: true })
      }
    }

    // Pull focus back if it escapes (browser chrome, programmatic focus).
    const onFocusIn = (event: FocusEvent) => {
      const target = event.target as Element | null
      if (!target || container.contains(target) || isExempt(target)) return
      container.focus({ preventScroll: true })
    }

    container.addEventListener('keydown', onKeyDown)
    document.addEventListener('focusin', onFocusIn)

    return () => {
      container.removeEventListener('keydown', onKeyDown)
      document.removeEventListener('focusin', onFocusIn)

      // Restore on active -> false rather than on unmount, so focus never
      // lands on an element that is mid-exit-animation.
      const restore = previouslyFocused.current
      if (restore && restore.isConnected) {
        restore.focus({ preventScroll: true })
      }
    }
  }, [active, containerRef, initialFocusRef])
}
