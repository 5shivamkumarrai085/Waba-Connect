import React, { useMemo } from 'react'

interface ColorPickerProps {
  /** Hex value including the leading #, e.g. "#22C55E". */
  value: string
  onChange: (hex: string) => void
  disabled?: boolean
  id?: string
}

const PRESETS = [
  '#22C55E', '#3B82F6', '#EAB308', '#A855F7', '#EF4444',
  '#10B981', '#F97316', '#06B6D4', '#EC4899', '#6B7280'
]

const clamp = (n: number) => Math.max(0, Math.min(255, n))

const hexToRgb = (hex: string): { r: number; g: number; b: number } => {
  const normalized = hex.replace('#', '')
  // Expand the 3-digit shorthand so "#0f0" round-trips instead of parsing as garbage.
  const full = normalized.length === 3
    ? normalized.split('').map((c) => c + c).join('')
    : normalized.padEnd(6, '0').slice(0, 6)

  return {
    r: parseInt(full.slice(0, 2), 16) || 0,
    g: parseInt(full.slice(2, 4), 16) || 0,
    b: parseInt(full.slice(4, 6), 16) || 0
  }
}

const rgbToHex = (r: number, g: number, b: number) =>
  '#' + [r, g, b].map((n) => clamp(n).toString(16).padStart(2, '0')).join('').toUpperCase()

/**
 * Hex + RGB colour input backed by the native colour well.
 *
 * Deliberately no third-party picker: the native `<input type="color">` gives a real
 * eyedropper and OS palette for free, and the hex/RGB fields cover the case where someone is
 * pasting a brand colour rather than choosing one.
 */
export const ColorPicker: React.FC<ColorPickerProps> = ({ value, onChange, disabled, id }) => {
  const safeValue = useMemo(() => {
    const candidate = (value || '').trim()
    return /^#[0-9a-fA-F]{3,6}$/.test(candidate) ? candidate : '#6B7280'
  }, [value])

  const rgb = useMemo(() => hexToRgb(safeValue), [safeValue])

  const updateChannel = (channel: 'r' | 'g' | 'b', raw: string) => {
    const parsed = clamp(parseInt(raw, 10) || 0)
    const next = { ...rgb, [channel]: parsed }
    onChange(rgbToHex(next.r, next.g, next.b))
  }

  return (
    <div className="color-picker">
      <div className="color-picker-row">
        <input
          id={id}
          type="color"
          className="color-picker-well"
          value={safeValue}
          onChange={(e) => onChange(e.target.value.toUpperCase())}
          disabled={disabled}
          aria-label="Pick a colour"
        />

        <input
          type="text"
          className="setup-input color-picker-hex"
          value={safeValue}
          onChange={(e) => {
            const next = e.target.value.startsWith('#') ? e.target.value : `#${e.target.value}`
            onChange(next.toUpperCase())
          }}
          placeholder="#22C55E"
          disabled={disabled}
          aria-label="Hex colour"
        />
      </div>

      <div className="color-picker-rgb">
        {(['r', 'g', 'b'] as const).map((channel) => (
          <label className="color-picker-channel" key={channel}>
            <input
              type="number"
              min={0}
              max={255}
              className="setup-input"
              value={rgb[channel]}
              onChange={(e) => updateChannel(channel, e.target.value)}
              disabled={disabled}
              aria-label={channel.toUpperCase()}
            />
            <span>{channel.toUpperCase()}</span>
          </label>
        ))}
      </div>

      <div className="color-picker-presets">
        {PRESETS.map((preset) => (
          <button
            key={preset}
            type="button"
            className={`color-picker-preset ${safeValue.toUpperCase() === preset ? 'is-selected' : ''}`}
            style={{ backgroundColor: preset }}
            onClick={() => onChange(preset)}
            disabled={disabled}
            aria-label={`Use ${preset}`}
            title={preset}
          />
        ))}
      </div>
    </div>
  )
}

export default ColorPicker
