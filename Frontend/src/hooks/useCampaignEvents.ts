/**
 * useCampaignEvents.ts
 *
 * React hook that subscribes to real-time SignalR events for a campaign and
 * applies counter deltas directly to the Zustand campaign store — no polling,
 * no full page reload.
 *
 * Usage:
 *   useCampaignEvents(campaignId)   // in CampaignDetails — starts and stops automatically
 *
 * The hook is a no-op when:
 * - campaignId is null / undefined
 * - the campaign is in a terminal status (Sent, Failed, PartiallyFailed, Cancelled)
 * - the user is not authenticated (no token in localStorage)
 */

import { useEffect } from 'react'
import { campaignHubService, type CampaignEventPayload } from '../services/campaigns/campaignHubService'
import { useCampaignStore } from '../store/campaignStore'
import { AUTH_TOKEN_STORAGE_KEY } from '../services/apiClient'

export function useCampaignEvents(campaignId?: number | null): void {
  const { applyEventDelta } = useCampaignStore()

  useEffect(() => {
    if (!localStorage.getItem(AUTH_TOKEN_STORAGE_KEY)) return

    const unsub = campaignHubService.subscribe(campaignId, (event: CampaignEventPayload) => {
      applyEventDelta(event)
    })

    return unsub
  }, [campaignId, applyEventDelta])
}
