export interface Connection {
  id: number
  name: string
  nickname?: string
  description?: string
  isActive: boolean
  phoneNumber?: string
  phoneNumberId?: string
  displayName?: string
  verifiedName?: string
  wabaId?: string
  isConnected: boolean
  /** Whether the stored WhatsApp credentials are still complete enough to reach Meta. */
  hasCredentials: boolean
  /** Whether a sender number is attached. Without one nothing can be sent or received. */
  hasPhoneNumber: boolean
  /**
   * The one status the whole app agrees on: "Connected", "Setup pending" or "Disconnected".
   * Derived on the server, because this list and Chat used to derive it separately and disagree.
   */
  status: string
  connectedOn?: string
  createdAt: string
}

export interface ConnectionDashboard {
  totalConnections: number
  connectedCount: number
  disconnectedCount: number
  totalConnectedNumbers: number
  connections: Connection[]
}

export interface CreateConnectionPayload {
  name: string
  nickname?: string
  description?: string
}

export interface UpdateConnectionPayload {
  name?: string
  nickname?: string
  description?: string
  isActive?: boolean
}
