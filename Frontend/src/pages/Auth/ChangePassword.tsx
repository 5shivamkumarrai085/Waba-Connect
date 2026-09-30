import React, { useState } from 'react'
import { useNavigate, Navigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Eye, EyeOff, KeyRound, AlertCircle, ShieldAlert } from 'lucide-react'
import useAuthStore from '../../store/authStore'
import { authService } from '../../services/auth/authService'
import { getErrorMessage } from '../../utils/errorHelper'
import { fadeSlideUp, transitions } from '../../utils/motion'
import './auth.css'

/**
 * Serves two situations: the forced reset after signing in with a seeded or admin-reset
 * password, and a voluntary change from the account menu. The forced variant explains itself
 * and hides the escape hatch; the voluntary one offers Cancel.
 */
export const ChangePassword: React.FC = () => {
  const navigate = useNavigate()
  const user = useAuthStore((state) => state.user)
  const applySession = useAuthStore((state) => state.applySession)

  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showPasswords, setShowPasswords] = useState(false)
  const [error, setError] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)

  if (!user) {
    return <Navigate to="/login" replace />
  }

  const isForced = user.mustChangePassword

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')

    if (newPassword !== confirmPassword) {
      setError('The new passwords do not match.')
      return
    }

    setIsSubmitting(true)
    try {
      // The server ends every other session on a password change and returns fresh tokens for
      // this one — tokens whose claims no longer demand a password change.
      const session = await authService.changePassword({ currentPassword, newPassword, confirmPassword })
      applySession(session)
      toast.success('Password changed successfully.')
      navigate('/', { replace: true })
    } catch (err) {
      setError(getErrorMessage(err, 'Unable to change your password. Please try again.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const toggleLabel = showPasswords ? 'Hide passwords' : 'Show passwords'

  return (
    <div className="auth-page">
      <motion.div
        className="auth-card"
        variants={fadeSlideUp}
        initial="hidden"
        animate="visible"
        transition={transitions.smooth}
      >
        <div className="auth-brand">
          <div className="auth-brand-mark auth-brand-mark-warning">
            <KeyRound size={22} />
          </div>
          <h1 className="auth-title">{isForced ? 'Choose a new password' : 'Change your password'}</h1>
          <p className="auth-subtitle">
            {isForced
              ? 'Your account is still using its initial password. Set a new one to continue.'
              : `Update the password for ${user.email}.`}
          </p>
        </div>

        {isForced && (
          <div className="auth-notice">
            <ShieldAlert size={16} />
            <span>You can&apos;t access the rest of the app until this is done.</span>
          </div>
        )}

        <form className="auth-form" onSubmit={handleSubmit} noValidate>
          {error && (
            <motion.div
              className="auth-error"
              role="alert"
              initial={{ opacity: 0, y: -4 }}
              animate={{ opacity: 1, y: 0 }}
              transition={transitions.snappy}
            >
              <AlertCircle size={16} />
              <span>{error}</span>
            </motion.div>
          )}

          <div className="auth-field">
            <label className="auth-label" htmlFor="current-password">Current password</label>
            <input
              id="current-password"
              className="auth-input"
              type={showPasswords ? 'text' : 'password'}
              autoComplete="current-password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              disabled={isSubmitting}
              required
              autoFocus
            />
          </div>

          <div className="auth-field">
            <label className="auth-label" htmlFor="new-password">New password</label>
            <div className="auth-input-wrapper">
              <input
                id="new-password"
                className="auth-input auth-input-with-affix"
                type={showPasswords ? 'text' : 'password'}
                autoComplete="new-password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                disabled={isSubmitting}
                required
              />
              <button
                type="button"
                className="auth-input-affix"
                onClick={() => setShowPasswords((prev) => !prev)}
                aria-label={toggleLabel}
                tabIndex={-1}
              >
                {showPasswords ? <EyeOff size={16} /> : <Eye size={16} />}
              </button>
            </div>
            <p className="auth-hint">At least 8 characters, including a letter and a number.</p>
          </div>

          <div className="auth-field">
            <label className="auth-label" htmlFor="confirm-password">Confirm new password</label>
            <input
              id="confirm-password"
              className="auth-input"
              type={showPasswords ? 'text' : 'password'}
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              disabled={isSubmitting}
              required
            />
          </div>

          <button
            type="submit"
            className="auth-submit"
            disabled={isSubmitting || !currentPassword || !newPassword || !confirmPassword}
          >
            {isSubmitting ? 'Saving…' : 'Update password'}
          </button>

          {!isForced && (
            <button
              type="button"
              className="auth-secondary-action"
              onClick={() => navigate(-1)}
              disabled={isSubmitting}
            >
              Cancel
            </button>
          )}
        </form>
      </motion.div>
    </div>
  )
}

export default ChangePassword
