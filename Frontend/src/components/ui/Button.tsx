import React from 'react'
import { Loader2 } from 'lucide-react'
import './ui.css'

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type ButtonSize = 'sm' | 'md'

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  /** Shows a spinner, keeps the width, and blocks clicks while an action runs. */
  loading?: boolean
  icon?: React.ReactNode
}

/**
 * The one button. Variants carry meaning: primary for the main action of a view, secondary for
 * the rest, ghost for toolbars, danger for destructive actions. Defaults to type="button" so a
 * button inside a form never submits it by accident.
 */
export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'secondary', size = 'md', loading = false, icon, className, children, disabled, type = 'button', ...rest },
  ref
) {
  return (
    <button
      ref={ref}
      type={type}
      className={`ui-btn ui-btn--${variant} ui-btn--${size}${className ? ` ${className}` : ''}`}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {loading ? <Loader2 size={size === 'sm' ? 14 : 16} className="ui-spin" aria-hidden="true" /> : icon}
      {children}
    </button>
  )
})

interface IconButtonProps extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'aria-label'> {
  /** Required: an icon alone says nothing to a screen reader. Also used as the tooltip. */
  label: string
  icon: React.ReactNode
  variant?: 'ghost' | 'secondary'
  size?: ButtonSize
  /** Marks a toggle button's on state (e.g. a panel that is open). */
  pressed?: boolean
}

/** A square icon-only button with an accessible name and a native tooltip. */
export const IconButton = React.forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton(
  { label, icon, variant = 'ghost', size = 'md', pressed, className, type = 'button', title, ...rest },
  ref
) {
  return (
    <button
      ref={ref}
      type={type}
      aria-label={label}
      title={title ?? label}
      aria-pressed={pressed}
      className={`ui-icon-btn ui-icon-btn--${variant} ui-icon-btn--${size}${pressed ? ' is-pressed' : ''}${className ? ` ${className}` : ''}`}
      {...rest}
    >
      {icon}
    </button>
  )
})
