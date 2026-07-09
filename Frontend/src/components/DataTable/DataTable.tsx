import React from 'react'
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
              <tr key={row.id || index}>
                {headers.map((header) => (
                  <td key={header.key}>
                    {renderCell ? renderCell(row, header.key) : row[header.key]}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
