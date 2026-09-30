import { useCallback, useEffect, useRef } from 'react'
import { apiClient } from '../services/apiClient'
import { realtimeService } from '../services/campaigns/campaignHubService'

/**
 * Where a custom notification sound is looked for.
 *
 * Drop an audio file at `Frontend/public/notification.mp3` and it is used instead of the
 * synthesised tone below — that is the supported way to install a specific sound, including the
 * one your phone plays. No such file ships with the app: WhatsApp's own notification audio is
 * theirs, not something this project can redistribute.
 */
const CUSTOM_SOUND_URL = '/notification.mp3'

/** How often the inbox is checked for new arrivals, app-wide. */
const POLL_MS = 20_000

/** How long the settings answer is trusted before being re-read. */
const SETTING_TTL_MS = 60_000

/**
 * Plays a notification tone whenever the inbox gains an unread message — on every page.
 *
 * <para>
 * Mounted once, in the signed-in shell. It used to live inside the Chat page, which meant it only
 * ran while Chat was open — and on that page the thread you are reading is marked read as you read
 * it, so the unread total usually did not move and no sound ever played. Somebody working on the
 * Dashboard, where a notification is actually useful, heard nothing at all.
 * </para>
 * <para>
 * One instance means one sound per arrival. The Chat page no longer plays its own; if it did, both
 * would fire for the same message.
 * </para>
 * <para>
 * The trigger is the unread total rising between two polls. That is the one signal meaning
 * "something arrived that you have not seen": a message you sent does not raise it, opening a
 * thread lowers it, and an unchanged poll does not move it. The first poll only records a
 * baseline, so signing in does not announce mail that was already waiting.
 * </para>
 */
