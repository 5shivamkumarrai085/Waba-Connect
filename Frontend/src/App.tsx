import React, { lazy, Suspense } from 'react'
import { BrowserRouter, Routes, Route, Outlet, Navigate } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { PageLayout } from './components/PageLayout'
import { RequireAuth } from './components/RequireAuth'
import Can from './components/Can/Can'

// Lazy loaded page components
const Dashboard = lazy(() => import('./pages/Dashboard').then(m => ({ default: m.Dashboard })))
const CampaignsList = lazy(() => import('./pages/Campaigns/CampaignsList').then(m => ({ default: m.CampaignsList })))
const CampaignWizard = lazy(() => import('./pages/Campaigns/CampaignWizard').then(m => ({ default: m.CampaignWizard })))
const CampaignDetails = lazy(() => import('./pages/Campaigns/CampaignDetails').then(m => ({ default: m.CampaignDetails })))
const Placeholder = lazy(() => import('./pages/Placeholder').then(m => ({ default: m.Placeholder })))
const NotFound = lazy(() => import('./pages/NotFound').then(m => ({ default: m.NotFound })))
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
const Login = lazy(() => import('./pages/Auth/Login').then(m => ({ default: m.Login })))
const ChangePassword = lazy(() => import('./pages/Auth/ChangePassword').then(m => ({ default: m.ChangePassword })))
const OmniConnectSettings = lazy(() => import('./pages/OmniSettings/OmniConnectSettings').then(m => ({ default: m.OmniConnectSettings })))
const SetupLayout = lazy(() => import('./pages/Setup/SetupLayout').then(m => ({ default: m.SetupLayout })))
const SetupUsersList = lazy(() => import('./pages/Setup/Users/UsersList').then(m => ({ default: m.UsersList })))
const SetupUserForm = lazy(() => import('./pages/Setup/Users/UserForm').then(m => ({ default: m.UserForm })))
const SetupRolesList = lazy(() => import('./pages/Setup/Roles/RolesList').then(m => ({ default: m.RolesList })))
const SetupRoleForm = lazy(() => import('./pages/Setup/Roles/RoleForm').then(m => ({ default: m.RoleForm })))
const SetupStatusList = lazy(() => import('./pages/Setup/Lookups/StatusList').then(m => ({ default: m.StatusList })))
const SetupSourceList = lazy(() => import('./pages/Setup/Lookups/SourceList').then(m => ({ default: m.SourceList })))
const SetupTypeList = lazy(() => import('./pages/Setup/Lookups/TypeList').then(m => ({ default: m.TypeList })))
const SetupGroupsList = lazy(() => import('./pages/Setup/Groups/GroupsList').then(m => ({ default: m.GroupsList })))
const SetupLanguagesList = lazy(() => import('./pages/Setup/Lookups/LanguagesList').then(m => ({ default: m.LanguagesList })))
const SetupTranslateScreen = lazy(() => import('./pages/Setup/Lookups/TranslateScreen').then(m => ({ default: m.TranslateScreen })))
const SetupAiPromptsList = lazy(() => import('./pages/Setup/AiPrompts/AiPromptsList').then(m => ({ default: m.AiPromptsList })))
const SetupCannedRepliesList = lazy(() => import('./pages/Setup/CannedReplies/CannedRepliesList').then(m => ({ default: m.CannedRepliesList })))
const SetupEmailTemplatesList = lazy(() => import('./pages/Setup/EmailTemplates/EmailTemplatesList').then(m => ({ default: m.EmailTemplatesList })))
const SetupActivityLog = lazy(() => import('./pages/Setup/ActivityLog/MessageActivityLog').then(m => ({ default: m.MessageActivityLog })))
const SetupSystemLogs = lazy(() => import('./pages/Setup/SystemLogs/SystemLogs').then(m => ({ default: m.SystemLogs })))

// Reusable Loading Fallback
const LoadingFallback: React.FC = () => (
  <div className="module-loader">
    <p className="module-loader-text">Loading module...</p>
  </div>
)

/**
 * The signed-in shell: sidebar, header, and the page container. Everything inside requires a
 * session, so the guard is applied once here rather than repeated on ~30 routes.
 */
const AuthedShell: React.FC = () => (
  <RequireAuth>
    <PageLayout>
      <Outlet />
    </PageLayout>
  </RequireAuth>
)

