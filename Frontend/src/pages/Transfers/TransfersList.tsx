import React, { useCallback, useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { RefreshCw, Search, ArrowLeftRight, ExternalLink } from 'lucide-react'
import { Modal } from '../../components/Modal/Modal'
import { EmptyState } from '../../components/EmptyState/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import { Pagination } from '../../components/Pagination/Pagination'
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'
import useReference from '../../hooks/useReference'
import {
  remittanceService,
  formatMoney,
  formatRate,
  humanize,
  orderStatusBadge,
  paymentStatusBadge,
  type Paged,
  type TransferOrderDetail,
  type TransferOrderEvent,
  type TransferOrderSummary
} from '../../services/remittance/remittanceService'
import { getErrorMessage } from '../../utils/errorHelper'
import { formatAbsoluteDateTime, formatRelativeTime } from '../../utils/dateHelper'
import { pageTransitionProps } from '../../utils/motion'
import '../Setup/SendMoney/SendMoney.css'

const PAGE_SIZES = [10, 25, 50]

/** A plain-language line for one timeline entry. */
const describe = (e: TransferOrderEvent): string => {
  if (e.kind === 'OrderStatus' && e.toStatus) return `Order ${humanize(e.toStatus).toLowerCase()}`
  if (e.kind === 'PaymentStatus' && e.toStatus) return `Payment ${humanize(e.toStatus).toLowerCase()}`
  if (e.kind === 'PaymentCallback') return 'Payment provider callback (signature verified)'
  if (e.kind === 'PayoutCallback') return 'Payout partner callback (signature verified)'
  if (e.kind === 'Notification') return e.detail?.startsWith('WhatsApp message failed') ? 'WhatsApp message to customer failed' : 'Customer notified on WhatsApp'
  return humanize(e.kind)
}

const dotClass = (e: TransferOrderEvent) =>
  e.kind === 'OrderStatus' || e.kind === 'PaymentStatus' ? 'sm-dot sm-dot-status' : e.kind.endsWith('Callback') ? 'sm-dot sm-dot-callback' : 'sm-dot'

/**
 * Transfers: every Send Money order with its order status and its payment status side by side —
 * they are separate facts. Payment changes only on a verified provider callback.
 */
export const TransfersList: React.FC = () => {
  const reference = useReference(remittanceService.reference, 'remittance-reference')

  const [data, setData] = useState<Paged<TransferOrderSummary> | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(PAGE_SIZES[1])
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [orderStatus, setOrderStatus] = useState('')
  const [paymentStatus, setPaymentStatus] = useState('')

  const [detailId, setDetailId] = useState<number | null>(null)
  const [detail, setDetail] = useState<TransferOrderDetail | null>(null)
  const [detailError, setDetailError] = useState<string | null>(null)

  // Search runs once typing pauses, not on every keystroke.
  useEffect(() => {
    const handle = window.setTimeout(() => { setPage(1); setSearch(searchInput.trim()) }, 350)
    return () => window.clearTimeout(handle)
  }, [searchInput])

  const load = useCallback(async () => {
    setLoadError(null)
    try {
      setData(await remittanceService.orders({ page, pageSize, search, orderStatus, paymentStatus }))
    } catch (error) {
      setLoadError(getErrorMessage(error, 'Transfers could not be loaded.'))
    }
  }, [page, pageSize, search, orderStatus, paymentStatus])

  useEffect(() => { setData(null); void load() }, [load])

  const loadDetail = useCallback(async (id: number) => {
    setDetail(null)
    setDetailError(null)
    try { setDetail(await remittanceService.order(id)) }
    catch (error) { setDetailError(getErrorMessage(error, 'The transfer could not be loaded.')) }
  }, [])

  useEffect(() => { if (detailId !== null) void loadDetail(detailId) }, [detailId, loadDetail])

  const filtered = search !== '' || orderStatus !== '' || paymentStatus !== ''
  const o = detail?.order

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Transfers</h1>
        <p>Send Money orders placed in WhatsApp. An order is marked paid only when the payment provider's signed callback confirms it.</p>
      </div>

      <div className="contacts-card">
        <div className="contacts-controls-row">
          <div className="sm-filters">
            <div className="setup-search">
              <Search size={15} className="setup-search-icon" aria-hidden="true" />
              <input type="search" placeholder="Reference, customer or recipient…" value={searchInput} aria-label="Search transfers"
                onChange={e => setSearchInput(e.target.value)} />
            </div>
            <div className="sm-filter">
              <SearchableSelect label="Order status" value={orderStatus} placeholder="All orders…" searchThreshold={20}
                options={(reference.data?.orderStatuses ?? []).map(s => ({ value: s, label: humanize(s) }))}
                onChange={v => { setPage(1); setOrderStatus(v) }} />
            </div>
            <div className="sm-filter">
              <SearchableSelect label="Payment status" value={paymentStatus} placeholder="All payments…" searchThreshold={20}
                options={(reference.data?.paymentStatuses ?? []).map(s => ({ value: s, label: humanize(s) }))}
                onChange={v => { setPage(1); setPaymentStatus(v) }} />
            </div>
          </div>
          <button type="button" className="btn-toolbar-tertiary" onClick={() => { setData(null); void load() }}>
            <RefreshCw size={15} aria-hidden="true" /><span>Refresh</span>
          </button>
        </div>

        <div className="data-table-wrapper">
          {loadError ? (
            <EmptyState iconName="AlertCircle" title="Transfers could not be loaded" message={loadError} action={{ label: 'Try Again', onClick: () => void load() }} />
          ) : !data ? (
            <Skeleton variant="table" />
          ) : data.items.length === 0 ? (
            <EmptyState iconName={filtered ? 'SearchX' : 'ArrowLeftRight'}
              title={filtered ? 'No matching transfers' : 'No transfers yet'}
              message={filtered ? 'Try another search or clear the filters.' : 'Orders appear here when customers confirm a transfer in the Send Money Flow.'} />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col">Reference</th>
                  <th scope="col">Customer</th>
                  <th scope="col">Recipient</th>
                  <th scope="col" className="text-right">Paying</th>
                  <th scope="col" className="text-right">Receiving</th>
                  <th scope="col">Order</th>
                  <th scope="col">Payment</th>
                  <th scope="col">Created</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map(t => (
                  <tr key={t.id}>
                    <td>
                      <button type="button" className="setup-user-name" onClick={() => setDetailId(t.id)} translate="no"
                        aria-label={`Open transfer ${t.reference}`}>
                        {t.reference}
                      </button>
                    </td>
                    <td className="setup-truncate" title={t.contactPhone}>{t.contactName || t.contactPhone}</td>
                    <td className="setup-truncate" title={[t.beneficiaryName, t.institutionName, t.payoutMethodName].filter(Boolean).join(' · ')}>
                      {t.beneficiaryName}
                    </td>
                    <td className="text-right"><span className="sm-amount">{formatMoney(t.sendCurrency, t.totalPayable)}<small translate="no">{t.paymentMethodCode}</small></span></td>
                    <td className="text-right sm-num">{formatMoney(t.receiveCurrency, t.receiveAmount, Number.isInteger(t.receiveAmount) ? 0 : 2)}</td>
                    <td><StatusBadge type={orderStatusBadge(t.orderStatus)} text={humanize(t.orderStatus)} /></td>
                    <td><StatusBadge type={paymentStatusBadge(t.paymentStatus)} text={humanize(t.paymentStatus)} /></td>
                    <td><time dateTime={t.createdAt} title={formatAbsoluteDateTime(t.createdAt)}>{formatRelativeTime(t.createdAt)}</time></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        {data && data.totalCount > 0 && (
          <Pagination page={page} pageSize={pageSize} totalCount={data.totalCount} totalPages={data.totalPages}
            onPage={setPage} onPageSize={size => { setPage(1); setPageSize(size) }} pageSizeOptions={PAGE_SIZES} itemLabel="transfers" />
        )}
      </div>

      <Modal
        isOpen={detailId !== null}
        onClose={() => { setDetailId(null); setDetail(null) }}
        title={o ? o.reference : 'Transfer'}
        subtitle={o ? `${o.contactName || o.contactPhone} → ${o.beneficiaryName}` : undefined}
        icon={<ArrowLeftRight size={18} />}
        placement="right"
        size="lg"
      >
        {detailError && <div className="sm-form-error" role="alert">{detailError}</div>}
        {!detail && !detailError && <Skeleton variant="list" count={6} />}
        {detail && o && (
          <div className="sm-detail">
            <div className="sm-statuses">
              <StatusBadge type={orderStatusBadge(o.orderStatus)} text={`Order: ${humanize(o.orderStatus)}`} />
              <StatusBadge type={paymentStatusBadge(o.paymentStatus)} text={`Payment: ${humanize(o.paymentStatus)}`} />
            </div>

            <section aria-labelledby="sm-d-money">
              <h3 id="sm-d-money">Amounts</h3>
              <dl className="sm-facts">
                <dt>Sending</dt><dd className="sm-num">{formatMoney(o.sendCurrency, o.sendAmount)}</dd>
                <dt>Fee</dt><dd className="sm-num">{formatMoney(o.sendCurrency, detail.feeAmount)}</dd>
                <dt>Total payable</dt><dd className="sm-num"><strong>{formatMoney(o.sendCurrency, o.totalPayable)}</strong></dd>
                <dt>Rate</dt><dd className="sm-num">1 {o.sendCurrency} = {formatRate(detail.appliedRate)} {o.receiveCurrency}</dd>
                <dt>Recipient gets</dt><dd className="sm-num">{formatMoney(o.receiveCurrency, o.receiveAmount, Number.isInteger(o.receiveAmount) ? 0 : 2)}</dd>
              </dl>
            </section>

            <section aria-labelledby="sm-d-recipient">
              <h3 id="sm-d-recipient">Recipient</h3>
              <dl className="sm-facts">
                <dt>Name</dt><dd>{o.beneficiaryName}</dd>
                <dt>Delivery</dt><dd>{o.payoutMethodName}</dd>
                {o.institutionName && <><dt>Bank</dt><dd>{o.institutionName}</dd></>}
                {detail.maskedAccount && <><dt>Account</dt><dd className="sm-num">{detail.maskedAccount}</dd></>}
                <dt>Purpose</dt><dd>{detail.purpose ? humanize(detail.purpose) : '—'}</dd>
                <dt>Source of funds</dt><dd>{detail.sourceOfFunds ? humanize(detail.sourceOfFunds) : '—'}</dd>
                {detail.remarks && <><dt>Remarks</dt><dd>{detail.remarks}</dd></>}
              </dl>
            </section>

            <section aria-labelledby="sm-d-payment">
              <h3 id="sm-d-payment">Payment and payout</h3>
              <dl className="sm-facts">
                <dt>Method</dt><dd translate="no">{o.paymentMethodCode} · {o.paymentProvider}</dd>
                <dt>Provider payment</dt><dd className="sm-code">{detail.providerPaymentId ?? '—'}</dd>
                {detail.paymentReference && <><dt>Counter reference</dt><dd className="sm-num">{detail.paymentReference}</dd></>}
                {detail.paymentUrl && (
                  <>
                    <dt>Payment page</dt>
                    <dd><a href={detail.paymentUrl} target="_blank" rel="noopener noreferrer" className="webhook-link-btn">Open <ExternalLink size={12} aria-hidden="true" /></a></dd>
                  </>
                )}
                <dt>Pay by</dt><dd>{detail.paymentExpiresAt ? formatAbsoluteDateTime(detail.paymentExpiresAt) : '—'}</dd>
                <dt>Paid</dt><dd>{o.paidAt ? formatAbsoluteDateTime(o.paidAt) : '—'}</dd>
                <dt>Payout</dt><dd translate="no">{detail.payoutProvider}{detail.payoutReference ? ` · ${detail.payoutReference}` : ''}</dd>
                <dt>Completed</dt><dd>{o.completedAt ? formatAbsoluteDateTime(o.completedAt) : '—'}</dd>
              </dl>
            </section>

            <section aria-labelledby="sm-d-timeline">
              <h3 id="sm-d-timeline">Timeline</h3>
              <ol className="sm-timeline">
                {detail.events.map(e => (
                  <li key={e.id}>
                    <span className={dotClass(e)} aria-hidden="true" />
                    <div>
                      <div className="sm-event-head">
                        <span>{describe(e)}</span>
                        <time dateTime={e.occurredAt}>{formatAbsoluteDateTime(e.occurredAt)}</time>
                      </div>
                      {(e.detail || e.provider) && (
                        <p className="sm-event-detail">
                          {[e.source !== 'System' ? e.source : null, e.provider, e.providerEventId].filter(Boolean).join(' · ')}
                          {e.detail && <>{e.source !== 'System' || e.provider ? ' — ' : ''}{e.detail}</>}
                        </p>
                      )}
                    </div>
                  </li>
                ))}
              </ol>
            </section>
          </div>
        )}
      </Modal>
    </motion.div>
  )
}

export default TransfersList
