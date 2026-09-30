import React, { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import { motion } from 'framer-motion'
import { useNavigate, useSearchParams } from 'react-router-dom'
import {
  AlertCircle, AlertTriangle, ArrowLeft, CheckCircle2, Inbox, KeyRound, Link2, Loader2,
  RefreshCw, Save, Send, ServerCog, ShieldCheck, UserRound
} from 'lucide-react'
import toast from 'react-hot-toast'
import { pageTransitionProps } from '../../utils/motion'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { referenceService, type EmailPortPreset } from '../../services/referenceService'
import useReference from '../../hooks/useReference'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import { Skeleton } from '../../components/Skeleton'
import Toggle from '../../components/Toggle/Toggle'
import { getErrorMessage } from '../../utils/errorHelper'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import type { DomainHealth, EmailConnection, EmailProviderTestResult, SmtpSecurityMode } from '../../types/email'
import './ConnectEmail.css'

const STATUS_BADGE: Record<string, string> = {
  Connected: 'success',
  'Needs attention': 'error',
  Disconnected: 'error',
  'Setup pending': 'warning'
}

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface SmtpForm {
  host: string
  port: string
  security: SmtpSecurityMode
  username: string
  password: string
  fromName: string
  fromEmail: string
  replyTo: string
  sendRate: string
}

interface ImapForm {
  host: string
  port: string
  security: SmtpSecurityMode
  username: string
  password: string
  allowInvalidCertificate: boolean
}

const smtpFrom = (c: EmailConnection): SmtpForm => ({
  host: c.smtpHost ?? '',
  port: c.smtpPort?.toString() ?? '',
  security: c.smtpSecurity ?? 'StartTls',
  username: c.smtpUsername ?? '',
  password: '',
  fromName: c.defaultFromName ?? '',
  fromEmail: c.defaultFromEmail ?? '',
  replyTo: c.defaultReplyTo ?? '',
  sendRate: c.maxSendRatePerSecond?.toString() ?? ''
})

const imapFrom = (c: EmailConnection): ImapForm => ({
  host: c.imapHost ?? '',
  port: c.imapPort?.toString() ?? '',
  security: c.imapSecurity ?? 'SslOnConnect',
  username: c.imapUsername ?? '',
  password: '',
  allowInvalidCertificate: c.imapAllowInvalidCertificate ?? false
})

/** The security a well-known port requires, from the server's catalogue. */
const impliedSecurity = (ports: EmailPortPreset[] | undefined, port: string) =>
  ports?.find(p => String(p.port) === port.trim())?.security as SmtpSecurityMode | undefined

/**
 * One email connection: the SMTP account it sends through, the IMAP mailbox replies are read
 * from, and the live checks that say whether both work. Passwords are write-only — the API only
 * says whether one is stored — and every option list and limit comes from the server
 * (GET api/reference/email-options).
 */
export const ConnectEmail: React.FC = () => {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const configurationId = Number(searchParams.get('emailConfigurationId') || 0)
  const options = useReference(referenceService.getEmailOptions, 'email-options')
  const id = useId()
  const hostRef = useRef<HTMLInputElement>(null)

  const [connection, setConnection] = useState<EmailConnection | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [smtp, setSmtp] = useState<SmtpForm | null>(null)
  const [imap, setImap] = useState<ImapForm | null>(null)
  const [errors, setErrors] = useState<Partial<Record<keyof SmtpForm | 'imapHost' | 'imapPort' | 'testAddress', string>>>({})

  const [busy, setBusy] = useState<null | 'save' | 'test' | 'saveImap' | 'testImap' | 'send'>(null)
  const [smtpResult, setSmtpResult] = useState<EmailProviderTestResult | null>(null)
  const [imapResult, setImapResult] = useState<EmailProviderTestResult | null>(null)
  const [sendResult, setSendResult] = useState<EmailProviderTestResult | null>(null)
  const [testAddress, setTestAddress] = useState('')

  const [health, setHealth] = useState<DomainHealth[] | null>(null)
  const [healthError, setHealthError] = useState<string | null>(null)
  const [healthLoading, setHealthLoading] = useState(false)

  const load = useCallback(async () => {
    if (!configurationId) {
      setLoadError('No email connection was specified.')
      return
    }
    const loaded = await emailConnectionService.getConnection(configurationId)
    if (!loaded) {
      setLoadError('That email connection could not be found. It may have been deleted.')
      return
    }
    setLoadError(null)
    setConnection(loaded)
    setSmtp(smtpFrom(loaded))
    setImap(imapFrom(loaded))
  }, [configurationId])

  const loadHealth = useCallback(async () => {
    if (!configurationId) return
    setHealthLoading(true)
    setHealthError(null)
    try {
      setHealth(await emailConnectionService.getDomainHealth(configurationId))
    } catch (err) {
      setHealthError(getErrorMessage(err, 'The DNS checks could not be run.'))
    } finally {
      setHealthLoading(false)
    }
  }, [configurationId])

  useEffect(() => { void load() }, [load])
  useEffect(() => { void loadHealth() }, [loadHealth])

  const smtpDirty = useMemo(() => {
    if (!connection || !smtp) return false
    const saved = smtpFrom(connection)
    return (Object.keys(saved) as (keyof SmtpForm)[]).some(k => saved[k] !== smtp[k])
  }, [connection, smtp])

  const imapDirty = useMemo(() => {
    if (!connection || !imap) return false
    const saved = imapFrom(connection)
    return (Object.keys(saved) as (keyof ImapForm)[]).some(k => saved[k] !== imap[k])
  }, [connection, imap])

  // Leaving with unsaved settings asks first.
  useEffect(() => {
    if (!smtpDirty && !imapDirty) return
    const warn = (e: BeforeUnloadEvent) => { e.preventDefault() }
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [smtpDirty, imapDirty])

  const leave = () => {
    if ((smtpDirty || imapDirty) && !window.confirm('Leave without saving your changes?')) return
    navigate('/connections')
  }

  const setSmtpField = <K extends keyof SmtpForm>(key: K, value: SmtpForm[K]) => {
    setSmtp(prev => {
      if (!prev) return prev
      const next = { ...prev, [key]: value }
      // A well-known port implies its security; an explicit "None" is never overridden.
      if (key === 'port') {
        const implied = impliedSecurity(options.data?.smtpPorts, String(value))
        if (implied && prev.security !== 'None') next.security = implied
      }
      return next
    })
    setErrors(prev => ({ ...prev, [key]: undefined }))
  }

  const setImapField = <K extends keyof ImapForm>(key: K, value: ImapForm[K]) => {
    setImap(prev => {
      if (!prev) return prev
      const next = { ...prev, [key]: value }
      if (key === 'port') {
        const implied = impliedSecurity(options.data?.imapPorts, String(value))
        if (implied && prev.security !== 'None') next.security = implied
      }
      return next
    })
    setErrors(prev => ({ ...prev, imapHost: key === 'host' ? undefined : prev.imapHost, imapPort: key === 'port' ? undefined : prev.imapPort }))
  }

  /** Field errors for the SMTP form; empty when it may be saved or tested. */
  const validateSmtp = (form: SmtpForm) => {
    const next: typeof errors = {}
    const rate = options.data?.sendRate
    if (!form.host.trim()) next.host = 'Enter the SMTP server, for example mail.example.com.'
    if (form.port && (!Number.isInteger(Number(form.port)) || Number(form.port) < 1 || Number(form.port) > 65535)) next.port = 'Enter a port between 1 and 65535.'
    if (form.fromEmail && !EMAIL_PATTERN.test(form.fromEmail.trim())) next.fromEmail = 'Enter a valid email address.'
    if (form.replyTo && !EMAIL_PATTERN.test(form.replyTo.trim())) next.replyTo = 'Enter a valid email address.'
    if (form.sendRate && rate && (Number.isNaN(Number(form.sendRate)) || Number(form.sendRate) < rate.min || Number(form.sendRate) > rate.max)) {
      next.sendRate = `Enter a rate between ${rate.min} and ${rate.max} emails per second.`
    }
    return next
  }

  const payload = (form: SmtpForm) => ({
    provider: 'Smtp' as const,
    smtpHost: form.host.trim() || undefined,
    smtpPort: form.port ? Number(form.port) : undefined,
    smtpSecurity: form.security,
    smtpUsername: form.username.trim() || undefined,
    smtpPassword: form.password.trim() || undefined,
    defaultFromName: form.fromName.trim() || undefined,
    defaultFromEmail: form.fromEmail.trim() || undefined,
    defaultReplyTo: form.replyTo.trim() || undefined,
    maxSendRatePerSecond: form.sendRate ? Number(form.sendRate) : undefined,
    isActive: true
  })

  const checkSmtp = () => {
    if (!smtp) return false
    const found = validateSmtp(smtp)
    setErrors(prev => ({ ...prev, ...found }))
    if (Object.keys(found).length > 0) {
      if (found.host) hostRef.current?.focus()
      return false
    }
    return true
  }

  const handleSave = async (e?: React.FormEvent) => {
    e?.preventDefault()
    if (!smtp || !checkSmtp()) return
    setBusy('save')
    try {
      const saved = await emailConnectionService.saveProvider(configurationId, payload(smtp))
      setConnection(saved)
      setSmtp(smtpFrom(saved))
      toast.success('Sending settings saved.')
      void loadHealth()
    } catch (err) {
      toast.error(getErrorMessage(err, 'The sending settings could not be saved.'))
    } finally {
      setBusy(null)
    }
  }

  const handleTest = async () => {
    if (!smtp || !checkSmtp()) return
    setBusy('test')
    setSmtpResult(null)
    try {
      setSmtpResult(await emailConnectionService.testUnsavedConnection({ ...payload(smtp), emailConfigurationId: configurationId }))
    } catch (err) {
      setSmtpResult({ success: false, message: getErrorMessage(err, 'The connection test failed.') })
    } finally {
      setBusy(null)
    }
  }

  const handleSaveImap = async () => {
    if (!imap) return
    const next: typeof errors = {}
    if (!imap.host.trim()) next.imapHost = 'Enter the IMAP server, for example mail.example.com.'
    if (imap.port && (!Number.isInteger(Number(imap.port)) || Number(imap.port) < 1 || Number(imap.port) > 65535)) next.imapPort = 'Enter a port between 1 and 65535.'
    setErrors(prev => ({ ...prev, ...next }))
    if (Object.keys(next).length > 0) return
    setBusy('saveImap')
    try {
      const saved = await emailConnectionService.saveImapSettings(configurationId, {
        imapHost: imap.host.trim(),
        imapPort: imap.port ? Number(imap.port) : undefined,
        imapSecurity: imap.security,
        imapUsername: imap.username.trim() || undefined,
        imapPassword: imap.password.trim() || undefined,
        imapAllowInvalidCertificate: imap.allowInvalidCertificate
      })
      setConnection(saved)
      setImap(imapFrom(saved))
      toast.success('Receiving settings saved. Replies are checked on the next poll.')
    } catch (err) {
      toast.error(getErrorMessage(err, 'The receiving settings could not be saved.'))
    } finally {
      setBusy(null)
    }
  }

  const handleTestImap = async () => {
    setBusy('testImap')
    setImapResult(null)
    try {
      setImapResult(await emailConnectionService.testImapConnection(configurationId))
    } catch (err) {
      setImapResult({ success: false, message: getErrorMessage(err, 'The mailbox could not be reached.') })
    } finally {
      setBusy(null)
    }
  }

  const handleSendTest = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!EMAIL_PATTERN.test(testAddress.trim())) {
      setErrors(prev => ({ ...prev, testAddress: 'Enter the address to send the test to.' }))
      return
    }
    setBusy('send')
    setSendResult(null)
    try {
      setSendResult(await emailConnectionService.sendTestEmail(configurationId, { toAddress: testAddress.trim() }))
    } catch (err) {
      setSendResult({ success: false, message: getErrorMessage(err, 'The test email could not be sent.') })
    } finally {
      setBusy(null)
    }
  }

  if (loadError) {
    return (
      <div className="ec-page">
        <div className="ec-panel ec-fatal" role="alert">
          <AlertCircle size={20} aria-hidden="true" />
          <div>
            <h2>This connection could not be opened</h2>
            <p>{loadError}</p>
          </div>
          <button type="button" className="btn btn-secondary" onClick={() => navigate('/connections')}>
            <ArrowLeft size={16} aria-hidden="true" /> Back to Connections
          </button>
        </div>
      </div>
    )
  }

  if (!connection || !smtp || !imap) {
    return (
      <div className="ec-page" aria-busy="true">
        <Skeleton variant="title" width={320} height={32} />
        <div className="ec-layout">
          <div className="ec-main"><Skeleton variant="card" count={2} /></div>
          <aside className="ec-side"><Skeleton variant="card" /></aside>
        </div>
      </div>
    )
  }

  const securityOptions = options.data?.securityModes ?? []
  const describeSecurity = (value: string) => securityOptions.find(o => o.value === value)?.description
  const smtpPortMismatch = (() => {
    const implied = impliedSecurity(options.data?.smtpPorts, smtp.port)
    return implied && smtp.security !== 'None' && implied !== smtp.security ? implied : null
  })()
  const imapDefaultPort = options.data?.defaultImapPort
  const worstLevel = (checks: DomainHealth['checks']) =>
    checks.some(c => c.level === 'fail') ? 'error' : checks.some(c => c.level === 'warn') ? 'warning' : 'success'

  return (
    <motion.div className="ec-page" {...pageTransitionProps}>
      <header className="omni-page-hero form-page-hero ec-hero">
        <button type="button" className="btn-back" onClick={leave} aria-label="Back to Connections">
          <ArrowLeft size={18} aria-hidden="true" />
        </button>
        <div className="ec-hero-text">
          <h1>{connection.connectionName}</h1>
          <p>{connection.defaultFromEmail ?? 'Email connection'} · sends by SMTP, receives replies by IMAP</p>
        </div>
        <StatusBadge type={STATUS_BADGE[connection.status] ?? 'info'} text={connection.status} />
      </header>

      {!connection.credentialsReadable && (
        <div className="ec-alert is-error" role="alert">
          <KeyRound size={18} aria-hidden="true" />
          <p>
            <strong>The stored password can't be read by this server.</strong> Sends and reply checks on this connection
            will be held until it is re-entered. Type the password below and save.
          </p>
        </div>
      )}

      <div className="ec-layout">
        <div className="ec-main">
          {/* ── Sending ─────────────────────────────────────────────────────── */}
          <form className="ec-panel" aria-labelledby={`${id}-smtp`} onSubmit={handleSave} noValidate>
            <div className="ec-panel-head">
              <span className="ec-panel-icon" aria-hidden="true"><Send size={18} /></span>
              <div>
                <h2 id={`${id}-smtp`}>Sending (SMTP)</h2>
                <p>The mail server campaigns, chat replies and proofs are sent through.</p>
              </div>
            </div>

            <div className="ec-grid">
              <Field id={`${id}-host`} label="SMTP server" required error={errors.host}>
                <input ref={hostRef} id={`${id}-host`} className="form-control" value={smtp.host} autoComplete="off" spellCheck={false}
                  placeholder="mail.example.com…" aria-invalid={!!errors.host} aria-describedby={errors.host ? `${id}-host-error` : undefined}
                  onChange={e => setSmtpField('host', e.target.value)} />
              </Field>

              <Field id={`${id}-port`} label="Port" error={errors.port} hint={options.data ? `Common: ${options.data.smtpPorts.map(p => p.label).join(', ')}` : undefined}>
                <input id={`${id}-port`} className="form-control" inputMode="numeric" list={`${id}-smtp-ports`} value={smtp.port}
                  placeholder={options.data?.smtpPorts[0] ? String(options.data.smtpPorts[0].port) : ''} aria-invalid={!!errors.port}
                  onChange={e => setSmtpField('port', e.target.value.replace(/\D/g, ''))} />
                <datalist id={`${id}-smtp-ports`}>
                  {options.data?.smtpPorts.map(p => <option key={p.port} value={p.port}>{p.label}</option>)}
                </datalist>
              </Field>

              <Field id={`${id}-security`} label="Security" hint={describeSecurity(smtp.security)}
                warning={smtpPortMismatch ? `Port ${smtp.port} expects ${securityOptions.find(o => o.value === smtpPortMismatch)?.label ?? smtpPortMismatch}; the server will use that.` : undefined}>
                <select id={`${id}-security`} className="form-control" value={smtp.security} disabled={!options.data}
                  onChange={e => setSmtpField('security', e.target.value as SmtpSecurityMode)}>
                  {securityOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>

              <Field id={`${id}-user`} label="Username">
                <input id={`${id}-user`} className="form-control" value={smtp.username} autoComplete="off" spellCheck={false}
                  placeholder="name@example.com…" onChange={e => setSmtpField('username', e.target.value)} />
              </Field>

              <Field id={`${id}-password`} label="Password"
                hint={connection.hasSmtpPassword ? (connection.credentialsReadable ? 'A password is saved. Leave blank to keep it.' : 'The saved password is unreadable. Enter it again.') : undefined}>
                <input id={`${id}-password`} type="password" className="form-control" value={smtp.password} autoComplete="new-password"
                  placeholder={connection.hasSmtpPassword ? '••••••••' : 'Enter the mailbox password…'}
                  onChange={e => setSmtpField('password', e.target.value)} />
              </Field>

              <Field id={`${id}-rate`} label="Sending rate" error={errors.sendRate}
                hint={options.data ? `Emails per second. Leave blank for the default (${options.data.sendRate.default}/s).` : undefined}>
                <input id={`${id}-rate`} className="form-control" inputMode="decimal" value={smtp.sendRate}
                  placeholder={options.data ? String(options.data.sendRate.default) : ''} aria-invalid={!!errors.sendRate}
                  onChange={e => setSmtpField('sendRate', e.target.value.replace(/[^\d.]/g, ''))} />
              </Field>
            </div>

            <fieldset className="ec-fieldset">
              <legend>Default sender</legend>
              <div className="ec-grid">
                <Field id={`${id}-from-name`} label="From name">
                  <input id={`${id}-from-name`} className="form-control" value={smtp.fromName} autoComplete="off"
                    onChange={e => setSmtpField('fromName', e.target.value)} />
                </Field>
                <Field id={`${id}-from-email`} label="From address" error={errors.fromEmail}>
                  <input id={`${id}-from-email`} type="email" className="form-control" value={smtp.fromEmail} autoComplete="off" spellCheck={false}
                    aria-invalid={!!errors.fromEmail} onChange={e => setSmtpField('fromEmail', e.target.value)} />
                </Field>
                <Field id={`${id}-reply`} label="Replies go to" error={errors.replyTo} hint="Optional. Must be the mailbox below for replies to reach Chat.">
                  <input id={`${id}-reply`} type="email" className="form-control" value={smtp.replyTo} autoComplete="off" spellCheck={false}
                    aria-invalid={!!errors.replyTo} onChange={e => setSmtpField('replyTo', e.target.value)} />
                </Field>
              </div>
            </fieldset>

            <div aria-live="polite">{smtpResult && <Result result={smtpResult} successTitle="The server accepted the login" />}</div>

            <div className="ec-actions">
              <button type="button" className="btn btn-secondary" onClick={handleTest} disabled={busy !== null}>
                {busy === 'test' ? <Loader2 size={16} className="ec-spin" aria-hidden="true" /> : <Link2 size={16} aria-hidden="true" />}
                {busy === 'test' ? 'Testing…' : 'Test Connection'}
              </button>
              <span className="ec-actions-note">{smtpDirty ? 'Unsaved changes' : connection.lastTestedAt
                ? `Last tested ${formatAbsoluteDateTime(connection.lastTestedAt)} · ${connection.lastTestSucceeded ? 'passed' : 'failed'}` : ''}</span>
              <button type="submit" className="btn btn-primary" disabled={busy !== null || !smtpDirty}
                title={smtpDirty ? undefined : 'Nothing has changed yet.'}>
                {busy === 'save' ? <Loader2 size={16} className="ec-spin" aria-hidden="true" /> : <Save size={16} aria-hidden="true" />}
                {busy === 'save' ? 'Saving…' : 'Save Sending Settings'}
              </button>
            </div>
          </form>

          {/* ── Receiving ───────────────────────────────────────────────────── */}
          <section className="ec-panel" aria-labelledby={`${id}-imap`}>
            <div className="ec-panel-head">
              <span className="ec-panel-icon" aria-hidden="true"><Inbox size={18} /></span>
              <div>
                <h2 id={`${id}-imap`}>Receiving replies (IMAP)</h2>
                <p>The mailbox checked for replies and bounce reports, so they appear in Chat and in campaign results.</p>
              </div>
            </div>

            <div className={`ec-inbound ${connection.imapLastError ? 'is-error' : connection.effectiveImapHost ? 'is-ok' : 'is-idle'}`} role="status">
              {connection.imapLastError ? <AlertTriangle size={18} aria-hidden="true" /> : <ServerCog size={18} aria-hidden="true" />}
              <div>
                {connection.effectiveImapHost ? (
                  <p>
                    Reading replies from <strong>{connection.effectiveImapHost}</strong>
                    {connection.imapHostIsDerived
                      ? `, worked out from the SMTP server${imapDefaultPort ? ` (port ${imapDefaultPort}, SSL/TLS)` : ''} with the SMTP login. Save your own settings below to override.`
                      : ' with the settings below.'}
                  </p>
                ) : (
                  <p>No mailbox is being checked yet. Save a server below to receive replies.</p>
                )}
                <p className="ec-inbound-meta">
                  {connection.imapLastPolledAt ? `Last checked ${formatAbsoluteDateTime(connection.imapLastPolledAt)}` : 'Not checked yet'}
                  {connection.imapLastError && <> · <span className="ec-error-text">{connection.imapLastError}</span></>}
                </p>
              </div>
            </div>

            <div className="ec-grid">
              <Field id={`${id}-imap-host`} label="IMAP server" required error={errors.imapHost}>
                <input id={`${id}-imap-host`} className="form-control" value={imap.host} autoComplete="off" spellCheck={false}
                  placeholder={connection.imapHostIsDerived && connection.effectiveImapHost ? `${connection.effectiveImapHost}…` : 'mail.example.com…'}
                  aria-invalid={!!errors.imapHost} onChange={e => setImapField('host', e.target.value)} />
              </Field>
              <Field id={`${id}-imap-port`} label="Port" error={errors.imapPort} hint={options.data ? `Common: ${options.data.imapPorts.map(p => p.label).join(', ')}` : undefined}>
                <input id={`${id}-imap-port`} className="form-control" inputMode="numeric" list={`${id}-imap-ports`} value={imap.port}
                  placeholder={imapDefaultPort ? String(imapDefaultPort) : ''} aria-invalid={!!errors.imapPort}
                  onChange={e => setImapField('port', e.target.value.replace(/\D/g, ''))} />
                <datalist id={`${id}-imap-ports`}>
                  {options.data?.imapPorts.map(p => <option key={p.port} value={p.port}>{p.label}</option>)}
                </datalist>
              </Field>
              <Field id={`${id}-imap-security`} label="Security" hint={describeSecurity(imap.security)}>
                <select id={`${id}-imap-security`} className="form-control" value={imap.security} disabled={!options.data}
                  onChange={e => setImapField('security', e.target.value as SmtpSecurityMode)}>
                  {securityOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>
              <Field id={`${id}-imap-user`} label="Username" hint="Blank uses the SMTP username.">
                <input id={`${id}-imap-user`} className="form-control" value={imap.username} autoComplete="off" spellCheck={false}
                  placeholder={smtp.username ? `${smtp.username}…` : ''} onChange={e => setImapField('username', e.target.value)} />
              </Field>
              <Field id={`${id}-imap-password`} label="Password"
                hint={connection.hasImapPassword ? 'A password is saved. Leave blank to keep it.' : 'Blank uses the SMTP password.'}>
                <input id={`${id}-imap-password`} type="password" className="form-control" value={imap.password} autoComplete="new-password"
                  placeholder={connection.hasImapPassword ? '••••••••' : ''} onChange={e => setImapField('password', e.target.value)} />
              </Field>
            </div>

            <div className="ec-toggle-row">
              <Toggle checked={imap.allowInvalidCertificate} onChange={v => setImapField('allowInvalidCertificate', v)}
                label="Accept a certificate issued to another name" />
              <p className="ec-hint">
                Only for shared hosting whose certificate names the host company. The connection stays encrypted, but the
                server's identity is not checked.
              </p>
            </div>

            <div aria-live="polite">{imapResult && <Result result={imapResult} successTitle="The mailbox is reachable" />}</div>

            <div className="ec-actions">
              <button type="button" className="btn btn-secondary" onClick={handleTestImap}
                disabled={busy !== null || !connection.effectiveImapHost || imapDirty}
                title={imapDirty ? 'Save your changes first; the test uses the saved settings.' : !connection.effectiveImapHost ? 'Save a mailbox first.' : undefined}>
                {busy === 'testImap' ? <Loader2 size={16} className="ec-spin" aria-hidden="true" /> : <ServerCog size={16} aria-hidden="true" />}
                {busy === 'testImap' ? 'Testing…' : 'Test Mailbox'}
              </button>
              <span className="ec-actions-note">{imapDirty ? 'Unsaved changes' : ''}</span>
              <button type="button" className="btn btn-primary" onClick={handleSaveImap} disabled={busy !== null || !imapDirty}
                title={imapDirty ? undefined : 'Nothing has changed yet.'}>
                {busy === 'saveImap' ? <Loader2 size={16} className="ec-spin" aria-hidden="true" /> : <Save size={16} aria-hidden="true" />}
                {busy === 'saveImap' ? 'Saving…' : 'Save Receiving Settings'}
              </button>
            </div>
          </section>
        </div>

        <aside className="ec-side">
          {/* ── Domain authentication ───────────────────────────────────────── */}
          <section className="ec-panel ec-side-panel" aria-labelledby={`${id}-dns`} aria-busy={healthLoading}>
            <div className="ec-side-head">
              <h2 id={`${id}-dns`}><ShieldCheck size={16} aria-hidden="true" /> Domain authentication</h2>
              <button type="button" className="ec-icon-btn" onClick={() => void loadHealth()} disabled={healthLoading} aria-label="Check the DNS again">
                <RefreshCw size={15} className={healthLoading ? 'ec-spin' : undefined} aria-hidden="true" />
              </button>
            </div>
            <p className="ec-hint">SPF, DKIM and DMARC let Gmail and Yahoo trust your mail. Checked live in DNS.</p>
            {healthError ? (
              <p className="ec-error-text" role="alert">{healthError}</p>
            ) : !health ? (
              <Skeleton variant="list" count={4} />
            ) : health.length === 0 ? (
              <p className="ec-hint">Add a sender address to check its domain.</p>
            ) : health.map(d => (
              <div className="ec-domain" key={d.domain}>
                <div className="ec-domain-head">
                  <strong>{d.domain}</strong>
                  <StatusBadge type={worstLevel(d.checks)} text={worstLevel(d.checks) === 'success' ? 'All good' : worstLevel(d.checks) === 'error' ? 'Action needed' : 'Improve'} />
                </div>
                <ul className="ec-checks">
                  {d.checks.map(c => (
                    <li key={c.key} className={`is-${c.level}`}>
                      {c.level === 'pass' ? <CheckCircle2 size={15} aria-hidden="true" /> : <AlertTriangle size={15} aria-hidden="true" />}
                      <div>
                        <span className="ec-check-title">{c.title}</span>
                        <span className="ec-check-detail" title={c.detail}>{c.detail}</span>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </section>

          {/* ── Test email ──────────────────────────────────────────────────── */}
          <form className="ec-panel ec-side-panel" aria-labelledby={`${id}-send`} onSubmit={handleSendTest} noValidate>
            <h2 id={`${id}-send`}><Send size={16} aria-hidden="true" /> Send a test email</h2>
            <p className="ec-hint">Sends one real message with the saved settings.</p>
            <div className="ec-inline">
              <label className="sr-only" htmlFor={`${id}-test-to`}>Send the test to</label>
              <input id={`${id}-test-to`} type="email" className="form-control" value={testAddress} autoComplete="email" spellCheck={false}
                placeholder="you@example.com…" aria-invalid={!!errors.testAddress}
                onChange={e => { setTestAddress(e.target.value); setErrors(prev => ({ ...prev, testAddress: undefined })) }} />
              <button type="submit" className="btn btn-secondary" disabled={busy !== null || !connection.configuredAt || smtpDirty}
                title={!connection.configuredAt ? 'Save the sending settings first.' : smtpDirty ? 'Save your changes first; the test uses the saved settings.' : undefined}>
                {busy === 'send' ? <Loader2 size={16} className="ec-spin" aria-hidden="true" /> : <Send size={16} aria-hidden="true" />}
                {busy === 'send' ? 'Sending…' : 'Send'}
              </button>
            </div>
            {errors.testAddress && <p className="ec-error-text" role="alert">{errors.testAddress}</p>}
            <div aria-live="polite">{sendResult && <Result result={sendResult} successTitle="Sent" />}</div>
          </form>

          {/* ── Senders ─────────────────────────────────────────────────────── */}
          <section className="ec-panel ec-side-panel" aria-labelledby={`${id}-senders`}>
            <h2 id={`${id}-senders`}><UserRound size={16} aria-hidden="true" /> Sender addresses</h2>
            {connection.senders.length === 0 ? (
              <p className="ec-hint">No sender addresses yet. Add one from the campaign wizard.</p>
            ) : (
              <ul className="ec-senders">
                {connection.senders.map(s => (
                  <li key={s.id}>
                    <div>
                      <span className="ec-sender-name">{s.displayName}</span>
                      <span className="ec-sender-address">{s.emailAddress}</span>
                    </div>
                    {s.isDefault && <StatusBadge type="info" text="Default" />}
                    {!s.isActive && <StatusBadge type="stale" text="Off" />}
                  </li>
                ))}
              </ul>
            )}
          </section>
        </aside>
      </div>
    </motion.div>
  )
}

interface FieldProps {
  id: string
  label: string
  required?: boolean
  hint?: string | null
  warning?: string
  error?: string
  children: React.ReactNode
}

/** Label, control, then (in priority order) the error, a warning or the hint. */
const Field: React.FC<FieldProps> = ({ id, label, required, hint, warning, error, children }) => (
  <div className="ec-field">
    <label className="form-label" htmlFor={id}>
      {label}{required && <span className="ec-required" aria-hidden="true"> *</span>}
    </label>
    {children}
    {error ? <p id={`${id}-error`} className="ec-error-text" role="alert">{error}</p>
      : warning ? <p className="ec-warning-text">{warning}</p>
      : hint ? <p className="ec-hint">{hint}</p> : null}
  </div>
)

/** The outcome of a test, with the server's own explanation when it failed. */
const Result: React.FC<{ result: EmailProviderTestResult; successTitle: string }> = ({ result, successTitle }) => (
  <div className={`ec-result ${result.success ? 'is-ok' : 'is-error'}`}>
    {result.success ? <CheckCircle2 size={18} aria-hidden="true" /> : <AlertCircle size={18} aria-hidden="true" />}
    <div>
      <strong>{result.success ? successTitle : 'It did not work'}</strong>
      <p>{result.message}</p>
      {result.details && Object.keys(result.details).length > 0 && (
        <dl className="ec-result-details">
          {Object.entries(result.details).map(([key, value]) => (
            <div key={key}><dt>{key}</dt><dd>{value}</dd></div>
          ))}
        </dl>
      )}
    </div>
  </div>
)

export default ConnectEmail
