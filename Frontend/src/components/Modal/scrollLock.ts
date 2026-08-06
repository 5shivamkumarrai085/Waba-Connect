/**
 * Reference-counted body scroll lock.
 *
 * Counting is not optional here. ContactsList mounts a ConfirmationModal and an
 * InitiateChatModal simultaneously (only `isOpen` differs), ConnectionsList
 * mounts two ConfirmationModals, and React 19 StrictMode double-invokes effects
 * in development. A boolean lock leaks in all three cases.
 *
 * Uses a class plus a custom property rather than inline styles: responsive.css
 * already sets `body.sidebar-open { overflow: hidden !important }`, and two
 * class-based locks coexist without clobbering each other's saved state.
 */

let count = 0

export function lockBodyScroll(): void {
  if (count++ > 0) return

  // Compensate for the disappearing scrollbar so the page doesn't shift.
  // Safe here: the document itself scrolls, and .sidebar / .header are
  // position: sticky (in-flow), so they reflow with the padding.
  const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth
  document.documentElement.style.setProperty('--scrollbar-width', `${scrollbarWidth}px`)
  document.body.classList.add('is-modal-open')
}

export function unlockBodyScroll(): void {
  if (count === 0) return
  if (--count > 0) return

  document.body.classList.remove('is-modal-open')
  document.documentElement.style.removeProperty('--scrollbar-width')
}
