import React, { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Workflow, CheckCircle2, AlertTriangle, Circle, Loader2, Send, KeyRound, UploadCloud, Rocket, Link2 } from 'lucide-react'
import { CopyField } from '../../components/CopyField/CopyField'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import usePermission from '../../hooks/usePermission'
import { whatsAppFlowsService, type WhatsAppFlowsStatus } from '../../services/waba/whatsAppFlowsService'
import { getErrorMessage } from '../../utils/errorHelper'
import { formatAbsoluteDateTime, formatRelativeTime } from '../../utils/dateHelper'

type StepState = 'done' | 'problem' | 'todo'

const StepIcon: React.FC<{ state: StepState }> = ({ state }) =>
  state === 'done' ? <CheckCircle2 size={16} className="wf-ok" aria-label="Done" />
    : state === 'problem' ? <AlertTriangle size={16} className="wf-bad" aria-label="Needs attention" />
      : <Circle size={16} className="wf-todo" aria-label="Not done" />

const when = (iso: string | null) => iso
  ? <time dateTime={iso} title={formatAbsoluteDateTime(iso)}>{formatRelativeTime(iso)}</time>
  : null

/**
 * Connect WABA › WhatsApp Flows: what Meta needs before the Send Money Flow can open on a phone —
 * a public HTTPS address, this connection's encryption key, the Flow uploaded with our endpoint,
 * and (for customers outside the test list) publishing.
 */