export const useChatNotificationSound = () => {
  const enabledRef = useRef(false)
  const settingCheckedAtRef = useRef(0)
  const previousUnreadRef = useRef<number | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const customBufferRef = useRef<AudioBuffer | null>(null)
  const customCheckedRef = useRef(false)
  const inFlightRef = useRef(false)

  /** One shared context. Browsers cap how many a page may open. */
  const getContext = useCallback((): AudioContext | null => {
    const AudioCtor =
      window.AudioContext ??
      (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext

    if (!AudioCtor) return null

    audioContextRef.current ??= new AudioCtor()

    // Autoplay policy: a context created before the user has interacted with the page starts
    // suspended. Resuming is a no-op once they have clicked anything.
    if (audioContextRef.current.state === 'suspended') void audioContextRef.current.resume()

    return audioContextRef.current
  }, [])

  // Browsers refuse to produce sound until the page has been interacted with. Nudging the context
  // awake on the first click means the first notification is audible rather than silently dropped.
  useEffect(() => {
    const wake = () => {
      const ctx = audioContextRef.current
      if (ctx && ctx.state === 'suspended') void ctx.resume()
    }

    window.addEventListener('pointerdown', wake)
    window.addEventListener('keydown', wake)

    return () => {
      window.removeEventListener('pointerdown', wake)
      window.removeEventListener('keydown', wake)
    }
  }, [])

  /**
   * Loads a custom sound file once, if one has been installed.
   *
   * Decoded up front so the first notification is not delayed by a download. A missing file is the
   * normal case and is remembered, so this never becomes a request per message.
   */
  const loadCustomSound = useCallback(async () => {
    if (customCheckedRef.current) return
    customCheckedRef.current = true

    try {
      const res = await fetch(CUSTOM_SOUND_URL)

      // A dev server answers an unknown path with index.html rather than a 404, so the content
      // type is checked too — decoding HTML as audio would throw on every notification.
      const type = res.headers.get('content-type') ?? ''
      if (!res.ok || !type.startsWith('audio')) return

      const ctx = getContext()
      if (!ctx) return

      customBufferRef.current = await ctx.decodeAudioData(await res.arrayBuffer())
    } catch {
      // No custom sound installed, or it could not be decoded. The synthesised tone is used.
    }
  }, [getContext])

  useEffect(() => {
    void loadCustomSound()
  }, [loadCustomSound])

  /**
   * The built-in tone.
   *
   * <para>
   * Two ascending notes, each with its octave and a quiet fifth above, a near-instant attack and a
   * quick exponential decay — the shape a struck bell or a marimba makes, which is what a message
   * alert imitates. A bare sine with no overtones and no decay curve reads as a test beep instead.
   * </para>
   * <para>
   * Short (about a third of a second) and moderate in level. This fires while someone is working.
   * </para>
   */
  const playSynthesised = useCallback((ctx: AudioContext) => {
    const start = ctx.currentTime

    // A gentle low-pass takes the glassy edge off the upper partials.
    const filter = ctx.createBiquadFilter()
    filter.type = 'lowpass'
    filter.frequency.value = 6000
    filter.connect(ctx.destination)

    const strike = (frequency: number, at: number, gain: number) => {
      ;[
        { ratio: 1, level: gain },
        { ratio: 2, level: gain * 0.3 },
        { ratio: 3, level: gain * 0.12 },
      ].forEach(({ ratio, level }) => {
        const osc = ctx.createOscillator()
        const env = ctx.createGain()

        osc.type = 'sine'
        osc.frequency.value = frequency * ratio

        env.gain.setValueAtTime(0.0001, at)
        env.gain.exponentialRampToValueAtTime(level, at + 0.005)  // struck, not faded in
        env.gain.exponentialRampToValueAtTime(0.0001, at + 0.3)   // rings out

        osc.connect(env)
        env.connect(filter)

        osc.start(at)
        osc.stop(at + 0.32)
      })
    }

    strike(1318.5, start, 0.18)          // E6
    strike(1760.0, start + 0.08, 0.15)   // A6, a beat later and slightly softer
  }, [])

  const play = useCallback(() => {
    try {
      const ctx = getContext()
      if (!ctx) return

      if (customBufferRef.current) {
        const source = ctx.createBufferSource()
        const gain = ctx.createGain()
        gain.gain.value = 0.8

        source.buffer = customBufferRef.current
        source.connect(gain)
        gain.connect(ctx.destination)
        source.start()
        return
      }

      playSynthesised(ctx)
    } catch {
      // Audio is a nicety. A browser that refuses to play must not take anything else down.
    }
  }, [getContext, playSynthesised])

  /** Re-reads the on/off setting, but not more than once a minute. */
  const isEnabled = useCallback(async () => {
    const now = Date.now()
    if (now - settingCheckedAtRef.current < SETTING_TTL_MS) return enabledRef.current

    settingCheckedAtRef.current = now

    try {
      const res = await apiClient.get('/OmniSettings/client')
      enabledRef.current = Boolean(res.data?.data?.chatNotificationSoundEnabled)
    } catch {
      // Silence is the safe default: an unexpected noise is worse than a missing one.
      enabledRef.current = false
    }

    return enabledRef.current
  }, [])

  // ── The watch ─────────────────────────────────────────────────────────────────
  // Pushed, not polled: the server announces each inbound message over the real-time connection.
  // Only while that connection is down does a slow poll of the unread total stand in for it.
  useEffect(() => {
    let stopped = false
    let fallbackTimer: number | null = null

    const unsubscribeInbox = realtimeService.subscribeInbox((event) => {
      if (event.type !== 'messageReceived') return
      void isEnabled().then((enabled) => {
        if (enabled && !stopped) play()
      })
    })

    const checkUnread = async () => {
      if (stopped || inFlightRef.current) return
      inFlightRef.current = true
      try {
        const res = await apiClient.get('/Chat/conversations', { params: { filter: 'Unread Chats', limit: 100 } })
        const conversations: { unreadCount?: number }[] = res.data?.data ?? []
        const total = conversations.reduce((sum, c) => sum + (c.unreadCount ?? 0), 0)

        const previous = previousUnreadRef.current
        previousUnreadRef.current = total
        if (previous !== null && total > previous && (await isEnabled())) play()
      } catch {
        // A failed poll leaves the baseline untouched.
      } finally {
        inFlightRef.current = false
      }
    }

    const unsubscribeState = realtimeService.onStateChange((state) => {
      if (state === 'connected') {
        if (fallbackTimer !== null) window.clearInterval(fallbackTimer)
        fallbackTimer = null
        previousUnreadRef.current = null
      } else if (fallbackTimer === null) {
        void checkUnread()
        fallbackTimer = window.setInterval(checkUnread, POLL_MS)
      }
    })

    return () => {
      stopped = true
      if (fallbackTimer !== null) window.clearInterval(fallbackTimer)
      unsubscribeState()
      unsubscribeInbox()
    }
  }, [isEnabled, play])

  // Release the audio hardware when the shell unmounts (sign-out).
  useEffect(
    () => () => {
      void audioContextRef.current?.close()
      audioContextRef.current = null
    },
    []
  )
}

export default useChatNotificationSound
