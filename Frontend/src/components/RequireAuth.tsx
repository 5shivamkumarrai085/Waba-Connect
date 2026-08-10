import React, { useEffect } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import useAuthStore from '../store/authStore'

interface RequireAuthProps {
  children: React.ReactNode
  /** Set on the change-password route itself, so the forced-reset redirect can't loop. */
  allowPasswordChange?: boolean
}

/**
 * Gates the authenticated shell.
 *
 * Waits for the startup token check to finish before deciding anything — otherwise a page
 * refresh bounces a signed-in user to the login screen for the split second before /auth/me
 * comes back.
 */
export const RequireAuth: React.FC<RequireAuthProps> = ({ children, allowPasswordChange }) => {
  const location = useLocation()
  const user = useAuthStore((state) => state.user)
  const isInitializing = useAuthStore((state) => state.isInitializing)
  const initialize = useAuthStore((state) => state.initialize)

  useEffect(() => {
    if (isInitializing) {
      void initialize()
    }
    // Runs once: initialize() sets isInitializing false, and the guard is intentionally not
    // re-armed on later renders.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (isInitializing) {
    return <div className="auth-boot">Restoring your session…</div>
  }

  if (!user) {
    // Remember where they were headed so login can send them back there.
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }

  if (user.mustChangePassword && !allowPasswordChange) {
    return <Navigate to="/change-password" replace />
  }

  return <>{children}</>
}

export default RequireAuth