export const WhatsAppFlowsCard: React.FC<{ connectionId: number }> = ({ connectionId }) => {
  const { has } = usePermission()
  const canConnect = has('ConnectAccount.Connect')

  const [status, setStatus] = useState<WhatsAppFlowsStatus | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState<'key' | 'sync' | 'publish' | 'send' | 'link' | null>(null)
  const [testPhone, setTestPhone] = useState('')
  const [linkFlowId, setLinkFlowId] = useState('')
  const [confirm, setConfirm] = useState<'regenerate' | 'publish' | 'new-draft' | 'link' | null>(null)

  const load = useCallback(async () => {
    setLoadError(null)
    try { setStatus(await whatsAppFlowsService.status(connectionId)) }
    catch (error) { setLoadError(getErrorMessage(error, 'WhatsApp Flows status could not be loaded.')) }
  }, [connectionId])

  useEffect(() => { void load() }, [load])

  const act = async (kind: 'key' | 'sync' | 'publish' | 'link', work: () => Promise<WhatsAppFlowsStatus>, success: string) => {
    setBusy(kind)
    try {
      const next = await work()
      setStatus(next)
      const problem = kind === 'key' ? next.key.lastError : next.sendMoney?.lastError
      if (problem) toast.error(problem)
      else toast.success(success)
      return true
    } catch (error) {
      toast.error(getErrorMessage(error, 'WhatsApp did not accept the request.'))
      void load()
      return false
    } finally {
      setBusy(null)
    }
  }

  const linkFlow = () =>
    act('link', () => whatsAppFlowsService.linkSendMoney(connectionId, linkFlowId.trim()), 'Flow linked to this connection.')
      .then(linked => { if (linked) setLinkFlowId('') })

  const submitLink = (e: React.FormEvent) => {
    e.preventDefault()
    // Replacing a Flow changes what customers receive, so say so first.
    if (status?.sendMoney?.metaFlowId && status.sendMoney.metaFlowId !== linkFlowId.trim()) setConfirm('link')
    else void linkFlow()
  }

  const sendTest = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy('send')
    try {
      const { success, message } = await whatsAppFlowsService.testSend(connectionId, testPhone)
      if (success) toast.success('Send Money Flow sent. Open WhatsApp on that phone.')
      else toast.error(message || 'The Flow could not be sent.')
    } catch (error) {
      toast.error(getErrorMessage(error, 'The Flow could not be sent.'))
    } finally {
      setBusy(null)
    }
  }

  if (loadError) {
    return (
      <div className="waba-setup-card">
        <Header />
        <p className="wf-error" role="alert">{loadError} <button type="button" className="wf-link" onClick={() => void load()}>Try again</button></p>
      </div>
    )
  }
  if (!status) {
    return (
      <div className="waba-setup-card">
        <Header />
        <p className="wf-muted" role="status"><Loader2 size={14} className="wf-spin" aria-hidden="true" /> Checking…</p>
      </div>
    )
  }

  const flow = status.sendMoney
  const errors = flow?.validationErrors ?? []
  const published = flow?.status === 'PUBLISHED'
  const addressState: StepState = status.publicAddressProblem ? 'problem' : 'done'
  const keyState: StepState = status.key.lastError ? 'problem' : status.key.uploadedAt ? 'done' : 'todo'
  const flowState: StepState = !flow?.metaFlowId ? 'todo' : errors.length > 0 || flow.lastError ? 'problem' : 'done'
  const endpointMatches = !!flow?.endpointUri && flow.endpointUri === flow.endpointUrl

  return (
    <div className="waba-setup-card">
      <Header />
      <p className="wf-muted">Send Money opens as a native form inside WhatsApp. Meta needs these steps done for this connection.</p>

      <ol className="wf-steps">
        <li>
          <StepIcon state={addressState} />
          <div>
            <strong>Public HTTPS address</strong>
            {status.publicAddressProblem
              ? <p className="wf-bad-text">{status.publicAddressProblem} Start ngrok and set <code translate="no">App:PublicBaseUrl</code> to its https address, then restart the server.</p>
              : <p className="wf-muted" translate="no">{status.publicBaseUrl}</p>}
          </div>
        </li>

        <li>
          <StepIcon state={keyState} />
          <div>
            <strong>Encryption key</strong>
            <p className="wf-muted">
              {status.key.uploadedAt ? <>Uploaded {when(status.key.uploadedAt)}{status.key.signatureStatus ? ` · signature ${status.key.signatureStatus.toLowerCase()}` : ''}</>
                : status.key.exists ? 'Created, not yet uploaded to WhatsApp.' : 'Not created yet.'}
            </p>
            {status.key.lastError && <p className="wf-bad-text">{status.key.lastError}</p>}
            {canConnect && (
              <div className="wf-actions">
                <button type="button" className="btn-waba-action" disabled={busy !== null}
                  onClick={() => act('key', () => whatsAppFlowsService.ensureKey(connectionId), 'Encryption key uploaded to WhatsApp.')}>
                  {busy === 'key' ? <Loader2 size={14} className="wf-spin" aria-hidden="true" /> : <KeyRound size={14} aria-hidden="true" />}
                  <span>{status.key.exists ? 'Upload Key' : 'Create & Upload Key'}</span>
                </button>
                {status.key.exists && (
                  <button type="button" className="wf-link" disabled={busy !== null} onClick={() => setConfirm('regenerate')}>New key…</button>
                )}
              </div>
            )}
          </div>
        </li>

        <li>
          <StepIcon state={flowState} />
          <div>
            <strong>Send Money Flow</strong>
            <p className="wf-muted">
              {flow?.metaFlowId
                ? <>Flow <code translate="no">{flow.metaFlowId}</code> · {flow.status ?? 'unknown'}{flow.jsonVersion ? ` · JSON ${flow.jsonVersion}` : ''}{flow.lastSyncedAt ? <> · synced {when(flow.lastSyncedAt)}</> : null}</>
                : 'Not on WhatsApp yet.'}
            </p>
            {flow?.lastError && <p className="wf-bad-text">{flow.lastError}</p>}
            {errors.length > 0 && (
              <div className="wf-validation" role="alert">
                <p>WhatsApp reported {errors.length} problem{errors.length === 1 ? '' : 's'} with the Flow JSON:</p>
                <ul>
                  {errors.slice(0, 10).map((e, i) => (
                    <li key={i}>
                      {e.message ?? e.error ?? 'Unknown problem'}
                      {e.line_start ? <span className="wf-muted"> (line {e.line_start}{e.column_start ? `, col ${e.column_start}` : ''})</span> : null}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {flow && (
              <div className="form-group wf-endpoint">
                <span className="waba-input-label">Data endpoint</span>
                <CopyField value={flow.endpointUrl} isSensitive={false} />
                {flow.endpointUri && !endpointMatches && (
                  <p className="wf-bad-text">WhatsApp still has {flow.endpointUri}. Sync again to update it.</p>
                )}
                <p className="wf-muted">
                  {flow.lastPingAt ? <>WhatsApp health check answered {when(flow.lastPingAt)}.</> : 'WhatsApp has not called this endpoint yet.'}
                </p>
              </div>
            )}
            {canConnect && (
              <div className="wf-actions">
                <button type="button" className="btn-waba-action" disabled={busy !== null || addressState === 'problem' || keyState === 'todo'}
                  title={addressState === 'problem' ? 'Set a public HTTPS address first' : keyState === 'todo' ? 'Upload the encryption key first' : undefined}
                  onClick={() => act('sync', () => whatsAppFlowsService.syncSendMoney(connectionId), 'Send Money Flow synced with WhatsApp.')}>
                  {busy === 'sync' ? <Loader2 size={14} className="wf-spin" aria-hidden="true" /> : <UploadCloud size={14} aria-hidden="true" />}
                  <span>{flow?.metaFlowId ? 'Sync Flow' : 'Create Flow'}</span>
                </button>
                {published && (
                  <button type="button" className="wf-link" disabled={busy !== null} onClick={() => setConfirm('new-draft')}>Start a new version…</button>
                )}
              </div>
            )}
            {canConnect && (
              <form className="wf-link-flow" onSubmit={submitLink}>
                <label className="waba-input-label" htmlFor="wf-link-id">Or use a Flow made in WhatsApp Manager</label>
                <div className="waba-input-with-button">
                  <input id="wf-link-id" className="form-control" inputMode="numeric" placeholder="Flow ID, e.g. 1356559833010787" value={linkFlowId}
                    autoComplete="off" spellCheck={false} onChange={e => setLinkFlowId(e.target.value)} aria-describedby="wf-link-hint" />
                  <button type="submit" className="btn-waba-action" disabled={busy !== null || linkFlowId.trim() === ''}>
                    {busy === 'link' ? <Loader2 size={14} className="wf-spin" aria-hidden="true" /> : <Link2 size={14} aria-hidden="true" />}
                    <span>Link Flow</span>
                  </button>
                </div>
                <p id="wf-link-hint" className="wf-muted">It must belong to this connection’s WhatsApp account and use the Send Money screens. A draft is pointed at this connection’s data endpoint automatically.</p>
              </form>
            )}
          </div>
        </li>

        <li>
          <StepIcon state={published ? 'done' : 'todo'} />
          <div>
            <strong>Publish</strong>
            <p className="wf-muted">
              {published
                ? <>Published {when(flow?.publishedAt ?? null)}. Customers can open it. A published Flow can't be edited — start a new version to change it.</>
                : status.allowDraftSend
                  ? 'Draft. It can be sent to the test numbers on your WhatsApp app while you check it. Publish once it works end to end.'
                  : 'Draft. Publish it before customers can open it.'}
            </p>
            {canConnect && !published && (
              <div className="wf-actions">
                <button type="button" className="btn-waba-action" disabled={busy !== null || flowState !== 'done'}
                  title={flowState !== 'done' ? 'Sync the Flow without errors first' : undefined} onClick={() => setConfirm('publish')}>
                  {busy === 'publish' ? <Loader2 size={14} className="wf-spin" aria-hidden="true" /> : <Rocket size={14} aria-hidden="true" />}
                  <span>Publish</span>
                </button>
              </div>
            )}
          </div>
        </li>
      </ol>

      {canConnect && flow?.metaFlowId && (
        <form className="wf-test" onSubmit={sendTest}>
          <label className="waba-input-label" htmlFor="wf-test-phone">Send the Flow to a test number</label>
          <div className="waba-input-with-button">
            <input id="wf-test-phone" type="tel" className="form-control" placeholder="+60 12 345 6789" value={testPhone} autoComplete="off"
              onChange={e => setTestPhone(e.target.value)} required aria-describedby="wf-test-hint" />
            <button type="submit" className="btn-waba-action" disabled={busy !== null || testPhone.trim() === ''}>
              {busy === 'send' ? <Loader2 size={14} className="wf-spin" aria-hidden="true" /> : <Send size={14} aria-hidden="true" />}
              <span>Send</span>
            </button>
          </div>
          <p id="wf-test-hint" className="wf-muted">The number must be an existing contact{published ? '' : ' and on your app’s test recipient list'}. This sends a real WhatsApp message.</p>
        </form>
      )}

      <ConfirmationModal
        isOpen={confirm !== null}
        title={confirm === 'regenerate' ? 'Replace Encryption Key' : confirm === 'publish' ? 'Publish Send Money Flow' : confirm === 'link' ? 'Switch Send Money Flow' : 'Start a New Version'}
        message={
          confirm === 'regenerate'
            ? 'Create a new key pair and upload it? Flows already open on customers’ phones stop working until they reopen them.'
            : confirm === 'publish'
              ? 'Publish the Send Money Flow? Every customer who types the trigger can open it, and it can no longer be edited — changes need a new version.'
              : confirm === 'link'
                ? `Use Flow ${linkFlowId.trim()} instead of ${flow?.metaFlowId ?? 'the current one'}? From now on the bot and test sends open the new Flow; the old one stays on WhatsApp unused.`
                : 'Create a new draft Flow from the current screens? The published one keeps working until the new version is published.'
        }
        confirmText={confirm === 'regenerate' ? 'Replace Key' : confirm === 'publish' ? 'Publish' : confirm === 'link' ? 'Use This Flow' : 'Create Draft'}
        onConfirm={() => {
          const which = confirm
          setConfirm(null)
          if (which === 'regenerate') void act('key', () => whatsAppFlowsService.ensureKey(connectionId, true), 'New encryption key uploaded.')
          if (which === 'publish') void act('publish', () => whatsAppFlowsService.publishSendMoney(connectionId), 'Send Money Flow published.')
          if (which === 'link') void linkFlow()
          if (which === 'new-draft') void act('sync', () => whatsAppFlowsService.syncSendMoney(connectionId, true), 'New draft created on WhatsApp.')
        }}
        onCancel={() => setConfirm(null)}
      />
    </div>
  )
}

const Header: React.FC = () => (
  <div className="waba-card-icon-header">
    <div className="waba-card-icon-box purple-theme">
      <Workflow size={18} />
    </div>
    <h3 className="waba-card-title">WhatsApp Flows</h3>
  </div>
)

export default WhatsAppFlowsCard
