import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { Plus, RefreshCw, MoreVertical, Webhook as WebhookIcon, KeyRound, RotateCcw, Loader2 } from 'lucide-react'
import { Menu, MenuItem } from '../../../components/Menu/Menu'
import { Modal } from '../../../components/Modal/Modal'
import { ConfirmationModal } from '../../../components/Modal/ConfirmationModal'
import { EmptyState } from '../../../components/EmptyState/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import { CopyField } from '../../../components/CopyField/CopyField'
import { StatusBadge } from '../../../components/StatusBadge/StatusBadge'
import { Pagination } from '../../../components/Pagination/Pagination'
import { Toggle } from '../../../components/Toggle/Toggle'
import { ChoicePills } from '../../../components/ChoicePills/ChoicePills'
import { SearchableSelect } from '../../../components/SearchableSelect/SearchableSelect'
import Can from '../../../components/Can/Can'
import usePermission from '../../../hooks/usePermission'
import useReference from '../../../hooks/useReference'
import { connectionService } from '../../../services/connections/connectionService'
import { referenceService, labelOf, type WebhookEventTypeOption } from '../../../services/referenceService'
import {
  webhookService,
  type Paged,
  type SaveWebhookInput,
  type Webhook,
  type WebhookDelivery
} from '../../../services/setup/webhookService'
import type { Connection } from '../../../types/connection'
import { getErrorMessage } from '../../../utils/errorHelper'
import { formatAbsoluteDateTime, formatRelativeTime } from '../../../utils/dateHelper'
import { pageTransitionProps } from '../../../utils/motion'
import './Webhooks.css'

const FORM_ID = 'webhook-form'
const ALL_EVENTS = '*'
const LOG_PAGE_SIZES = [10, 25, 50]

const emptyForm: SaveWebhookInput = { name: '', url: '', eventTypes: [], connectionIds: [], includePersonalData: false, isActive: true }

const hostOf = (url: string) => {
  try { return new URL(url).host } catch { return url }
}

/** How a webhook's health reads at a glance, as a StatusBadge type and label. */
const healthOf = (w: Webhook): { type: string; text: string } => {
  if (!w.isActive) return { type: 'closed', text: 'Off' }
  if (w.lastStatus === 'Failed') return { type: 'error', text: 'Failing' }
  if (w.lastStatus === 'Retrying') return { type: 'warning', text: 'Retrying' }
  if (!w.lastDeliveryAt) return { type: 'info', text: 'Waiting for events' }
  return { type: 'success', text: 'Healthy' }
}

const deliveryBadge = (status: string) =>
  status === 'Delivered' ? 'success' : status === 'Failed' ? 'error' : status === 'Pending' ? 'warning' : 'closed'

