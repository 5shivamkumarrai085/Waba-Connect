import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/layout.css'
import './styles/components.css'
import './styles/dashboard.css'
import './styles/campaign.css'
import './styles/Skeleton.css'
import './styles/responsive.css'

/* Eager page-level stylesheet imports to resolve lazy-load race conditions & HMR CSS loss */
import './pages/ActivityLogs.css'
import './pages/Reporting.css'
import './pages/Templates/TemplatesList.css'
import './pages/Campaigns/CampaignsList.css'
import './pages/Campaigns/CampaignDetails.css'
import './pages/Campaigns/CampaignWizard.css'
import './pages/Contacts/AddContact.css'
import './pages/Contacts/ContactsList.css'
import './pages/Contacts/ImportContacts.css'
import './pages/Chat/Chat.css'
import './pages/BulkCampaign/BulkCampaign.css'
import './pages/ConnectWABA/ConnectWABA.css'
import './pages/MessageBot/MessageBotList.css'
import './pages/MessageBot/MessageBotWizard.css'
import './pages/TemplateBot/TemplateBotList.css'
import './pages/TemplateBot/TemplateBotWizard.css'
import './pages/BotFlow/BotFlowList.css'
import './pages/BotFlow/BotFlowDesigner.css'

import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