/**
 * Route-level permission gate.
 *
 * The sidebar already hides links the user can't use, but a typed or bookmarked URL bypasses
 * the sidebar entirely — without this the page mounts, fires its API calls, collects a row of
 * 403s and renders as empty-and-broken rather than as restricted.
 *
 * `anyOf` covers the routes reachable by more than one permission, e.g. the campaign wizard
 * serves both create and edit.
 */
const Guarded: React.FC<{ permission?: string; anyOf?: string[]; children: React.ReactNode }> = ({
  permission,
  anyOf,
  children
}) => (
  <Can permission={permission} anyOf={anyOf} mode="page">
    {children}
  </Can>
)

const App: React.FC = () => {
  return (
    <BrowserRouter>
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
          {/* Auth screens render outside PageLayout — no sidebar or header behind a login
              form, and no chrome for a user who isn't signed in yet. */}
          <Route path="/login" element={<Login />} />
          <Route
            path="/change-password"
            element={
              <RequireAuth allowPasswordChange>
                <ChangePassword />
              </RequireAuth>
            }
          />

          <Route element={<AuthedShell />}>
            <Route path="/" element={<Guarded permission="Dashboard.View"><Dashboard /></Guarded>} />
            <Route path="/campaigns/campaign" element={<Guarded permission="Campaign.View"><CampaignsList /></Guarded>} />
            <Route path="/campaigns/campaign/create" element={<Guarded permission="Campaign.Create"><CampaignWizard /></Guarded>} />
            <Route path="/campaigns/campaign/edit/:id" element={<Guarded permission="Campaign.Edit"><CampaignWizard /></Guarded>} />
            <Route path="/campaigns/campaign/details/:id" element={<Guarded permission="Campaign.View"><CampaignDetails /></Guarded>} />

            {/* Completed high-fidelity pages */}
            <Route path="/reporting" element={<Guarded permission="Reporting.View"><Reporting /></Guarded>} />
            <Route path="/activity-logs" element={<Guarded permission="ActivityLog.View"><ActivityLogs /></Guarded>} />
            <Route path="/connections" element={<Guarded permission="ConnectAccount.View"><ConnectionsList /></Guarded>} />
            <Route path="/connections/new" element={<Guarded permission="ConnectAccount.Connect"><ConnectNewWabaPage /></Guarded>} />

            {/* The section is part of the path so a settings page is linkable and survives a
                refresh. The bare path renders the first section rather than redirecting, which
                keeps "/omniconnect-settings" a valid destination for the sidebar. */}
            <Route path="/omniconnect-settings" element={<Guarded permission="OmniSettings.View"><OmniConnectSettings /></Guarded>} />
            <Route path="/omniconnect-settings/:sectionKey" element={<Guarded permission="OmniSettings.View"><OmniConnectSettings /></Guarded>} />

            {/* Connection Access moved under Setup. The old paths redirect so existing
                bookmarks and any links already shared internally keep working. */}
            <Route path="/admin/permissions/user" element={<Navigate to="/setup/connection-access/user" replace />} />
            <Route path="/admin/permissions/department" element={<Navigate to="/setup/connection-access/department" replace />} />
            {/* Dual-purpose page: the WABA detail dashboard for a connected account, the setup
                wizard for a disconnected one. Guarded on View because reading it is what most
                visitors do — the endpoint behind it is already ConnectAccount.View, and the
                wizard steps and every mutating control inside carry their own stricter gate. */}
            <Route path="/connect-waba" element={<Guarded permission="ConnectAccount.View"><ConnectWABA /></Guarded>} />
            <Route path="/contacts" element={<Guarded permission="Contact.View"><ContactsList /></Guarded>} />
            <Route path="/contacts/contact" element={<Guarded permission="Contact.Create"><AddContact /></Guarded>} />
            <Route path="/contacts/contact/edit/:id" element={<Guarded permission="Contact.Edit"><AddContact /></Guarded>} />
            <Route path="/contacts/import" element={<Guarded permission="Contact.Import"><ImportContacts /></Guarded>} />
            <Route path="/templates" element={<Guarded permission="Template.View"><TemplatesList /></Guarded>} />
            <Route path="/bulk-campaigns" element={<Guarded permission="BulkCampaign.View"><BulkCampaign /></Guarded>} />

            <Route path="/message-bot" element={<Guarded permission="MessageBot.View"><MessageBotList /></Guarded>} />
            <Route path="/message-bot/bot" element={<Guarded permission="MessageBot.Create"><MessageBotWizard /></Guarded>} />
            <Route path="/message-bot/bot/:id" element={<Guarded permission="MessageBot.Edit"><MessageBotWizard /></Guarded>} />

            <Route path="/template-bot" element={<Guarded permission="TemplateBot.View"><TemplateBotList /></Guarded>} />
            <Route path="/template-bot/bot" element={<Guarded permission="TemplateBot.Create"><TemplateBotWizard /></Guarded>} />
            <Route path="/template-bot/bot/:id" element={<Guarded permission="TemplateBot.Edit"><TemplateBotWizard /></Guarded>} />
            <Route path="/bot-flow" element={<Guarded permission="BotFlow.View"><BotFlowList /></Guarded>} />
            {/* The designer both reads and saves a flow, so viewing it needs the edit grant. */}
            <Route path="/bot-flow/designer/:id" element={<Guarded permission="BotFlow.Edit"><BotFlowDesigner /></Guarded>} />
            <Route path="/chat" element={<Guarded permission="Chat.View"><Chat /></Guarded>} />
            {/* System Settings belongs to the host app, not OmniConnect. The route survives only
                to redirect bookmarks; the sidebar item is gone. */}
            <Route path="/system-settings" element={<Navigate to="/omniconnect-settings" replace />} />
            <Route path="/omniconnect-settings" element={<Placeholder />} />

            {/* Setup is the one nested subtree in this otherwise flat route table. It earns
                the nesting: a single <Outlet /> keeps the section rail mounted across
                navigations instead of remounting it on every click. */}
            <Route path="/setup" element={<SetupLayout />}>
              <Route index element={<Navigate to="users" replace />} />

              <Route path="users" element={<Guarded permission="User.View"><SetupUsersList /></Guarded>} />
              <Route path="users/new" element={<Guarded permission="User.Create"><SetupUserForm /></Guarded>} />
              <Route path="users/:id" element={<Guarded permission="User.Edit"><SetupUserForm /></Guarded>} />
              <Route path="roles" element={<Guarded permission="Role.View"><SetupRolesList /></Guarded>} />
              <Route path="roles/new" element={<Guarded permission="Role.Create"><SetupRoleForm /></Guarded>} />
              <Route path="roles/:id" element={<Guarded permission="Role.Edit"><SetupRoleForm /></Guarded>} />
              <Route path="status" element={<Guarded permission="Status.View"><SetupStatusList /></Guarded>} />
              <Route path="source" element={<Guarded permission="Source.View"><SetupSourceList /></Guarded>} />
              <Route path="type" element={<Guarded permission="ContactType.View"><SetupTypeList /></Guarded>} />
              <Route path="groups" element={<Guarded permission="ContactGroup.View"><SetupGroupsList /></Guarded>} />
              <Route path="ai-prompts" element={<Guarded permission="AiPrompt.View"><SetupAiPromptsList /></Guarded>} />
              <Route path="canned-replies" element={<Guarded permission="CannedReply.View"><SetupCannedRepliesList /></Guarded>} />
              <Route path="activity-log" element={<Guarded permission="ActivityLog.View"><SetupActivityLog /></Guarded>} />
              <Route path="languages" element={<Guarded permission="Language.View"><SetupLanguagesList /></Guarded>} />
              {/* The translate screen writes translations, so it needs the edit grant, not view. */}
              <Route path="languages/:id/translate" element={<Guarded permission="Language.Edit"><SetupTranslateScreen /></Guarded>} />
              <Route path="email-templates" element={<Guarded permission="EmailTemplate.View"><SetupEmailTemplatesList /></Guarded>} />
              <Route path="system-logs" element={<Guarded permission="SystemLog.View"><SetupSystemLogs /></Guarded>} />

              <Route path="connection-access/user" element={<Guarded permission="ConnectionAccess.View"><UserPermissionsList /></Guarded>} />
              <Route path="connection-access/department" element={<Guarded permission="ConnectionAccess.View"><DepartmentPermissionsList /></Guarded>} />
            </Route>
            
            {/* Fallback route — a real 404, distinct from Placeholder (which is
                reserved for pages that exist in the roadmap but aren't built
                yet, above). This is for URLs that don't correspond to
                anything at all. */}
            <Route path="*" element={<NotFound />} />
          </Route>
        </Routes>
      </Suspense>
    </BrowserRouter>
  )
}

export default App

