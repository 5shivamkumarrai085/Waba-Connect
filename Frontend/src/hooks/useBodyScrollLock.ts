import { useEffect } from 'react'
import { lockBodyScroll, unlockBodyScroll } from '../components/Modal/scrollLock'

/** Locks body scroll while `active`. See scrollLock.ts for why it's ref-counted. */
export function useBodyScrollLock(active: boolean): void {
  useEffect(() => {
    if (!active) return
    lockBodyScroll()
    return unlockBodyScroll
  }, [active])
}
