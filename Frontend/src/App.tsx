import React, { lazy, Suspense } from 'react'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { PageLayout } from './components/PageLayout'

// Lazy loaded page components
const Dashboard = lazy(() => import('./pages/Dashboard').then(m => ({ default: m.Dashboard })))
const CampaignsList = lazy(() => import('./pages/Campaigns/CampaignsList').then(m => ({ default: m.CampaignsList })))
const CampaignWizard = lazy(() => import('./pages/Campaigns/CampaignWizard').then(m => ({ default: m.CampaignWizard })))
const CampaignDetails = lazy(() => import('./pages/Campaigns/CampaignDetails').then(m => ({ default: m.CampaignDetails })))
const Placeholder = lazy(() => import('./pages/Placeholder').then(m => ({ default: m.Placeholder })))
const Reporting = lazy(() => import('./pages/Reporting').then(m => ({ default: m.Reporting })))
const ActivityLogs = lazy(() => import('./pages/ActivityLogs').then(m => ({ default: m.ActivityLogs })))
const ConnectWABA = lazy(() => import('./pages/ConnectWABA/ConnectWABA').then(m => ({ default: m.ConnectWABA })))
const ContactsList = lazy(() => import('./pages/Contacts/ContactsList').then(m => ({ default: m.ContactsList })))
const AddContact = lazy(() => import('./pages/Contacts/AddContact').then(m => ({ default: m.AddContact })))
const ImportContacts = lazy(() => import('./pages/Contacts/ImportContacts').then(m => ({ default: m.ImportContacts })))
const TemplatesList = lazy(() => import('./pages/Templates/TemplatesList').then(m => ({ default: m.TemplatesList })))
const BulkCampaign = lazy(() => import('./pages/BulkCampaign/BulkCampaign').then(m => ({ default: m.BulkCampaign })))
const Chat = lazy(() => import('./pages/Chat/Chat').then(m => ({ default: m.Chat })))
const MessageBotList = lazy(() => import('./pages/MessageBot/MessageBotList').then(m => ({ default: m.MessageBotList })))
const MessageBotWizard = lazy(() => import('./pages/MessageBot/MessageBotWizard').then(m => ({ default: m.MessageBotWizard })))
const TemplateBotList = lazy(() => import('./pages/TemplateBot/TemplateBotList').then(m => ({ default: m.TemplateBotList })))
const TemplateBotWizard = lazy(() => import('./pages/TemplateBot/TemplateBotWizard').then(m => ({ default: m.TemplateBotWizard })))
const BotFlowList = lazy(() => import('./pages/BotFlow/BotFlowList').then(m => ({ default: m.BotFlowList })))
const BotFlowDesigner = lazy(() => import('./pages/BotFlow/BotFlowDesigner').then(m => ({ default: m.BotFlowDesigner })))
const ConnectionsList = lazy(() => import('./pages/Connections/ConnectionsList').then(m => ({ default: m.ConnectionsList })))
const ConnectNewWabaPage = lazy(() => import('./pages/Connections/ConnectNewWabaPage').then(m => ({ default: m.ConnectNewWabaPage })))
const UserPermissionsList = lazy(() => import('./pages/Permissions/UserPermissionsList').then(m => ({ default: m.UserPermissionsList })))
const DepartmentPermissionsList = lazy(() => import('./pages/Permissions/DepartmentPermissionsList').then(m => ({ default: m.DepartmentPermissionsList })))

// Reusable Loading Fallback
const LoadingFallback: React.FC = () => (
  <div className="module-loader">
    <p className="module-loader-text">Loading module...</p>
  </div>
)

const App: React.FC = () => {
  return (
    <BrowserRouter>
      <PageLayout>
        <Toaster 
          position="top-right" 
          containerStyle={{ zIndex: 999999 }}
          toastOptions={{
            duration: 3000,
            style: { zIndex: 999999 },
            error: {
              duration: 4000
            },
            success: {
              duration: 3000
            }
          }}
        />
        <Suspense fallback={<LoadingFallback />}>
          <Routes>
            <Route path="/" element={<Dashboard />} />
            <Route path="/campaigns/campaign" element={<CampaignsList />} />
            <Route path="/campaigns/campaign/create" element={<CampaignWizard />} />
            <Route path="/campaigns/campaign/edit/:id" element={<CampaignWizard />} />
            <Route path="/campaigns/campaign/details/:id" element={<CampaignDetails />} />
            
            {/* Completed high-fidelity pages */}
            <Route path="/reporting" element={<Reporting />} />
            <Route path="/activity-logs" element={<ActivityLogs />} />
            <Route path="/connections" element={<ConnectionsList />} />
            <Route path="/connections/new" element={<ConnectNewWabaPage />} />
            <Route path="/admin/permissions/user" element={<UserPermissionsList />} />
            <Route path="/admin/permissions/department" element={<DepartmentPermissionsList />} />
            <Route path="/connect-waba" element={<ConnectWABA />} />
            <Route path="/contacts" element={<ContactsList />} />
            <Route path="/contacts/contact" element={<AddContact />} />
            <Route path="/contacts/contact/edit/:id" element={<AddContact />} />
            <Route path="/contacts/import" element={<ImportContacts />} />
            <Route path="/templates" element={<TemplatesList />} />
            <Route path="/bulk-campaigns" element={<BulkCampaign />} />
            
            <Route path="/message-bot" element={<MessageBotList />} />
            <Route path="/message-bot/bot" element={<MessageBotWizard />} />
            <Route path="/message-bot/bot/:id" element={<MessageBotWizard />} />

            <Route path="/template-bot" element={<TemplateBotList />} />
            <Route path="/template-bot/bot" element={<TemplateBotWizard />} />
            <Route path="/template-bot/bot/:id" element={<TemplateBotWizard />} />
            <Route path="/bot-flow" element={<BotFlowList />} />
            <Route path="/bot-flow/designer/:id" element={<BotFlowDesigner />} />
            <Route path="/chat" element={<Chat />} />
            <Route path="/system-settings" element={<Placeholder />} />
            <Route path="/omniconnect-settings" element={<Placeholder />} />
            <Route path="/setup" element={<Placeholder />} />
            
            {/* Fallback route */}
            <Route path="*" element={<Placeholder />} />
          </Routes>
        </Suspense>
      </PageLayout>
    </BrowserRouter>
  )
}

export default App

