export interface MessageBot {
  id: number
  name: string
  relationType: string
  replyText: string
  replyType: string
  triggerKeyword: string
  header?: string
  footer?: string
  isActive: boolean
  optionType: string
  
  // Reply Buttons
  button1?: string
  button1Id?: string
  button2?: string
  button2Id?: string
  button3?: string
  button3Id?: string
  
  // CTA URL
  ctaButtonName?: string
  ctaButtonLink?: string
  
  // Files
  fileType?: string
  fileName?: string
  fileUrl?: string
  
  // Assistant
  assistantName?: string
  
  createdAt: string
  updatedAt: string
}

export interface CreateMessageBotRequest {
  name: string
  relationType: string
  replyText: string
  replyType: string
  triggerKeyword: string
  header?: string
  footer?: string
  isActive: boolean
  optionType: string
  
  button1?: string
  button1Id?: string
  button2?: string
  button2Id?: string
  button3?: string
  button3Id?: string
  
  ctaButtonName?: string
  ctaButtonLink?: string
  
  fileType?: string
  fileName?: string
  fileUrl?: string
  
  assistantName?: string
}

export interface UpdateMessageBotRequest extends CreateMessageBotRequest {}
