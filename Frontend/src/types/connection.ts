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
