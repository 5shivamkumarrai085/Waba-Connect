import { useEffect, useRef } from 'react'

/**
 * Fires `handler` when a pointer press lands outside every supplied ref.
 *
 * Listens on `pointerdown` rather than `click`: dismissal should feel
 * immediate, and it avoids the classic race where the same click that opens a
 * trigger also closes the surface it just opened.
 */
export function useOnClickOutside(
  refs: Array<React.RefObject<HTMLElement | null>>,
  handler: (event: PointerEvent) => void,
  enabled = true
): void {
  const handlerRef = useRef(handler)
  handlerRef.current = handler

  const refsRef = useRef(refs)
  refsRef.current = refs

  useEffect(() => {
    if (!enabled) return

    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node | null
      if (!target || !target.isConnected) return

      const isInside = refsRef.current.some(
        (ref) => ref.current && ref.current.contains(target)
      )
      if (!isInside) handlerRef.current(event)
    }

    document.addEventListener('pointerdown', onPointerDown, true)
    return () => document.removeEventListener('pointerdown', onPointerDown, true)
  }, [enabled])
}