/** Setup › Webhooks: signed event delivery to other systems, with a delivery log and replay. */
export const WebhooksList: React.FC = () => {
  const { has } = usePermission()
  const canManage = has('Webhook.Manage')
  const options = useReference(referenceService.getWebhookOptions, 'webhook-options')

  const [webhooks, setWebhooks] = useState<Webhook[]>([])
  const [connections, setConnections] = useState<Connection[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [openMenuId, setOpenMenuId] = useState<number | null>(null)
  const [announcement, setAnnouncement] = useState('')

  const [editing, setEditing] = useState<Webhook | null>(null)
  const [isFormOpen, setIsFormOpen] = useState(false)
  const [form, setForm] = useState<SaveWebhookInput>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const formErrorRef = useRef<HTMLDivElement>(null)

  const [revealed, setRevealed] = useState<{ name: string; secret: string } | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<Webhook | null>(null)
  const [rotateTarget, setRotateTarget] = useState<Webhook | null>(null)
  const [testingId, setTestingId] = useState<number | null>(null)

  const [logFor, setLogFor] = useState<Webhook | null>(null)
  const [log, setLog] = useState<Paged<WebhookDelivery> | null>(null)
  const [logError, setLogError] = useState<string | null>(null)
  const [logPage, setLogPage] = useState(1)
  const [logPageSize, setLogPageSize] = useState(LOG_PAGE_SIZES[1])
  const [logStatus, setLogStatus] = useState('')
  const [replayingId, setReplayingId] = useState<number | null>(null)
  const [payloadOf, setPayloadOf] = useState<WebhookDelivery | null>(null)

  // A save error is announced and focused once it is on screen, so keyboard and screen-reader
  // users land on it.
  useEffect(() => {
    if (formError) formErrorRef.current?.focus()
  }, [formError])

  const load = useCallback(async () => {
    setIsLoading(true)
    setLoadError(null)
    try {
      setWebhooks(await webhookService.list())
    } catch (error) {
      setLoadError(getErrorMessage(error, 'Webhooks could not be loaded.'))
    } finally {
      setIsLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
    connectionService.getConnections().then(setConnections).catch(() => setConnections([]))
  }, [load])

  const eventGroups = useMemo(() => {
    const map = new Map<string, WebhookEventTypeOption[]>()
    for (const e of options.data?.eventTypes ?? []) {
      if (e.key === 'webhook.test') continue
      map.set(e.group, [...(map.get(e.group) ?? []), e])
    }
    return [...map.entries()]
  }, [options.data])

  const connectionName = (id: number) => {
    const c = connections.find(x => x.id === id)
    return c ? (c.nickname || c.name) : `Connection ${id}`
  }

  const openCreate = () => {
    setEditing(null)
    setForm(emptyForm)
    setFormError(null)
    setIsFormOpen(true)
  }

  const openEdit = (w: Webhook) => {
    setEditing(w)
    setForm({ name: w.name, url: w.url, eventTypes: w.eventTypes, connectionIds: w.connectionIds, includePersonalData: w.includePersonalData, isActive: w.isActive })
    setFormError(null)
    setIsFormOpen(true)
  }

  const allEvents = form.eventTypes.includes(ALL_EVENTS)
  const toggleEvent = (key: string) =>
    setForm(f => ({ ...f, eventTypes: f.eventTypes.includes(key) ? f.eventTypes.filter(k => k !== key) : [...f.eventTypes.filter(k => k !== ALL_EVENTS), key] }))
  const toggleGroup = (keys: string[], on: boolean) =>
    setForm(f => {
      const rest = f.eventTypes.filter(k => k !== ALL_EVENTS && !keys.includes(k))
      return { ...f, eventTypes: on ? [...rest, ...keys] : rest }
    })

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)
    setFormError(null)
    try {
      if (editing) {
        await webhookService.update(editing.id, form)
        toast.success(`Webhook “${form.name}” saved.`)
      } else {
        const created = await webhookService.create(form)
        if (created.secret) setRevealed({ name: created.name, secret: created.secret })
        toast.success(`Webhook “${created.name}” created.`)
      }
      setIsFormOpen(false)
      void load()
    } catch (error) {
      setFormError(getErrorMessage(error, 'The webhook could not be saved.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  const sendTest = async (w: Webhook) => {
    setTestingId(w.id)
    setAnnouncement(`Sending a test event to ${hostOf(w.url)}…`)
    try {
      const result = await webhookService.sendTest(w.id)
      setAnnouncement(result.message)
      if (result.success) toast.success(result.message)
      else toast.error(result.message || 'The test failed.')
      void load()
    } catch (error) {
      const message = getErrorMessage(error, 'The test could not be sent.')
      setAnnouncement(message)
      toast.error(message)
    } finally {
      setTestingId(null)
    }
  }

  const setActive = async (w: Webhook, isActive: boolean) => {
    try {
      await webhookService.update(w.id, { name: w.name, url: w.url, eventTypes: w.eventTypes, connectionIds: w.connectionIds, includePersonalData: w.includePersonalData, isActive })
      toast.success(isActive ? `“${w.name}” switched on.` : `“${w.name}” switched off.`)
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The webhook could not be updated.'))
    }
  }

  const confirmRotate = async () => {
    if (!rotateTarget) return
    try {
      const rotated = await webhookService.rotateSecret(rotateTarget.id)
      if (rotated.secret) setRevealed({ name: rotated.name, secret: rotated.secret })
    } catch (error) {
      toast.error(getErrorMessage(error, 'The secret could not be rotated.'))
    } finally {
      setRotateTarget(null)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await webhookService.remove(deleteTarget.id)
      toast.success(`Webhook “${deleteTarget.name}” deleted.`)
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The webhook could not be deleted.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const loadLog = useCallback(async (w: Webhook, page: number, pageSize: number, status: string) => {
    setLog(null)
    setLogError(null)
    try {
      setLog(await webhookService.deliveries(w.id, page, pageSize, status))
    } catch (error) {
      setLogError(getErrorMessage(error, 'Deliveries could not be loaded.'))
    }
  }, [])

  useEffect(() => {
    if (logFor) void loadLog(logFor, logPage, logPageSize, logStatus)
  }, [logFor, logPage, logPageSize, logStatus, loadLog])

  const openLog = (w: Webhook) => {
    setLogPage(1)
    setLogStatus('')
    setLogFor(w)
  }

  const replay = async (d: WebhookDelivery) => {
    setReplayingId(d.id)
    try {
      await webhookService.replay(d.id)
      toast.success(`${d.eventType} queued to send again.`)
      if (logFor) void loadLog(logFor, logPage, logPageSize, logStatus)
    } catch (error) {
      toast.error(getErrorMessage(error, 'The delivery could not be replayed.'))
    } finally {
      setReplayingId(null)
    }
  }

  const prettyPayload = (json: string) => {
    try { return JSON.stringify(JSON.parse(json), null, 2) } catch { return json }
  }

  const canSubmit = form.name.trim() !== '' && form.url.trim() !== '' && form.eventTypes.length > 0 && !isSubmitting

  return (
    <motion.div {...pageTransitionProps}>
      <div className="contacts-page-header">
        <h1>Webhooks</h1>
        <p>Send campaign, message, consent and chat events to your other systems. Every request is signed, so the receiver can prove it came from here.</p>
      </div>

      <div className="contacts-toolbar">
        <Can permission="Webhook.Manage">
          <button type="button" className="btn-toolbar-primary" onClick={openCreate}>
            <Plus size={15} aria-hidden="true" />
            <span>New Webhook</span>
          </button>
        </Can>
        <button type="button" className="btn-toolbar-tertiary" onClick={() => load()} disabled={isLoading}>
          <RefreshCw size={15} aria-hidden="true" className={isLoading ? 'webhook-spin' : undefined} />
          <span>Refresh</span>
        </button>
      </div>

      {/* Test results and other async outcomes, for screen readers. */}
      <div className="sr-only" aria-live="polite">{announcement}</div>

      <div className="contacts-card">
        <div className="data-table-wrapper">
          {isLoading ? (
            <Skeleton variant="table" />
          ) : loadError ? (
            <EmptyState iconName="AlertCircle" title="Webhooks could not be loaded" message={loadError}
              action={{ label: 'Try Again', onClick: () => void load() }} />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th className="actions-col" scope="col">Actions</th>
                  <th scope="col">Name</th>
                  <th scope="col">Events</th>
                  <th scope="col">Connections</th>
                  <th scope="col">Last Delivery</th>
                  <th scope="col">Status</th>
                  <th scope="col" className="text-center">On</th>
                </tr>
              </thead>
              <tbody>
                {webhooks.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="no-records-row">
                      <EmptyState
                        iconName="Webhook"
                        title="No webhooks yet"
                        message="Add one to push events to your CRM, data warehouse or automation tool as they happen."
                        action={canManage ? { label: 'New Webhook', onClick: openCreate } : undefined}
                      />
                    </td>
                  </tr>
                ) : webhooks.map(w => {
                  const health = healthOf(w)
                  const eventSummary = w.eventTypes.includes(ALL_EVENTS) ? 'All events' : `${w.eventTypes.length} event${w.eventTypes.length === 1 ? '' : 's'}`
                  return (
                    <tr key={w.id}>
                      <td className="actions-col">
                        <div className="contact-actions-menu-wrapper">
                          <Menu
                            open={openMenuId === w.id}
                            onOpenChange={(isOpen) => setOpenMenuId(isOpen ? w.id : null)}
                            align="start"
                            offset={4}
                            className="contact-actions-dropdown"
                            ariaLabel={`Actions for ${w.name}`}
                            trigger={(props) => (
                              <button {...props} type="button" className="contact-actions-trigger" aria-label={`Actions for ${w.name}`}>
                                <MoreVertical size={16} aria-hidden="true" />
                              </button>
                            )}
                          >
                            <MenuItem className="contact-actions-item" disabled={!canManage} onSelect={() => openEdit(w)}>Edit</MenuItem>
                            <MenuItem className="contact-actions-item" disabled={!canManage || !w.isActive || testingId === w.id} onSelect={() => sendTest(w)}>Send Test Event</MenuItem>
                            <MenuItem className="contact-actions-item" onSelect={() => openLog(w)}>View Delivery Log</MenuItem>
                            <MenuItem className="contact-actions-item" disabled={!canManage} onSelect={() => setRotateTarget(w)}>Rotate Secret</MenuItem>
                            <MenuItem destructive className="contact-actions-item" disabled={!canManage} onSelect={() => setDeleteTarget(w)}>Delete</MenuItem>
                          </Menu>
                        </div>
                      </td>
                      <td className="webhook-name-cell">
                        <button type="button" className="setup-user-name" onClick={() => canManage && openEdit(w)} disabled={!canManage}>{w.name}</button>
                        <span className="webhook-host" title={w.url} translate="no">{hostOf(w.url)}</span>
                      </td>
                      <td title={w.eventTypes.includes(ALL_EVENTS) ? undefined : w.eventTypes.join(', ')}>{eventSummary}</td>
                      <td className="setup-truncate" title={w.connectionIds.map(connectionName).join(', ')}>
                        {w.connectionIds.length === 0 ? 'All connections' : w.connectionIds.map(connectionName).join(', ')}
                      </td>
                      <td>
                        {w.lastDeliveryAt
                          ? <time dateTime={w.lastDeliveryAt} title={formatAbsoluteDateTime(w.lastDeliveryAt)}>{formatRelativeTime(w.lastDeliveryAt)}</time>
                          : <span className="webhook-muted">Never</span>}
                      </td>
                      <td title={!w.isActive && w.disabledReason ? w.disabledReason : undefined}>
                        <StatusBadge type={health.type} text={health.text} />
                      </td>
                      <td className="text-center">
                        <Toggle checked={w.isActive} onChange={on => setActive(w, on)} disabled={!canManage}
                          ariaLabel={`${w.name} is ${w.isActive ? 'on' : 'off'}`} disabledReason="You can view webhooks but not change them." />
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>

      <Modal
        isOpen={isFormOpen}
        onClose={() => setIsFormOpen(false)}
        title={editing ? 'Edit Webhook' : 'New Webhook'}
        icon={<WebhookIcon size={18} />}
        subtitle="Events are sent as JSON over HTTPS and signed with HMAC-SHA256"
        placement="right"
        size="md"
        footer={
          <button type="submit" form={FORM_ID} className="oc-dialog-btn oc-dialog-btn-primary" disabled={!canSubmit}>
            {isSubmitting && <Loader2 size={15} className="webhook-spin" aria-hidden="true" />}
            {isSubmitting ? 'Saving…' : editing ? 'Save Changes' : 'Create Webhook'}
          </button>
        }
      >
        <form id={FORM_ID} onSubmit={handleSubmit} className="setup-modal-form" noValidate>
          {formError && (
            <div ref={formErrorRef} tabIndex={-1} role="alert" className="webhook-form-error">{formError}</div>
          )}

          <div className="setup-field">
            <label className="setup-label required" htmlFor="webhook-name">Name</label>
            <input id="webhook-name" name="webhook-name" className="setup-input" value={form.name} maxLength={100} data-autofocus required autoComplete="off"
              onChange={e => setForm(f => ({ ...f, name: e.target.value }))} placeholder="CRM sync…" />
          </div>

          <div className="setup-field">
            <label className="setup-label required" htmlFor="webhook-url">Endpoint URL</label>
            <input id="webhook-url" name="webhook-url" className="setup-input" value={form.url} maxLength={500} required type="url" inputMode="url"
              autoComplete="off" spellCheck={false} translate="no"
              onChange={e => setForm(f => ({ ...f, url: e.target.value }))} placeholder="https://example.com/hooks/waba…" aria-describedby="webhook-url-hint" />
            <p id="webhook-url-hint" className="setup-hint">HTTPS only. Addresses on private or internal networks are refused.</p>
          </div>

          <fieldset className="setup-field webhook-fieldset">
            <legend className="setup-label required">Events</legend>
            {options.loading && <Skeleton variant="list" count={3} />}
            {options.error && (
              <p className="webhook-form-error" role="alert">
                {options.error} <button type="button" className="webhook-link-btn" onClick={options.retry}>Try again</button>
              </p>
            )}
            {options.data && (
              <>
                <Toggle checked={allEvents} onChange={on => setForm(f => ({ ...f, eventTypes: on ? [ALL_EVENTS] : [] }))}
                  label="All events, including ones added later" />
                {!allEvents && eventGroups.map(([group, items]) => {
                  const keys = items.map(i => i.key)
                  const selectedCount = keys.filter(k => form.eventTypes.includes(k)).length
                  return (
                    <fieldset key={group} className="webhook-event-group">
                      <legend>
                        <span>{group}</span>
                        <button type="button" className="webhook-link-btn" onClick={() => toggleGroup(keys, selectedCount < keys.length)}>
                          {selectedCount < keys.length ? 'Select all' : 'Clear'}
                        </button>
                      </legend>
                      {items.map(e => (
                        <label key={e.key} className="webhook-check" title={e.description}>
                          <input type="checkbox" name="webhook-events" value={e.key} checked={form.eventTypes.includes(e.key)} onChange={() => toggleEvent(e.key)} />
                          <code translate="no">{e.key}</code>
                        </label>
                      ))}
                    </fieldset>
                  )
                })}
              </>
            )}
          </fieldset>

          <div className="setup-field">
            <span className="setup-label" id="webhook-connections-label">Connections</span>
            <p className="setup-hint">Send only events from these connections. Leave all unselected to include every connection.</p>
            <ChoicePills
              multiple
              ariaLabel="Connections"
              options={connections.map(c => ({ value: c.id, label: c.nickname || c.name }))}
              selected={form.connectionIds}
              onChange={ids => setForm(f => ({ ...f, connectionIds: ids.map(Number) }))}
              emptyMessage="No connections yet."
            />
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Include personal data</span>
              <p className="setup-hint">Adds recipient addresses to message events. Leave off unless the receiver needs them.</p>
            </div>
            <Toggle checked={form.includePersonalData} onChange={on => setForm(f => ({ ...f, includePersonalData: on }))} ariaLabel="Include personal data" />
          </div>

          <div className="setup-toggle-item">
            <div>
              <span className="setup-toggle-label">Active</span>
              {editing?.disabledReason && !editing.isActive
                ? <p className="setup-hint webhook-danger-text">{editing.disabledReason}</p>
                : <p className="setup-hint">Switched-off webhooks keep their settings but receive nothing.</p>}
            </div>
            <Toggle checked={form.isActive} onChange={on => setForm(f => ({ ...f, isActive: on }))} ariaLabel="Active" />
          </div>
        </form>
      </Modal>

      <Modal
        isOpen={revealed !== null}
        onClose={() => setRevealed(null)}
        title="Signing Secret"
        icon={<KeyRound size={18} />}
        subtitle={revealed ? `For “${revealed.name}”. This is the only time it is shown — store it in your receiver now.` : undefined}
        size="md"
        footer={<button type="button" className="oc-dialog-btn oc-dialog-btn-primary" onClick={() => setRevealed(null)}>I've Stored It</button>}
      >
        {revealed && (
          <div className="webhook-secret">
            <CopyField value={revealed.secret} isSensitive allowReveal />
            <div className="webhook-secret-help">
              <p>Every request carries these headers:</p>
              <ul>
                <li><code translate="no">X-Waba-Signature: t=&lt;unix time&gt;,v1=&lt;hex&gt;</code> — HMAC-SHA256 of <code translate="no">t + "." + body</code> with this secret. Refuse timestamps older than five minutes.</li>
                <li><code translate="no">X-Waba-Event-Id</code> — the same for every retry and replay; use it to ignore duplicates.</li>
                <li><code translate="no">X-Waba-Event-Type</code> — for example <code translate="no">message.delivered</code>.</li>
              </ul>
            </div>
          </div>
        )}
      </Modal>

      <Modal
        isOpen={logFor !== null}
        onClose={() => { setLogFor(null); setLog(null) }}
        title={logFor ? `Delivery Log — ${logFor.name}` : 'Delivery Log'}
        subtitle={logFor ? hostOf(logFor.url) : undefined}
        icon={<WebhookIcon size={18} />}
        size="xl"
      >
        <div className="webhook-log-toolbar">
          <div className="webhook-log-filter">
            <SearchableSelect
              label="Status"
              value={logStatus}
              placeholder="All statuses…"
              options={(options.data?.deliveryStatuses ?? []).map(s => ({ value: s.value, label: s.label }))}
              onChange={v => { setLogPage(1); setLogStatus(v) }}
              searchThreshold={10}
            />
          </div>
          <button type="button" className="btn-toolbar-tertiary" onClick={() => logFor && loadLog(logFor, logPage, logPageSize, logStatus)}>
            <RefreshCw size={15} aria-hidden="true" /><span>Refresh</span>
          </button>
        </div>
        <div className="data-table-wrapper">
          {logError ? (
            <EmptyState iconName="AlertCircle" title="Deliveries could not be loaded" message={logError}
              action={{ label: 'Try Again', onClick: () => logFor && void loadLog(logFor, logPage, logPageSize, logStatus) }} />
          ) : !log ? (
            <Skeleton variant="table" />
          ) : log.items.length === 0 ? (
            <EmptyState iconName="History" title="No deliveries"
              message={logStatus ? `No ${labelOf(options.data?.deliveryStatuses, logStatus).toLowerCase()} deliveries.` : 'Events will appear here as they are sent.'} />
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col">When</th>
                  <th scope="col">Event</th>
                  <th scope="col">Status</th>
                  <th scope="col" className="text-right">Attempts</th>
                  <th scope="col">Response</th>
                  <th scope="col" className="text-right">Time</th>
                  <th scope="col"><span className="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                {log.items.map(d => (
                  <tr key={d.id}>
                    <td><time dateTime={d.createdAt}>{formatAbsoluteDateTime(d.createdAt)}</time></td>
                    <td>
                      <button type="button" className="setup-user-name" onClick={() => setPayloadOf(d)} aria-label={`View payload of ${d.eventType}`}>
                        <code translate="no">{d.eventType}</code>
                      </button>
                    </td>
                    <td title={d.error ?? undefined}><StatusBadge type={deliveryBadge(d.status)} text={labelOf(options.data?.deliveryStatuses, d.status)} /></td>
                    <td className="text-right webhook-num">{d.attempts}</td>
                    <td className="setup-truncate" title={d.error ?? undefined}>{d.responseCode ? `HTTP ${d.responseCode}` : d.error ? 'No response' : '—'}</td>
                    <td className="text-right webhook-num">{d.durationMs != null ? `${new Intl.NumberFormat().format(d.durationMs)} ms` : '—'}</td>
                    <td className="text-right">
                      {canManage && d.status !== 'Pending' && (
                        <button type="button" className="btn-toolbar-tertiary webhook-replay" onClick={() => replay(d)} disabled={replayingId === d.id}
                          aria-label={`Replay ${d.eventType} from ${formatAbsoluteDateTime(d.createdAt)}`} title="Send again, with the same event id">
                          <RotateCcw size={14} aria-hidden="true" /><span>{replayingId === d.id ? 'Queuing…' : 'Replay'}</span>
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        {log && log.totalCount > 0 && (
          <Pagination
            page={logPage}
            pageSize={logPageSize}
            totalCount={log.totalCount}
            totalPages={log.totalPages}
            onPage={setLogPage}
            onPageSize={size => { setLogPage(1); setLogPageSize(size) }}
            pageSizeOptions={LOG_PAGE_SIZES}
            itemLabel="deliveries"
          />
        )}
      </Modal>

      <Modal isOpen={payloadOf !== null} onClose={() => setPayloadOf(null)} title={payloadOf?.eventType} subtitle={payloadOf?.eventId} size="lg">
        {payloadOf && (
          <>
            {payloadOf.error && <p className="webhook-form-error" role="status">{payloadOf.error}</p>}
            <pre className="webhook-payload" translate="no">{prettyPayload(payloadOf.payloadJson)}</pre>
          </>
        )}
      </Modal>

      <ConfirmationModal
        isOpen={rotateTarget !== null}
        title="Rotate Signing Secret"
        message={`Issue a new secret for “${rotateTarget?.name}”? The current one stops working immediately, so update the receiver straight away.`}
        confirmText="Rotate Secret"
        onConfirm={confirmRotate}
        onCancel={() => setRotateTarget(null)}
      />

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Webhook"
        message={`Delete “${deleteTarget?.name}” and its delivery log? This cannot be undone.`}
        confirmText="Delete Webhook"
        isDestructive
        showWarningIcon
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </motion.div>
  )
}

export default WebhooksList
