import React, { useState } from 'react'
import { X, Shield, Check } from 'lucide-react'
import type { Connection } from '../../types/connection'

interface AssignPermissionModalProps {
  type: 'user' | 'department'
  connections: Connection[]
  onClose: () => void
  onAssignUser?: (data: { userId: string; userName: string; userEmail: string; departmentName: string; connectionIds: number[] }) => Promise<void>
  onAssignDepartment?: (data: { departmentId: string; departmentName: string; description: string; memberCount: number; connectionIds: number[] }) => Promise<void>
}

export const AssignPermissionModal: React.FC<AssignPermissionModalProps> = ({
  type,
  connections,
  onClose,
  onAssignUser,
  onAssignDepartment
}) => {
  const [userName, setUserName] = useState('')
  const [userEmail, setUserEmail] = useState('')
  const [departmentName, setDepartmentName] = useState('Sales')
  const [description, setDescription] = useState('')
  const [memberCount, setMemberCount] = useState(5)
  const [selectedConnIds, setSelectedConnIds] = useState<number[]>([])
  const [isSubmitting, setIsSubmitting] = useState(false)

  const toggleConnection = (id: number) => {
    if (selectedConnIds.includes(id)) {
      setSelectedConnIds(selectedConnIds.filter((x) => x !== id))
    } else {
      setSelectedConnIds([...selectedConnIds, id])
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (selectedConnIds.length === 0) return
    setIsSubmitting(true)
    try {
      if (type === 'user' && onAssignUser) {
        await onAssignUser({
          userId: `usr-${Date.now()}`,
          userName: userName.trim() || 'New User',
          userEmail: userEmail.trim() || 'user@example.com',
          departmentName,
          connectionIds: selectedConnIds
        })
      } else if (type === 'department' && onAssignDepartment) {
        await onAssignDepartment({
          departmentId: `dept-${Date.now()}`,
          departmentName,
          description: description.trim() || `${departmentName} Department Connections`,
          memberCount,
          connectionIds: selectedConnIds
        })
      }
      onClose()
    } catch {
      // Handled silently
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm p-4 animate-in fade-in duration-200">
      <div className="bg-white rounded-xl shadow-2xl max-w-lg w-full overflow-hidden border border-slate-200">
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100 bg-slate-50/50">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-lg bg-blue-50 border border-blue-100 flex items-center justify-center text-blue-600">
              <Shield className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-lg font-bold text-slate-900">
                {type === 'user' ? 'Assign User Permission' : 'Assign Department Permission'}
              </h3>
              <p className="text-xs text-slate-500">
                {type === 'user' ? 'Assign WABA connections to an individual user.' : 'Assign WABA connections to an entire department.'}
              </p>
            </div>
          </div>
          <button onClick={onClose} className="p-2 text-slate-400 hover:text-slate-600 rounded-lg">
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {type === 'user' ? (
            <>
              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">User Full Name</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Aman Kumar"
                  value={userName}
                  onChange={(e) => setUserName(e.target.value)}
                  className="w-full px-3.5 py-2 bg-slate-50 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">User Email Address</label>
                <input
                  type="email"
                  required
                  placeholder="e.g. aman.kumar@example.com"
                  value={userEmail}
                  onChange={(e) => setUserEmail(e.target.value)}
                  className="w-full px-3.5 py-2 bg-slate-50 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">Department</label>
                <select
                  value={departmentName}
                  onChange={(e) => setDepartmentName(e.target.value)}
                  className="w-full px-3.5 py-2 bg-slate-50 border border-slate-200 rounded-lg text-sm font-medium text-slate-800"
                >
                  <option value="Sales">Sales</option>
                  <option value="Support">Support</option>
                  <option value="Marketing">Marketing</option>
                  <option value="Operations">Operations</option>
                </select>
              </div>
            </>
          ) : (
            <>
              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">Department Name</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Sales"
                  value={departmentName}
                  onChange={(e) => setDepartmentName(e.target.value)}
                  className="w-full px-3.5 py-2 bg-slate-50 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">Department Description</label>
                <input
                  type="text"
                  placeholder="e.g. Handles all sales related queries and leads"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  className="w-full px-3.5 py-2 bg-slate-50 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 uppercase mb-1">Member Count</label>
                <input
                  type="number"
                  min={1}
                  value={memberCount}
                  onChange={(e) => setMemberCount(Number(e.target.value))}
                  className="w-full px-3.5 py-2 bg-slate-50 border border-slate-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500/20"
                />
              </div>
            </>
          )}

          <div>
            <label className="block text-xs font-semibold text-slate-700 uppercase mb-2">Select Allowed Connections</label>
            <div className="space-y-2 max-h-48 overflow-y-auto pr-1">
              {connections.length === 0 ? (
                <p className="text-xs text-slate-400 italic">No connections available.</p>
              ) : (
                connections.map((conn) => {
                  const isSelected = selectedConnIds.includes(conn.id)
                  return (
                    <div
                      key={conn.id}
                      onClick={() => toggleConnection(conn.id)}
                      className={`flex items-center justify-between p-3 rounded-lg border cursor-pointer transition-colors ${
                        isSelected
                          ? 'bg-blue-50/70 border-blue-200 text-blue-900'
                          : 'bg-slate-50/50 border-slate-200 text-slate-700 hover:bg-slate-50'
                      }`}
                    >
                      <div className="flex items-center gap-2 text-xs font-medium">
                        <span className={`w-2 h-2 rounded-full ${conn.isConnected ? 'bg-emerald-500' : 'bg-amber-500'}`} />
                        <span>{conn.name}</span>
                        <span className="text-slate-400">({conn.phoneNumber || 'Setup pending'})</span>
                      </div>
                      {isSelected && <Check className="w-4 h-4 text-blue-600" />}
                    </div>
                  )
                })
              )}
            </div>
          </div>

          <div className="flex items-center justify-end gap-3 pt-4 border-t border-slate-100">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 rounded-lg border border-slate-200"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSubmitting || selectedConnIds.length === 0}
              className="px-5 py-2 text-sm font-medium bg-blue-600 hover:bg-blue-700 text-white rounded-lg shadow-sm disabled:opacity-50"
            >
              {isSubmitting ? 'Assigning...' : 'Assign Permission'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
