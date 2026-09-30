import {
  AlertCircle,
  BarChart3,
  Bell,
  Bot,
  CheckCircle2,
  Circle,
  Clock,
  Cpu,
  Download,
  FileBarChart2,
  FileText,
  HelpCircle,
  History,
  Languages,
  Layers,
  Mail,
  Megaphone,
  MessageCircle,
  MessageSquare,
  MessageSquareReply,
  Play,
  RefreshCw,
  SearchX,
  Settings,
  Settings2,
  ShieldAlert,
  ShieldCheck,
  SlidersHorizontal,
  Sparkles,
  Tags,
  Trash2,
  Users,
  Webhook,
  type LucideIcon,
} from 'lucide-react'

type IconComponent = LucideIcon

/**
 * The icons that can be referred to by name — by the server (report cards, settings sections) or
 * by data (channels, empty states).
 *
 * An explicit list rather than `import * as Icons from 'lucide-react'`: the wildcard import
 * defeats tree-shaking and shipped every icon in the library (~1.2 MB) to every user. A name that
 * is not listed here falls back to a neutral glyph; add it here when a new one is introduced.
 */
const REGISTRY: Record<string, IconComponent> = {
  AlertCircle,
  BarChart3,
  Bell,
  Bot,
  CheckCircle2,
  Circle,
  Clock,
  Cpu,
  Download,
  FileBarChart2,
  FileText,
  HelpCircle,
  History,
  Languages,
  Layers,
  Mail,
  Megaphone,
  MessageCircle,
  MessageSquare,
  MessageSquareReply,
  Play,
  RefreshCw,
  SearchX,
  Settings,
  Settings2,
  ShieldAlert,
  ShieldCheck,
  SlidersHorizontal,
  Sparkles,
  Tags,
  Trash2,
  Users,
  Webhook,
}

/** The icon registered under `name`, or `fallback` (a help glyph by default). */
export const iconByName = (name: string | undefined | null, fallback: IconComponent = HelpCircle): IconComponent =>
  (name && REGISTRY[name]) || fallback
