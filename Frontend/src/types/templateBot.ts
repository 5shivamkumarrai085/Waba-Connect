export interface TemplateBotVariable {
  variableName: string
  variableValue?: string
  mergeField?: string
}

export interface TemplateBot {
  id: number
  name: string
  relationType: string
  templateId: number
  templateName: string
  replyType: string
  triggerKeyword: string
  isActive: boolean
  createdAt: string
  updatedAt: string
  variables: TemplateBotVariable[]
}
