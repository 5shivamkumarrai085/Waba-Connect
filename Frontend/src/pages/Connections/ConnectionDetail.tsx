import React from 'react'
import { X, CheckCircle, Smartphone, Key, Globe, Shield } from 'lucide-react'
import type { Connection } from '../../types/connection'

interface ConnectionDetailProps {
  connection: Connection
  onClose: () => void
}

export const ConnectionDetail: React.FC<ConnectionDetailProps> = ({ connection, onClose }) => {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm p-4 animate-in fade-in duration-200">
      <div className="bg-white rounded-xl shadow-2xl max-w-2xl w-full overflow-hidden border border-slate-200">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100 bg-slate-50/50">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-lg bg-indigo-50 border border-indigo-100 flex items-center justify-center text-indigo-600 font-semibold">
              {connection.id}
            </div>
            <div>
              <h3 className="text-lg font-bold text-slate-900">{connection.name}</h3>
              <p className="text-xs text-slate-500">{connection.description || 'WhatsApp Business Connection'}</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-2 text-slate-400 hover:text-slate-600 hover:bg-slate-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 space-y-6 max-h-[80vh] overflow-y-auto">
          {/* Status Badge */}
          <div className="flex items-center justify-between p-4 rounded-lg bg-slate-50 border border-slate-100">
            <span className="text-sm font-medium text-slate-700">Connection Status</span>
            <span
              className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold ${
                connection.isConnected
                  ? 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                  : 'bg-amber-50 text-amber-700 border border-amber-200'
              }`}
            >
              <span className={`w-2 h-2 rounded-full ${connection.isConnected ? 'bg-emerald-500' : 'bg-amber-500'}`} />
              {connection.isConnected ? 'Connected & Active' : 'Disconnected'}
            </span>
          </div>

          {/* Details Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div className="p-4 rounded-lg border border-slate-200/80 bg-white space-y-1">
              <div className="flex items-center gap-2 text-xs font-medium text-slate-500">
                <Smartphone className="w-4 h-4 text-slate-400" />
                Phone Number
              </div>
              <p className="text-sm font-semibold text-slate-900">{connection.phoneNumber || 'Not Connected'}</p>
            </div>

            <div className="p-4 rounded-lg border border-slate-200/80 bg-white space-y-1">
              <div className="flex items-center gap-2 text-xs font-medium text-slate-500">
                <Key className="w-4 h-4 text-slate-400" />
                Phone Number ID
              </div>
              <p className="text-sm font-mono text-slate-800">{connection.phoneNumberId || 'N/A'}</p>
            </div>

            <div className="p-4 rounded-lg border border-slate-200/80 bg-white space-y-1">
              <div className="flex items-center gap-2 text-xs font-medium text-slate-500">
                <Globe className="w-4 h-4 text-slate-400" />
                WABA Account ID
              </div>
              <p className="text-sm font-mono text-slate-800">{connection.wabaId || 'N/A'}</p>
            </div>

            <div className="p-4 rounded-lg border border-slate-200/80 bg-white space-y-1">
              <div className="flex items-center gap-2 text-xs font-medium text-slate-500">
                <CheckCircle className="w-4 h-4 text-slate-400" />
                Verified Business Name
              </div>
              <p className="text-sm font-semibold text-slate-900">{connection.verifiedName || connection.displayName || 'OmniConnect Business'}</p>
            </div>
          </div>

          {/* Security & Access Info */}
          <div className="p-4 rounded-lg bg-indigo-50/50 border border-indigo-100 space-y-2">
            <div className="flex items-center gap-2 text-xs font-semibold text-indigo-900">
              <Shield className="w-4 h-4 text-indigo-600" />
              Security & Scope Isolation
            </div>
            <p className="text-xs text-indigo-700/90 leading-relaxed">
              This connection is isolated. All chats, webhook callbacks, and bot flows routed through number{' '}
              <strong className="font-semibold">{connection.phoneNumber || 'assigned'}</strong> are strictly segregated and accessible only to authorized operators.
            </p>
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-slate-100 bg-slate-50/50">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-slate-700 bg-white border border-slate-200 hover:bg-slate-50 rounded-lg transition-colors shadow-sm"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
