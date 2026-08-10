import React from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { X } from 'lucide-react'
import usePermission from '../../hooks/usePermission'
import { setupNavSections } from './setupNav'

/**
 * Setup's own navigation rail, sitting beside the main sidebar.
 *
 * Below 1200px it becomes a horizontally scrollable chip row — two vertical rails side by side
 * leaves no usable content width on a laptop, let alone a tablet.
 */
export const SetupRail: React.FC = () => {
  const navigate = useNavigate()
  const { has } = usePermission()

  // Drop whole sections the user can't see anything in, so a restricted account doesn't get a
  // heading with nothing under it.
  const visibleSections = setupNavSections
    .map((section) => ({ ...section, items: section.items.filter((item) => has(item.permission)) }))
    .filter((section) => section.items.length > 0)

  return (
    <aside className="setup-rail" aria-label="Setup sections">
      <div className="setup-rail-header">
        <span className="setup-rail-title">Setup</span>
        <button
          type="button"
          className="setup-rail-close"
          onClick={() => navigate('/')}
          aria-label="Leave setup"
          title="Leave setup"
        >
          <X size={16} />
        </button>
      </div>

      <nav className="setup-rail-nav">
        {visibleSections.map((section, index) => (
          <div className="setup-rail-section" key={section.title ?? `section-${index}`}>
            {section.title && <div className="setup-rail-section-title">{section.title}</div>}

            {section.items.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className={({ isActive }) => `setup-rail-item ${isActive ? 'active' : ''}`}
              >
                <item.icon className="setup-rail-icon" size={16} />
                <span className="setup-rail-label">{item.label}</span>
              </NavLink>
            ))}
          </div>
        ))}
      </nav>
    </aside>
  )
}

export default SetupRail
