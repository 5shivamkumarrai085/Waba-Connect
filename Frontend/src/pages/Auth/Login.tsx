import React, { useState } from 'react'
import { useNavigate, useLocation, Navigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { Eye, EyeOff, LogIn, AlertCircle } from 'lucide-react'
import useAuthStore from '../../store/authStore'
import { getErrorMessage } from '../../utils/errorHelper'
import { fadeSlideUp, transitions } from '../../utils/motion'
import './auth.css'

interface LocationState {
  from?: string
}

export const Login: React.FC = () => {
  const navigate = useNavigate()
  const location = useLocation()

  const login = useAuthStore((state) => state.login)
  const isLoggingIn = useAuthStore((state) => state.isLoggingIn)
  const user = useAuthStore((state) => state.user)

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')

  // Already signed in — don't show a login form behind a valid session.
  if (user) {
    return <Navigate to={user.mustChangePassword ? '/change-password' : '/'} replace />
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')

    try {
      const signedInUser = await login({ email: email.trim(), password })

      if (signedInUser.mustChangePassword) {
        navigate('/change-password', { replace: true })
        return
      }

      // Return them to whatever they were trying to reach before the redirect.
      const from = (location.state as LocationState | null)?.from
      navigate(from && from !== '/login' ? from : '/', { replace: true })
    } catch (err) {
      setError(getErrorMessage(err, 'Unable to sign in. Please try again.'))
    }
  }

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
          <div className="auth-brand-mark">OC</div>
          <h1 className="auth-title">Sign in to OmniConnect</h1>
          <p className="auth-subtitle">Enter your credentials to continue.</p>
        </div>

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
            <label className="auth-label" htmlFor="login-email">Email</label>
            <input
              id="login-email"
              className="auth-input"
              type="email"
              autoComplete="username"
              placeholder="you@company.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={isLoggingIn}
              required
              autoFocus
            />
          </div>

          <div className="auth-field">
            <label className="auth-label" htmlFor="login-password">Password</label>
            <div className="auth-input-wrapper">
              <input
                id="login-password"
                className="auth-input auth-input-with-affix"
                type={showPassword ? 'text' : 'password'}
                autoComplete="current-password"
                placeholder="Enter your password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                disabled={isLoggingIn}
                required
              />
              <button
                type="button"
                className="auth-input-affix"
                onClick={() => setShowPassword((prev) => !prev)}
                aria-label={showPassword ? 'Hide password' : 'Show password'}
                tabIndex={-1}
              >
                {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
              </button>
            </div>
          </div>

          <button
            type="submit"
            className="auth-submit"
            disabled={isLoggingIn || !email.trim() || !password}
          >
            {isLoggingIn ? (
              <span>Signing in…</span>
            ) : (
              <>
                <LogIn size={16} />
                <span>Sign in</span>
              </>
            )}
          </button>
        </form>
      </motion.div>
    </div>
  )
}

export default Login
