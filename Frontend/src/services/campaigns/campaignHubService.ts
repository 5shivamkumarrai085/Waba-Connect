/**
 * campaignHubService.ts
 *
 * Manages the SignalR connection to CampaignHub (/hubs/campaign).
 *
 * Design:
 * - One singleton connection shared across the app (not one per component).
 * - Lazy-connect: the connection is only started when the first component subscribes.
 * - Auto-reconnect with exponential backoff (handled by @microsoft/signalr).
 * - JWT token read from localStorage on every negotiate call, so a refreshed token
 *   is picked up without restarting the connection.
 * - subscribe() returns an unsubscribe function for use in useEffect cleanup.
 *
 * Usage:
 *   const unsub = campaignHubService.subscribe(campaignId, (event) => { ... })
 *   // on unmount:
 *   unsub()
 */

import {
  HubConnectionBuilder,
  HubConnection,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { AUTH_TOKEN_STORAGE_KEY } from '../apiClient'

// ── Types ─────────────────────────────────────────────────────────────────────

/**
 * The payload shape the backend sends via  hub.Clients.Group(...).SendAsync("campaignEvent", ...)
 * Must stay in sync with EventPublisherConsumer.cs
 */
export interface CampaignEventPayload {
  campaignId: number
  kind: string                   // EmailEventKind enum value as string
  campaignContactId: number | null
  recipientAddress: string | null
  occurredAt: string             // ISO 8601

  // Counter deltas — apply these to the local Zustand state
  sentDelta: number
  failedDelta: number
  deliveredDelta: number
  bouncedDelta: number
  openedDelta: number
  clickedDelta: number
  repliedDelta: number
  unsubscribedDelta: number
  complainedDelta: number

  // Non-null only when the campaign reaches a terminal state
  newCampaignStatus: string | null
}

type EventHandler = (event: CampaignEventPayload) => void

// ── Hub URL ───────────────────────────────────────────────────────────────────

/** Strip /api suffix from the base URL — SignalR hubs live at the root, not under /api. */
const getHubUrl = (): string => {
  const base = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5155/api'
  return base.replace(/\/api\/?$/, '') + '/hubs/campaign'
}

// ── Service ───────────────────────────────────────────────────────────────────

class CampaignHubService {
  private connection: HubConnection | null = null
  /** campaignId → Set<handler> */
  private handlers = new Map<number, Set<EventHandler>>()
  /** Subscribed group IDs (sent a JoinCampaign). */
  private joinedGroups = new Set<number>()
  private connectingPromise: Promise<void> | null = null

  // ── Public API ──────────────────────────────────────────────────────────────

  /**
   * Subscribe to real-time events for a specific campaign or all campaigns (if campaignId is null/undefined/0).
   * Returns an unsubscribe function to be called from useEffect cleanup.
   */
  subscribe(campaignId: number | null | undefined, handler: EventHandler): () => void {
    const id = campaignId || 0
    // Register handler
    if (!this.handlers.has(id)) {
      this.handlers.set(id, new Set())
    }
    this.handlers.get(id)!.add(handler)

    // Ensure connected, then join the group if specific campaign
    this.ensureConnected().then(() => {
      if (id > 0) {
        this.joinGroup(id)
      }
    })

    return () => {
      this.handlers.get(id)?.delete(handler)

      // Leave server group if nobody is listening to this campaign anymore
      if (this.handlers.get(id)?.size === 0) {
        this.handlers.delete(id)
        if (id > 0) {
          this.leaveGroup(id)
        }
      }

      // If no handlers remain at all, disconnect
      if (this.handlers.size === 0) {
        this.disconnect()
      }
    }
  }

  // ── Connection management ────────────────────────────────────────────────────

  private ensureConnected(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) {
      return Promise.resolve()
    }
    if (this.connectingPromise) {
      return this.connectingPromise
    }

    this.connectingPromise = this.connect().finally(() => {
      this.connectingPromise = null
    })

    return this.connectingPromise
  }

  private async connect(): Promise<void> {
    this.connection = new HubConnectionBuilder()
      .withUrl(getHubUrl(), {
        // Token read on every negotiate — handles token refresh transparently.
        accessTokenFactory: () =>
          localStorage.getItem(AUTH_TOKEN_STORAGE_KEY) ?? '',
      })
      .withAutomaticReconnect({
        // 0s, 2s, 10s, 30s, then 30s forever
        nextRetryDelayInMilliseconds: (retryContext) => {
          const delays = [0, 2000, 10000, 30000]
          return delays[retryContext.previousRetryCount] ?? 30000
        },
      })
      .configureLogging(
        import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning,
      )
      .build()

    // Register the server-to-client event listener
    this.connection.on('campaignEvent', (payload: CampaignEventPayload) => {
      // Specific campaign handlers
      const set = this.handlers.get(payload.campaignId)
      if (set) {
        set.forEach((h) => h(payload))
      }
      // Global handlers (key 0)
      const globalSet = this.handlers.get(0)
      if (globalSet) {
        globalSet.forEach((h) => h(payload))
      }
    })

    // Re-join groups after automatic reconnect
    this.connection.onreconnected(() => {
      this.joinedGroups.forEach((id) => {
        this.connection?.invoke('JoinCampaign', id).catch(() => {})
      })
    })

    await this.connection.start()

    // Re-join any groups that were subscribed before connect() resolved
    this.joinedGroups.forEach((id) => {
      this.connection?.invoke('JoinCampaign', id).catch(() => {})
    })
  }

  private async joinGroup(campaignId: number): Promise<void> {
    if (this.joinedGroups.has(campaignId)) return
    this.joinedGroups.add(campaignId)
    try {
      await this.connection?.invoke('JoinCampaign', campaignId)
    } catch {
      // Will be retried on reconnect
    }
  }

  private async leaveGroup(campaignId: number): Promise<void> {
    this.joinedGroups.delete(campaignId)
    try {
      await this.connection?.invoke('LeaveCampaign', campaignId)
    } catch {
      // Best-effort
    }
  }

  private async disconnect(): Promise<void> {
    this.joinedGroups.clear()
    try {
      await this.connection?.stop()
    } catch {
      // Ignore errors on intentional disconnect
    }
    this.connection = null
  }
}

export const campaignHubService = new CampaignHubService()
