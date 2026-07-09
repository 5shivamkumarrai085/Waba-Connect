import React from 'react'
import { Sidebar } from './Sidebar'
import { Header } from './Header'

interface PageLayoutProps {
  children: React.ReactNode
}

export const PageLayout: React.FC<PageLayoutProps> = ({ children }) => {
  return (
    <div className="app-container">
      <Sidebar />
      <div className="main-content">
        <Header />
        <main className="page-container">
          {children}
        </main>
      </div>
    </div>
  )
}
