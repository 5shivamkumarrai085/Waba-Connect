import { useLayoutEffect } from 'react'
import { useLayoutStore } from '../store/layoutStore'

/**
 * Lets the calling page fill everything beside the sidebar while it is mounted: the shell drops
 * its top header and page padding, and the page gets exactly the viewport's height. Restored on
 * unmount, so every other page keeps the normal layout.
 *
 * A layout effect, so the switch happens before paint and the header never flashes.
 */
export function useFullBleed(): void {
  const setFullBleed = useLayoutStore((s) => s.setFullBleed)

  useLayoutEffect(() => {
    setFullBleed(true)
    return () => setFullBleed(false)
  }, [setFullBleed])
}

export default useFullBleed
