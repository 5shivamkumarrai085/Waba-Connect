import { useEffect, useRef } from 'react'

/**
 * Calls `handler` on Escape while `enabled`.
 *
 * Registered in the capture phase by default so a nested surface (a Menu open
 * inside a Dialog) can stopPropagation() and dismiss only itself.
 */
export function useEscapeKey(
  enabled: boolean,
  handler: (event: KeyboardEvent) => void,
  options: { capture?: boolean } = {}
): void {
  const { capture = true } = options

  // Keep the latest handler without re-binding the listener on every render.
  const handlerRef = useRef(handler)
  handlerRef.current = handler

  useEffect(() => {
    if (!enabled) return

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') handlerRef.current(event)
    }

    document.addEventListener('keydown', onKeyDown, capture)
    return () => document.removeEventListener('keydown', onKeyDown, capture)
  }, [enabled, capture])
}
