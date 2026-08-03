export interface BotFlow {
  id: number
  name: string
  description?: string
  isActive: boolean
  flowData: string // Serialized JSON string
  createdAt: string
  updatedAt: string

  // Null = fires on all connections. Set = scoped to one connection only.
  connectionId?: number | null
  connectionName?: string | null
}
