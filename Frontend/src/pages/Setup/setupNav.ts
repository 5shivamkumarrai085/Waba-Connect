import {
  Users,
  ShieldCheck,
  SlidersHorizontal,
  Tags,
  Layers,
  Sparkles,
  MessageSquareReply,
  Languages,
  Mail,
  FileBarChart2,
  Shield,
  Building2,
  Webhook,
  type LucideIcon
} from 'lucide-react'

export interface SetupNavItem {
  label: string
  path: string
  icon: LucideIcon
  /** Permission key gating visibility. Items the user can't view are dropped from the rail. */
  permission: string
  /** True once the section is actually implemented; others render the pending state. */
  built: boolean
}

export interface SetupNavSection {
  /** Omitted for the first group so the rail opens with content rather than a label. */
  title?: string
  items: SetupNavItem[]
}

/**
 * Setup navigation.
 *
 * The first group mirrors the reference UI's order exactly. Connection Access is separated
 * because those two pages were relocated here from the old Admin area and govern a different
 * axis — which WABA connection a user may see, rather than which features they may use.
 */
export const setupNavSections: SetupNavSection[] = [
  {
    items: [
      { label: 'User', path: '/setup/users', icon: Users, permission: 'User.View', built: true },
      { label: 'Role', path: '/setup/roles', icon: ShieldCheck, permission: 'Role.View', built: true },
      { label: 'Status', path: '/setup/status', icon: SlidersHorizontal, permission: 'Status.View', built: true },
      { label: 'Type', path: '/setup/type', icon: Tags, permission: 'ContactType.View', built: true },
      { label: 'Groups', path: '/setup/groups', icon: Users, permission: 'ContactGroup.View', built: true },
      { label: 'Source', path: '/setup/source', icon: Layers, permission: 'Source.View', built: true },
      { label: 'AI Prompts', path: '/setup/ai-prompts', icon: Sparkles, permission: 'AiPrompt.View', built: true },
      { label: 'Canned Reply', path: '/setup/canned-replies', icon: MessageSquareReply, permission: 'CannedReply.View', built: true },
      { label: 'Languages', path: '/setup/languages', icon: Languages, permission: 'Language.View', built: true },
      { label: 'Email Templates', path: '/setup/email-templates', icon: Mail, permission: 'EmailTemplate.View', built: true },
      { label: 'System Logs', path: '/setup/system-logs', icon: FileBarChart2, permission: 'SystemLog.View', built: true },
      { label: 'Webhooks', path: '/setup/webhooks', icon: Webhook, permission: 'Webhook.View', built: true },
    ]
  },
  {
    title: 'Connection Access',
    items: [
      { label: 'User Access', path: '/setup/connection-access/user', icon: Shield, permission: 'ConnectionAccess.View', built: true },
      { label: 'Department Access', path: '/setup/connection-access/department', icon: Building2, permission: 'ConnectionAccess.View', built: true },
    ]
  }
]
