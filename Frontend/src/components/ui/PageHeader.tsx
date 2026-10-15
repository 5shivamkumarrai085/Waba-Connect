import React, { useEffect } from 'react'
import { Link } from 'react-router-dom'
import './ui.css'

/** The product name shown after every page title in the browser tab. */
const APP_NAME = import.meta.env.VITE_APP_NAME || 'OmniConnect'

export interface Crumb {
  label: string
  to?: string
}

interface PageHeaderProps {
  title: string
  description?: React.ReactNode
  /** Buttons on the right: the page's main action last. */
  actions?: React.ReactNode
  breadcrumb?: Crumb[]
}

/**
 * Every page's heading: one h1, an optional description and breadcrumb, actions on the right.
 * Also names the browser tab after the page, so twenty open tabs are not twenty "OmniConnect"s.
 */
export const PageHeader: React.FC<PageHeaderProps> = ({ title, description, actions, breadcrumb }) => {
  useDocumentTitle(title)

  return (
    <header className="ui-page-header">
      <div className="ui-page-header-text">
        {breadcrumb && breadcrumb.length > 0 && (
          <nav aria-label="Breadcrumb" className="ui-breadcrumb">
            <ol>
              {breadcrumb.map((crumb, i) => (
                <li key={`${crumb.label}-${i}`}>
                  {crumb.to ? <Link to={crumb.to}>{crumb.label}</Link> : <span aria-current="page">{crumb.label}</span>}
                </li>
              ))}
            </ol>
          </nav>
        )}
        <h1 className="ui-page-title">{title}</h1>
        {description && <p className="ui-page-description">{description}</p>}
      </div>
      {actions && <div className="ui-page-actions">{actions}</div>}
    </header>
  )
}

/** Sets the browser tab title while the page is shown ("Inbox · OmniConnect"). */
export function useDocumentTitle(title: string | undefined): void {
  useEffect(() => {
    if (!title) return
    const previous = document.title
    document.title = `${title} · ${APP_NAME}`
    return () => {
      document.title = previous
    }
  }, [title])
}
