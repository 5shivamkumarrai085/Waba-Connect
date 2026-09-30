/**
 * The app's single real-time connection (SignalR, /hubs/campaign).
 *
 * - One shared connection, reference-counted: it starts with the first subscriber and stops a
 *   short grace period after the last one leaves, so React StrictMode's mount → unmount → mount
 *   does not tear it down and rebuild it.
 * - The first connect is retried with backoff (SignalR's automatic reconnect only covers a
 *   connection that was once up), so a backend that is still starting is not a permanent failure.
 * - The access token is fetched fresh for every (re)connect, refreshing the session if needed.
 * - Connection state is observable, so pages fall back to polling while it is down and reload
 *   their data after a reconnect (events sent while disconnected are not replayed).
 *
 * The server decides membership from the caller's permissions (Campaign.View → campaign events,
 * Chat.View → inbox events); payloads carry ids and counter deltas only.
 */

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { API_BASE_URL, getValidAccessToken } from '../apiClient'

// ── Payloads ──────────────────────────────────────────────────────────────────

/** Must stay in sync with EventPublisherConsumer.cs. */
export interface CampaignEventPayload {
  campaignId: number
  kind: string
  campaignContactId: number | null
  occurredAt: string

  // Counter deltas — the client applies these without a reload.
  sentDelta: number
  failedDelta: number
  deliveredDelta: number
  bouncedDelta: number
  openedDelta: number
  clickedDelta: number
  repliedDelta: number
  unsubscribedDelta: number
  complainedDelta: number
  /** WhatsApp read receipts. */
  readDelta: number

  /** Non-null only when the campaign reached a new final status. */
  newCampaignStatus: string | null
}

/** Must stay in sync with SignalRInboxNotifier.cs. */
export interface InboxEventPayload {
  type: 'messageReceived' | 'messageStatus' | 'conversationUpdated'
  conversationId: number
  connectionId?: number | null
  messageId?: number
  status?: string
}

export type RealtimeState = 'connecting' | 'connected' | 'disconnected'

type CampaignHandler = (event: CampaignEventPayload) => void
type InboxHandler = (event: InboxEventPayload) => void
type DashboardHandler = () => void
type StateHandler = (state: RealtimeState, info: { reconnected: boolean }) => void

const HUB_URL = API_BASE_URL.replace(/\/api\/?$/, '') + '/hubs/campaign'
const DISCONNECT_GRACE_MS = 3_000
const INITIAL_RETRY_DELAYS = [1_000, 2_000, 5_000, 10_000, 30_000]

// ── Service ───────────────────────────────────────────────────────────────────

class RealtimeService {
  private connection: HubConnection | null = null
  private starting: Promise<void> | null = null
  private state: RealtimeState = 'disconnected'
  private subscriberCount = 0
  private stopTimer: ReturnType<typeof setTimeout> | null = null
  private retryTimer: ReturnType<typeof setTimeout> | null = null
  private retryAttempt = 0

  /** campaignId → handlers; key 0 receives every campaign's events (list views). */
  private campaignHandlers = new Map<number, Set<CampaignHandler>>()
  private inboxHandlers = new Set<InboxHandler>()
  private dashboardHandlers = new Set<DashboardHandler>()
  private stateHandlers = new Set<StateHandler>()

  /** Events for one campaign (or every campaign when campaignId is falsy). */
  subscribe(campaignId: number | null | undefined, handler: CampaignHandler): () => void {
    const id = campaignId || 0
    if (!this.campaignHandlers.has(id)) this.campaignHandlers.set(id, new Set())
    this.campaignHandlers.get(id)!.add(handler)

    const release = this.acquire()
    if (id > 0) void this.invokeSafely('JoinCampaign', id)

    return () => {
      const set = this.campaignHandlers.get(id)
      set?.delete(handler)
      if (set?.size === 0) this.campaignHandlers.delete(id)
      release()
    }
  }

  /** Inbox activity: new messages and status changes, by conversation id. */
  subscribeInbox(handler: InboxHandler): () => void {
    this.inboxHandlers.add(handler)
    const release = this.acquire()
    return () => {
      this.inboxHandlers.delete(handler)
      release()
    }
  }

  /**
   * "The dashboard's numbers changed" (server-debounced, data-free — see DashboardNotifier.cs).
   * The handler refetches its own summary.
   */
  subscribeDashboard(handler: DashboardHandler): () => void {
    this.dashboardHandlers.add(handler)
    const release = this.acquire()
    return () => {
      this.dashboardHandlers.delete(handler)
      release()
    }
  }

