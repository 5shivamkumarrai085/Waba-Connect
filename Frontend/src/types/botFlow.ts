export interface BotFlow {
  id: number
  name: string
  description?: string
  isActive: boolean
  flowData: string // Serialized JSON string
  createdAt: string
  updatedAt: string
}
