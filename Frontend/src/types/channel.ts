/**
 * The channel dimension, shared by every surface that is no longer WhatsApp-only:
 * the campaign wizard, connections, the inbox, the dashboard and reporting.
 *
 * Previously there was no channel concept in the frontend at all — everything was implicitly
 * WhatsApp. Defining it in one place keeps a channel's name, icon, colour and copy from being
 * re-decided slightly differently on each screen.
 */

/** Channels the backend can actually send on. */
export type MessageChannel = 'whatsapp' | 'email'

/**
 * Channels shown in the UI but not yet implemented.
 *
 * Separate from {@link MessageChannel} on purpose: the type system then refuses to let a
 * "coming soon" channel be passed where a sendable one is expected, so an enabled-looking
 * button cannot accidentally reach the send path.
 */
export type PlannedChannel = 'sms' | 'instagram' | 'facebook'

export type AnyChannel = MessageChannel | PlannedChannel

export interface ChannelDefinition {
  key: AnyChannel
  /** Shown in menus and column values. */
  label: string
  /** One line explaining what picking this channel means, for the selection cards. */
  description: string
  /** lucide-react icon name, resolved dynamically the same way MetricCard does it. */
  iconName: string
  /** CSS custom-property name holding this channel's accent colour. */
  colorVar: string
  /** False for planned channels, which render disabled with a "Coming Soon" marker. */
  available: boolean
}

/**
 * The single source of truth for channel presentation.
 *
 * Ordered, because this is what the wizard's channel cards and the inbox's channel menu both
 * iterate — the available channels first, then the planned ones.
 */
export const CHANNELS: ChannelDefinition[] = [
  {
    key: 'whatsapp',
    label: 'WhatsApp',
    description: 'Send message via WhatsApp Business API',
    iconName: 'MessageCircle',
    colorVar: '--channel-whatsapp',
    available: true,
  },
  {
    key: 'email',
    label: 'Email',
    description: 'Send message via Email (Amazon SES or SMTP)',
    iconName: 'Mail',
    colorVar: '--channel-email',
    available: true,
  },
  {
    key: 'sms',
    label: 'SMS',
    description: 'Send text messages to mobile numbers',
    iconName: 'MessageSquare',
    colorVar: '--channel-sms',
    available: false,
  },
  {
    key: 'instagram',
    label: 'Instagram',
    description: 'Reply to Instagram direct messages',
    iconName: 'Instagram',
    colorVar: '--channel-instagram',
    available: false,
  },
  {
    key: 'facebook',
    label: 'Facebook',
    description: 'Reply to Facebook Messenger conversations',
    iconName: 'Facebook',
    colorVar: '--channel-facebook',
    available: false,
  },
]

/** Only the channels that can actually be used. What the campaign wizard offers. */
export const AVAILABLE_CHANNELS = CHANNELS.filter((c) => c.available)

/** The ones rendered disabled, under a "Coming Soon" heading. */
export const PLANNED_CHANNELS = CHANNELS.filter((c) => !c.available)

export const getChannel = (key: string | null | undefined): ChannelDefinition | undefined =>
  CHANNELS.find((c) => c.key === normalizeChannel(key))

/**
 * Normalises whatever the API returned into the frontend's lowercase form.
 *
 * The backend serialises its enum as `"WhatsApp"` / `"Email"`, while the frontend keys are
 * lowercase. Doing the conversion in one function rather than at each call site is what stops a
 * stray `'Email' === 'email'` comparison from silently failing.
 */
export const normalizeChannel = (value: string | null | undefined): AnyChannel => {
  const normalized = (value ?? '').trim().toLowerCase()
  return CHANNELS.some((c) => c.key === normalized) ? (normalized as AnyChannel) : 'whatsapp'
}

/** The form the API expects: `"WhatsApp"` or `"Email"`. */
export const toApiChannel = (channel: MessageChannel): string =>
  channel === 'email' ? 'Email' : 'WhatsApp'

export const channelLabel = (value: string | null | undefined): string =>
  getChannel(value)?.label ?? 'Unknown'
