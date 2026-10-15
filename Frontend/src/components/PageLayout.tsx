import React from 'react'
import { motion } from 'framer-motion'
import { Sidebar } from './Sidebar'
import { Header } from './Header'
import { fadeSlideUp, transitions } from '../utils/motion'
import { useLayoutStore } from '../store/layoutStore'

interface PageLayoutProps {
  children: React.ReactNode
}

/**
 * The signed-in shell: sidebar, top header, page container. A page that needs the whole space
 * beside the sidebar (the chat inbox) switches on full-bleed with `useFullBleed()`: the header and
 * page padding go, the content gets exactly the viewport's height, the sidebar stays as it is.
 */
export const PageLayout: React.FC<PageLayoutProps> = ({ children }) => {
  const fullBleed = useLayoutStore((s) => s.fullBleed)

  return (
    <div className={`app-container${fullBleed ? ' app-container--full-bleed' : ''}`}>
      <Sidebar />
      <div className="main-content">
        {!fullBleed && <Header />}
        <motion.main
          className={`page-container${fullBleed ? ' page-container--full-bleed' : ''}`}
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
