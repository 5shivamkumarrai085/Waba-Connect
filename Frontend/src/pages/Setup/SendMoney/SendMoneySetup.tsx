import React, { useRef, useState } from 'react'
import { motion } from 'framer-motion'
import { Info } from 'lucide-react'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import usePermission from '../../../hooks/usePermission'
import useReference from '../../../hooks/useReference'
import { remittanceService } from '../../../services/remittance/remittanceService'
import { pageTransitionProps } from '../../../utils/motion'
import { CountriesTab } from './CountriesTab'
import { ListsTab } from './ListsTab'
import { PaymentMethodsTab } from './PaymentMethodsTab'
import './SendMoney.css'

type TabId = 'countries' | 'lists' | 'payments'

const TABS: { id: TabId; label: string }[] = [
  { id: 'countries', label: 'Countries & payout' },
  { id: 'lists', label: 'Regulatory lists' },
  { id: 'payments', label: 'Payment methods' }
]

/**
 * Setup › Send Money. Everything a customer sees in the Send Money WhatsApp Flow — countries,
 * payout methods, fees, recipient fields, banks, pick-lists and payment methods — is set here.
 */
export const SendMoneySetup: React.FC = () => {
  const { has } = usePermission()
  const canManage = has('Remittance.Manage')
  const reference = useReference(remittanceService.reference, 'remittance-reference')
  const [tab, setTab] = useState<TabId>('countries')
  const tabRefs = useRef<Record<TabId, HTMLButtonElement | null>>({ countries: null, lists: null, payments: null })

  // Arrow keys move between tabs, as the WAI-ARIA tabs pattern expects.
  const onTabKey = (e: React.KeyboardEvent, index: number) => {
    if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return
    e.preventDefault()
    const next = TABS[(index + (e.key === 'ArrowRight' ? 1 : TABS.length - 1)) % TABS.length]
    setTab(next.id)
    tabRefs.current[next.id]?.focus()
  }

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Send Money</h1>
        <p>Countries, fees, recipient details and payment methods for the Send Money journey in WhatsApp. Changes reach customers the next time they open the Flow.</p>
      </div>

      {reference.data && !reference.data.enabled && (
        <div className="setup-info-box sm-banner" role="status">
          <Info size={18} aria-hidden="true" />
          <div>
            <strong>Send Money is switched off on this server</strong>
            <p>You can prepare the settings now. Customers can start a transfer once <code translate="no">Remittance:Enabled</code> is turned on in the server configuration.</p>
          </div>
        </div>
      )}

      <div className="sm-tabs" role="tablist" aria-label="Send Money settings">
        {TABS.map((t, i) => (
          <button
            key={t.id}
            ref={el => { tabRefs.current[t.id] = el }}
            id={`sm-tab-${t.id}`}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            aria-controls={`sm-panel-${t.id}`}
            tabIndex={tab === t.id ? 0 : -1}
            className={`sm-tab${tab === t.id ? ' sm-tab-active' : ''}`}
            onClick={() => setTab(t.id)}
            onKeyDown={e => onTabKey(e, i)}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div id={`sm-panel-${tab}`} role="tabpanel" aria-labelledby={`sm-tab-${tab}`} className="sm-panel">
        {reference.loading && <Skeleton variant="table" />}
        {reference.error && (
          <EmptyState iconName="AlertCircle" title="Send Money settings could not be loaded" message={reference.error}
            action={{ label: 'Try Again', onClick: reference.retry }} />
        )}
        {reference.data && tab === 'countries' && <CountriesTab reference={reference.data} canManage={canManage} />}
        {reference.data && tab === 'lists' && <ListsTab reference={reference.data} canManage={canManage} />}
        {reference.data && tab === 'payments' && <PaymentMethodsTab reference={reference.data} canManage={canManage} />}
      </div>
    </motion.div>
  )
}

export default SendMoneySetup
