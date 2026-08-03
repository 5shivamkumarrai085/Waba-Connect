import React from 'react'
import { motion } from 'framer-motion'

import './DataTable.css'

interface TableHeader {
  key: string
  label: string
}

interface DataTableProps {
  headers: TableHeader[]
  rows: any[]
  renderCell?: (row: any, key: string) => React.ReactNode
  emptyMessage?: string
}

// Use inline transition per row instead of a variants function to avoid TS Variants type incompatibility
export const DataTable: React.FC<DataTableProps> = ({
  headers,
  rows,
  renderCell,
  emptyMessage = 'No data available'
}) => {
  return (
    <div className="data-table-wrapper">
      {rows.length === 0 ? (
        <div className="data-table-empty">
          <p>{emptyMessage}</p>
        </div>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              {headers.map((header) => (
                <th key={header.key}>{header.label}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <motion.tr
                key={row.id || index}
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: index * 0.04, duration: 0.18, ease: 'easeOut' }}
              >
                {headers.map((header) => (
                  <td key={header.key}>
                    {renderCell ? renderCell(row, header.key) : row[header.key]}
                  </td>
                ))}
              </motion.tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
