import { useCallback, useEffect, useLayoutEffect, useState } from 'react'

/**
 * Positions a floating surface against a trigger element, in viewport
 * coordinates, for use with `position: fixed`.
 *
 * Why fixed + portal rather than absolute: the contacts row menus live inside
 * `.data-table-wrapper`, which is `overflow-x: auto`. An absolutely positioned
 * child of a scroll container is clipped by it, and no z-index can escape that.
 * Reporting coordinates in viewport space also removes scrollX/scrollY
 * arithmetic entirely, which is where stale-offset bugs usually come from.
 */

export type Side = 'bottom' | 'top'
export type Align = 'start' | 'end' | 'center'

export interface AnchoredPositionOptions {
  /** Preferred side. Flips to the opposite only when it genuinely won't fit. */
  side?: Side
  align?: Align
  /** Gap between trigger and surface. */
  offset?: number
  /** Minimum breathing room against the viewport edge. */
  padding?: number
  /** Force the surface to the trigger's width (for select-like menus). */
  matchTriggerWidth?: boolean
}

export interface AnchoredPosition {
  top: number
  left: number
  width?: number
  maxHeight: number
  side: Side
  transformOrigin: string
}

const clamp = (value: number, min: number, max: number) =>
  Math.min(Math.max(value, min), max)

export function useAnchoredPosition(
  triggerRef: React.RefObject<HTMLElement | null>,
  floatingRef: React.RefObject<HTMLElement | null>,
  open: boolean,
  options: AnchoredPositionOptions = {}
): AnchoredPosition | null {
  const {
    side: preferredSide = 'bottom',
    align = 'start',
    offset = 6,
    padding = 8,
    matchTriggerWidth = false,
  } = options

  const [position, setPosition] = useState<AnchoredPosition | null>(null)

  const compute = useCallback(() => {
    const trigger = triggerRef.current
    const floating = floatingRef.current
    if (!trigger || !floating) return

    const t = trigger.getBoundingClientRect()
    const f = floating.getBoundingClientRect()
    const viewportWidth = document.documentElement.clientWidth
    const viewportHeight = document.documentElement.clientHeight

    const spaceBelow = viewportHeight - t.bottom - padding
    const spaceAbove = t.top - padding

    // Flip only when the preferred side genuinely cannot fit AND the opposite
    // side has more room. Testing "fits" alone makes the menu flip-flop while
    // the page scrolls, which is far more distracting than a scrollable menu.
    const needed = f.height + offset
    let side: Side = preferredSide
    if (preferredSide === 'bottom' && needed > spaceBelow && spaceAbove > spaceBelow) {
      side = 'top'
    } else if (preferredSide === 'top' && needed > spaceAbove && spaceBelow > spaceAbove) {
      side = 'bottom'
    }

    const available = (side === 'bottom' ? spaceBelow : spaceAbove) - offset
    // Never below 96px: a surface squeezed smaller than that is unusable, and
    // it is better to overflow slightly than to render a 12px scroll sliver.
    const maxHeight = Math.max(available, 96)
    const height = Math.min(f.height, maxHeight)

    const top = side === 'bottom' ? t.bottom + offset : t.top - offset - height

    const width = matchTriggerWidth ? t.width : f.width
    let left: number
    if (align === 'end') left = t.right - width
    else if (align === 'center') left = t.left + t.width / 2 - width / 2
    else left = t.left

    const maxLeft = viewportWidth - width - padding
    left = maxLeft < padding ? padding : clamp(left, padding, maxLeft)

    // Origin-aware: scale out of the trigger, not out of the surface's own
    // corner. Computed AFTER clamping so a menu pushed off the viewport edge
    // still grows from the button the user clicked.
    const originX = clamp(t.left + t.width / 2 - left, 0, width)
    const originY = side === 'bottom' ? '0' : '100%'

    setPosition((prev) => {
      const next: AnchoredPosition = {
        top,
        left,
        width: matchTriggerWidth ? t.width : undefined,
        maxHeight,
        side,
        transformOrigin: `${originX}px ${originY}`,
      }
      if (
        prev &&
        prev.top === next.top &&
        prev.left === next.left &&
        prev.width === next.width &&
        prev.maxHeight === next.maxHeight &&
        prev.side === next.side &&
        prev.transformOrigin === next.transformOrigin
      ) {
        return prev
      }
      return next
    })
  }, [triggerRef, floatingRef, preferredSide, align, offset, padding, matchTriggerWidth])

  // Measure and commit in the same frame so there is no flash at the origin.
  useLayoutEffect(() => {
    if (!open) {
      setPosition(null)
      return
    }
    compute()
  }, [open, compute])

  useEffect(() => {
    if (!open) return

    // Deliberately synchronous rather than rAF-coalesced. Scroll events and
    // ResizeObserver callbacks are already delivered at most once per frame, so
    // deferring another frame only adds visible lag between the trigger and the
    // surface. It also removes a failure mode: a pending frame that never fires
    // (background tab, non-compositing embed) would otherwise wedge every
    // subsequent update behind a stale guard.
    const onReposition = () => compute()

    // capture: true is mandatory. The real scroller for the row menus is
    // `.data-table-wrapper`; scroll events do not bubble, so a non-capturing
    // window listener silently never fires.
    window.addEventListener('scroll', onReposition, { capture: true, passive: true })
    window.addEventListener('resize', onReposition)

    // Safe against feedback loops: compute() bails out of setState when the
    // resulting position is unchanged, so a size change settles in one pass.
    const observer = new ResizeObserver(onReposition)
    if (triggerRef.current) observer.observe(triggerRef.current)
    if (floatingRef.current) observer.observe(floatingRef.current)

    return () => {
      window.removeEventListener('scroll', onReposition, { capture: true })
      window.removeEventListener('resize', onReposition)
      observer.disconnect()
    }
  }, [open, compute, triggerRef, floatingRef])

  return position
}
