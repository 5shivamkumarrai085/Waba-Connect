import React from 'react'
import { motion } from 'framer-motion'
import { Sidebar } from './Sidebar'
import { Header } from './Header'
import { fadeSlideUp, transitions } from '../utils/motion'

interface PageLayoutProps {
  children: React.ReactNode
}

export const PageLayout: React.FC<PageLayoutProps> = ({ children }) => {
  return (
    <div className="app-container">
      <Sidebar />
      <div className="main-content">
        <Header />
        <motion.main 
          className="page-container"
          variants={fadeSlideUp}
          initial="hidden"
          animate="visible"
          transition={transitions.smooth}
        >
          {children}
        </motion.main>
      </div>
    </div>
  )
}
