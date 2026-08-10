import React from 'react'
import { Outlet } from 'react-router-dom'
import Can from '../../components/Can/Can'
import SetupRail from './SetupRail'
import './setup.css'

/**
 * Shell for every Setup section.
 *
 * Sits inside PageLayout, so the main sidebar and header stay put, and owns a second rail for
 * Setup's own sections. Because the rail lives here rather than in each page, a single
 * <Outlet /> keeps it mounted across Setup navigations — switching sections swaps only the
 * content, with no remount flash or re-run entrance animation on the rail.
 *
 * Guarding once here covers every nested route; individual pages add their own finer-grained
 * checks for create/edit/delete actions.
 */
export const SetupLayout: React.FC = () => (
  <Can permission="Setup.View" mode="page">
    <div className="setup-shell">
      <SetupRail />
      <div className="setup-content">
        <Outlet />
      </div>
    </div>
  </Can>
)

export default SetupLayout
