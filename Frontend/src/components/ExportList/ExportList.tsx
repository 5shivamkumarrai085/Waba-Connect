import React from 'react'
import { motion } from 'framer-motion'
import * as Icons from 'lucide-react'
import toast from 'react-hot-toast'
import type { ExportItemModel } from '../../types/reporting'
import './ExportList.css'

interface ExportListProps {
  items: ExportItemModel[]
  onActionClick?: (item: ExportItemModel) => void
}

const itemVariants = {
  hidden: { opacity: 0, x: -8 },
  visible: (i: number) => ({
    opacity: 1,
    x: 0,
    transition: { delay: i * 0.05, duration: 0.18 }
  })
}

export const ExportList: React.FC<ExportListProps> = ({
  items,
  onActionClick
}) => {
  return (
    <div className="export-list">
      {items.map((item, index) => {
        const ItemIcon = (Icons as any)[item.iconName] || Icons.FileText
        const ActionIcon = item.actionType === 'download' ? Icons.Download : Icons.ExternalLink

        const handleAction = () => {
          if (onActionClick) {
            onActionClick(item)
          } else {
            toast.success(`Initiated export action: ${item.actionType} for "${item.title}"`)
          }
        }

        return (
          <motion.div
            key={item.id}
            className="export-item"
            custom={index}
            variants={itemVariants}
            initial="hidden"
            animate="visible"
            whileHover={{ x: 4 }}
          >
            <div className="export-item-left">
              <div className="export-item-icon-box">
                <ItemIcon size={16} />
              </div>
              <div className="export-item-info">
                <h4 className="export-item-title">{item.title}</h4>
                <p className="export-item-desc">{item.description}</p>
              </div>
            </div>
            <button 
              className="export-item-action-btn"
              onClick={handleAction}
              aria-label={item.actionType === 'download' ? `Download ${item.title}` : `Open ${item.title}`}
            >
              <ActionIcon size={16} />
            </button>
          </motion.div>
        )
      })}
    </div>
  )
}
