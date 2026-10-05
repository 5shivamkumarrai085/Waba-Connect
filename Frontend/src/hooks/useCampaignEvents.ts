/**
 * Live campaign figures.
 *
 * Applies real-time counter deltas to the campaign store as they arrive. Two safety nets make
 * the numbers trustworthy rather than merely usually right:
 *  - after a reconnect, `onResync` reloads from the server, because events sent while the
 *    connection was down are not replayed;
 *  - while the connection is down, `onResync` runs on a slow poll so the page keeps moving.
 */

import { useEffect, useRef } from 'react'
import { realtimeService, type CampaignEventPayload } from '../services/campaigns/campaignHubService'
import { useCampaignStore } from '../store/campaignStore'
import { AUTH_TOKEN_STORAGE_KEY, REFRESH_TOKEN_STORAGE_KEY } from '../services/apiClient'

const FALLBACK_POLL_MS = 15_000

export function useCampaignEvents(campaignId?: number | null, onResync?: () => void, onEvent?: (event: CampaignEventPayload) => void): void {
  const applyEventDelta = useCampaignStore((state) => state.applyEventDelta)

  // Held in refs so a new callback identity on every render does not resubscribe.
  const resyncRef = useRef(onResync)
  const eventRef = useRef(onEvent)
  useEffect(() => {
    resyncRef.current = onResync
    eventRef.current = onEvent
  }, [onResync, onEvent])

  useEffect(() => {
    const signedIn = localStorage.getItem(AUTH_TOKEN_STORAGE_KEY) || localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)
    if (!signedIn) return

    let pollTimer: ReturnType<typeof setInterval> | null = null
    const stopPolling = () => {
      if (pollTimer) clearInterval(pollTimer)
      pollTimer = null
    }

    const unsubscribeEvents = realtimeService.subscribe(campaignId, (event: CampaignEventPayload) => {
      applyEventDelta(event)
      eventRef.current?.(event)
    })

    const unsubscribeState = realtimeService.onStateChange((state, { reconnected }) => {
      if (state === 'connected') {
        stopPolling()
        if (reconnected) resyncRef.current?.()
      } else if (!pollTimer && resyncRef.current) {
        pollTimer = setInterval(() => {
          if (document.visibilityState === 'visible') resyncRef.current?.()
        }, FALLBACK_POLL_MS)
      }
    })

    return () => {
      stopPolling()
      unsubscribeState()
      unsubscribeEvents()
    }
  }, [campaignId, applyEventDelta])
}