  /** Connection state changes. Called immediately with the current state. */
  onStateChange(handler: StateHandler): () => void {
    this.stateHandlers.add(handler)
    handler(this.state, { reconnected: false })
    return () => {
      this.stateHandlers.delete(handler)
    }
  }

  get isConnected(): boolean {
    return this.state === 'connected'
  }

  // ── Reference counting ──────────────────────────────────────────────────────

  private acquire(): () => void {
    this.subscriberCount++
    if (this.stopTimer) {
      clearTimeout(this.stopTimer)
      this.stopTimer = null
    }
    void this.ensureStarted()

    let released = false
    return () => {
      if (released) return
      released = true
      this.subscriberCount = Math.max(0, this.subscriberCount - 1)
      if (this.subscriberCount === 0) {
        this.stopTimer = setTimeout(() => void this.stop(), DISCONNECT_GRACE_MS)
      }
    }
  }

  // ── Connection lifecycle ────────────────────────────────────────────────────

  private setState(next: RealtimeState, reconnected = false) {
    if (this.state === next && !reconnected) return
    this.state = next
    this.stateHandlers.forEach((h) => h(next, { reconnected }))
  }

  private build(): HubConnection {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        // Fresh (and if necessary renewed) token for every negotiate and reconnect.
        accessTokenFactory: async () => (await getValidAccessToken()) ?? '',
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) => [0, 2_000, 5_000, 10_000][ctx.previousRetryCount] ?? 30_000,
      })
      .configureLogging(import.meta.env.DEV ? LogLevel.Warning : LogLevel.Error)
      .build()

    connection.on('campaignEvent', (payload: CampaignEventPayload) => {
      this.campaignHandlers.get(payload.campaignId)?.forEach((h) => h(payload))
      this.campaignHandlers.get(0)?.forEach((h) => h(payload))
    })

    connection.on('inboxEvent', (payload: InboxEventPayload) => {
      this.inboxHandlers.forEach((h) => h(payload))
    })

    connection.on('dashboardChanged', () => {
      this.dashboardHandlers.forEach((h) => h())
    })

    connection.onreconnecting(() => this.setState('connecting'))
    connection.onreconnected(() => {
      // Campaign detail pages re-join (a no-op server-side, kept for older servers) and every
      // subscriber is told to reload, because events sent while offline are not replayed.
      this.campaignHandlers.forEach((_, id) => {
        if (id > 0) void this.invokeSafely('JoinCampaign', id)
      })
      this.setState('connected', true)
    })
    connection.onclose(() => {
      this.setState('disconnected')
      // Closed for good (automatic reconnect gave up): start over while anyone still listens.
      if (this.subscriberCount > 0 && this.connection === connection) {
        this.connection = null
        this.scheduleRetry()
      }
    })

    return connection
  }

  private ensureStarted(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return Promise.resolve()
    if (this.starting) return this.starting

    this.starting = this.start().finally(() => {
      this.starting = null
    })
    return this.starting
  }

  private async start(): Promise<void> {
    if (!(await getValidAccessToken())) return // not signed in: nothing to connect as

    if (!this.connection) this.connection = this.build()
    if (this.connection.state !== HubConnectionState.Disconnected) return

    this.setState('connecting')
    try {
      await this.connection.start()
      this.retryAttempt = 0
      this.setState('connected')
    } catch {
      this.setState('disconnected')
      this.connection = null
      this.scheduleRetry()
    }
  }

  private scheduleRetry() {
    if (this.retryTimer || this.subscriberCount === 0) return
    const delay = INITIAL_RETRY_DELAYS[Math.min(this.retryAttempt, INITIAL_RETRY_DELAYS.length - 1)]
    this.retryAttempt++
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null
      if (this.subscriberCount > 0) void this.ensureStarted()
    }, delay)
  }

  private async stop(): Promise<void> {
    this.stopTimer = null
    if (this.retryTimer) {
      clearTimeout(this.retryTimer)
      this.retryTimer = null
    }

    const connection = this.connection
    this.connection = null
    this.retryAttempt = 0
    try {
      await connection?.stop()
    } catch {
      // Intentional stop; nothing to report.
    }
    this.setState('disconnected')
  }

  private async invokeSafely(method: string, ...args: unknown[]) {
    try {
      await this.ensureStarted()
      if (this.connection?.state === HubConnectionState.Connected) {
        await this.connection.invoke(method, ...args)
      }
    } catch {
      // Best-effort: membership is decided server-side on connect anyway.
    }
  }
}

export const realtimeService = new RealtimeService()

/** Kept under its original name for existing imports. */
export const campaignHubService = realtimeService
