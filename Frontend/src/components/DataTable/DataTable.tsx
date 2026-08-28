import React from 'react'
import { motion } from 'framer-motion'

import './DataTable.css'

interface TableHeader {
  key: string
  label: string
  /**
   * Applied to this column's header cell and to every body cell in it.
   *
   * Exists so a page can target a column by what it is rather than by where it sits. The audit
   * table used to pin its first column with `:first-child`; moving the actions column to the
   * front would have silently transferred that pin onto the Details button.
   */
  className?: string
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
                <th key={header.key} className={header.className}>{header.label}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <motion.tr
                key={row.id || index}
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                // The stagger is capped rather than open-ended. At a flat index * 0.04s the
                // fiftieth row arrives two seconds after the first, and a table that fills in
                // over two seconds reads as broken rather than as animated. Capped, the effect
                // still cascades across the rows anyone actually sees on screen.
                transition={{ delay: Math.min(index, 12) * 0.03, duration: 0.18, ease: 'easeOut' }}
              >
                {headers.map((header) => (
                  <td key={header.key} className={header.className}>
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
