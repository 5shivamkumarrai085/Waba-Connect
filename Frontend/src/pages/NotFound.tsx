import React from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { motion } from 'framer-motion'
import { SearchX, ArrowLeft } from 'lucide-react'
import { pageTransitionProps } from '../utils/motion'
import './NotFound.css'

/**
 * The real 404 — distinct from Placeholder.tsx, which is reserved for pages
 * that genuinely exist in the product roadmap but aren't built yet
 * (/system-settings, /omniconnect-settings, /setup). This is for URLs that
 * don't correspond to anything: a typo, a stale link, or a route that was
 * removed. Only wired to App.tsx's catch-all `*` route.
 */
export const NotFound: React.FC = () => {
  const location = useLocation()
  const navigate = useNavigate()

  return (
    <motion.div {...pageTransitionProps} className="not-found-page">
      <div className="not-found-card">
        <div className="not-found-icon-wrapper">
          <SearchX size={36} />
        </div>

        <h1 className="not-found-code">404</h1>
        <h2 className="not-found-title">Page not found</h2>
        <p className="not-found-desc">
          <code className="not-found-path">{location.pathname}</code> doesn't match any page in
          OmniConnect. It may have been moved, renamed, or never existed.
        </p>

        <button
          type="button"
          className="btn-toolbar not-found-back-btn"
          onClick={() => navigate('/')}
        >
          <ArrowLeft size={16} />
          <span>Back to Dashboard</span>
        </button>
      </div>
    </motion.div>
  )
}

export default NotFound
