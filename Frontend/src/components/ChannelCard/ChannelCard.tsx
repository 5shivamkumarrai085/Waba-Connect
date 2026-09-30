import React from 'react'
import { AlertCircle, Circle, Play } from 'lucide-react'
import { iconByName } from '../../utils/iconRegistry'
import type { ChannelDefinition } from '../../types/channel'
import './ChannelCard.css'

interface ChannelCardProps {
  channel: ChannelDefinition
  selected: boolean
  onSelect: (key: ChannelDefinition['key']) => void
  /**
   * Shown as a green "Already Configured" marker, or an amber prompt when the channel has no
   * usable connection yet.
   *
   * Passed in rather than derived here: whether a channel is configured depends on live
   * connection state, and a presentational card should not be fetching that.
   */
  configured?: boolean
  /** Overrides the default "not configured" hint, e.g. to say what is missing. */
  unconfiguredHint?: string
}

/**
 * One selectable channel in the campaign wizard's first step.
 *
 * A radio input rather than a styled div, so keyboard selection, arrow-key navigation within the
 * group and screen-reader announcement all come from the platform. The visual card is the
 * label — clicking anywhere on it selects, because a 300px card with a 16px hit target would be
 * needlessly awkward to use.
 */
export const ChannelCard: React.FC<ChannelCardProps> = ({
  channel,
  selected,
  onSelect,
  configured,
  unconfiguredHint
}) => {
  // Resolved by name, the same approach MetricCard uses, so a channel's icon is data rather than
  // another import to remember.
  const Icon = iconByName(channel.iconName, Circle)

  const disabled = !channel.available
  const inputId = `channel-option-${channel.key}`

  return (
    <label
      className={`channel-card ${selected ? 'selected' : ''} ${disabled ? 'disabled' : ''}`}
      htmlFor={inputId}
      data-channel={channel.key}
    >
      <input
        id={inputId}
        type="radio"
        name="campaign-channel"
        className="channel-card-radio"
        checked={selected}
        disabled={disabled}
        onChange={() => !disabled && onSelect(channel.key)}
      />

      <span className="channel-card-radio-dot" aria-hidden="true" />

      <span className="channel-card-icon">
        <Icon size={22} />
      </span>

      <span className="channel-card-body">
        <span className="channel-card-title">{channel.label}</span>
        <span className="channel-card-description">{channel.description}</span>

        {disabled ? (
          <span className="channel-card-tag coming-soon">Coming Soon</span>
        ) : configured ? (
          <span className="channel-card-tag configured">
            <Play size={11} />
            Already Configured
          </span>
        ) : (
          <span className="channel-card-tag needs-setup">
            <AlertCircle size={11} />
            {unconfiguredHint ?? 'Needs setup'}
          </span>
        )}
      </span>
    </label>
  )
}

export default ChannelCard
