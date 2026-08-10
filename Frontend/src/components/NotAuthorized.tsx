import React from 'react'
import { useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { ShieldOff, ArrowLeft } from 'lucide-react'
import { pageTransitionProps } from '../utils/motion'

interface NotAuthorizedProps {
  /** Surfaced so an admin reading over the user's shoulder knows exactly what to grant. */
  requiredPermission?: string
}

/**
 * Shown when a signed-in user reaches a page their role doesn't cover. Distinct from
 * NotFound (the route doesn't exist) and from the login redirect (no session at all) —
 * here the page is real and they simply aren't allowed in.
 */
export const NotAuthorized: React.FC<NotAuthorizedProps> = ({ requiredPermission }) => {
  const navigate = useNavigate()

  return (
    <motion.div {...pageTransitionProps} className="not-found-page">
      <div className="not-found-card">
        <div className="not-found-icon-wrapper">
          <ShieldOff size={36} />
        </div>

        <h1 className="not-found-title">Access restricted</h1>
        <p className="not-found-desc">
          Your account doesn&apos;t have permission to view this page.
          {requiredPermission && (
            <>
              {' '}It requires <code className="not-found-path">{requiredPermission}</code>.
            </>
          )}
          {' '}Ask an administrator if you need access.
        </p>

        <button type="button" className="btn-toolbar not-found-back-btn" onClick={() => navigate('/')}>
          <ArrowLeft size={16} />
          <span>Back to Dashboard</span>
        </button>
      </div>
    </motion.div>
  )
}

export default NotAuthorized
