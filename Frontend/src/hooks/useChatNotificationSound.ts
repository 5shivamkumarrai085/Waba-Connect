import { useEffect, useRef } from 'react'
import { apiClient } from '../services/apiClient'

/**
 * Plays a short tone when a new inbound chat message arrives, if the operator has enabled it.
 *
 * <para>
 * The trigger is the total unread count across the inbox rising between two polls. That is the
 * one signal that means "something arrived that you have not seen": a message you sent does not
 * raise it, opening a thread lowers it, and a poll that returns the same data does not move it.
 * </para>
 * <para>
 * The first poll after mount only records the baseline. Without that, every visit to the Chat
 * page would announce the unread messages that were already sitting there — which is a summary of
 * old news, not a notification.
 * </para>
 * <para>
 * The tone is synthesised with the Web Audio API rather than loaded from a file. It needs no
 * asset, no network request and no decoding, and it cannot half-play because a sound file is
 * still downloading.
 * </para>
 */
export const useChatNotificationSound = (totalUnread: number) => {
  const enabledRef = useRef(false)
  const previousUnreadRef = useRef<number | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)

  // ── The setting ───────────────────────────────────────────────────────────────
  // Re-read when the tab regains focus as well as on mount, so turning the sound off in the
  // settings page (or in another tab) takes effect on returning here rather than on a reload.
  useEffect(() => {
    let cancelled = false

    const load = async () => {
      try {
        const res = await apiClient.get('/OmniSettings/client')
        if (!cancelled) {
          enabledRef.current = Boolean(res.data?.data?.chatNotificationSoundEnabled)
        }
      } catch {
        // A settings failure must not break the chat. Silence is the safe default: an
        // unexpected noise is worse than a missing one.
        if (!cancelled) enabledRef.current = false
      }
    }

    void load()

    const onVisible = () => {
      if (document.visibilityState === 'visible') void load()
    }

    document.addEventListener('visibilitychange', onVisible)

    return () => {
      cancelled = true
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [])

  // ── The trigger ───────────────────────────────────────────────────────────────
  useEffect(() => {
    const previous = previousUnreadRef.current
    previousUnreadRef.current = totalUnread

    // First reading after mount: baseline only.
    if (previous === null) return
    if (totalUnread <= previous) return
    if (!enabledRef.current) return

    try {
      // Created lazily and reused. Browsers cap how many AudioContexts a page may open, so one
      // per notification would eventually stop producing sound altogether.
      const AudioCtor =
        window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext

      if (!AudioCtor) return

      audioContextRef.current ??= new AudioCtor()
      const ctx = audioContextRef.current

      // Autoplay policy: a context created before the user has interacted with the page starts
      // suspended. Resuming is a no-op once they have clicked anything.
      if (ctx.state === 'suspended') void ctx.resume()

      const now = ctx.currentTime
      const oscillator = ctx.createOscillator()
      const gain = ctx.createGain()

      // Two quick notes rather than one — a rising pair reads as "arrived" and is distinguishable
      // from the system sounds around it.
      oscillator.type = 'sine'
      oscillator.frequency.setValueAtTime(880, now)
      oscillator.frequency.setValueAtTime(1170, now + 0.09)

      // Shaped envelope: an abrupt start and stop on a sine wave clicks audibly.
      gain.gain.setValueAtTime(0.0001, now)
      gain.gain.exponentialRampToValueAtTime(0.12, now + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.28)

      oscillator.connect(gain)
      gain.connect(ctx.destination)

      oscillator.start(now)
      oscillator.stop(now + 0.3)
    } catch {
      // Audio is a nicety. A browser that refuses to play must not take the inbox down with it.
    }
  }, [totalUnread])

  // Release the audio hardware when the chat is closed.
  useEffect(
    () => () => {
      void audioContextRef.current?.close()
      audioContextRef.current = null
    },
    []
  )
}

export default useChatNotificationSound
