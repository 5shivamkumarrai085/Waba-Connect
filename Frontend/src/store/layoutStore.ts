import { create } from 'zustand'

/**
 * Page-level layout switches for the signed-in shell.
 *
 * `fullBleed`: the page wants the whole area beside the sidebar — no top header, no page padding,
 * exactly the viewport's height. Set by a page through `useFullBleed()` while it is mounted; the
 * sidebar itself never changes.
 */
interface LayoutState {
  fullBleed: boolean
  setFullBleed: (on: boolean) => void
}

export const useLayoutStore = create<LayoutState>((set) => ({
  fullBleed: false,
  setFullBleed: (fullBleed) => set({ fullBleed })
}))
