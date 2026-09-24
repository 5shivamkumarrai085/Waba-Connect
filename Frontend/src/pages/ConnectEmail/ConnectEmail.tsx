import React, { useEffect, useMemo, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { CheckCircle2, AlertCircle, Link2, Send, Save, ArrowLeft, Loader2, RefreshCw, Plus, Mail, ServerCog } from 'lucide-react'
import toast from 'react-hot-toast'
import { emailConnectionService } from '../../services/email/emailConnectionService'
import { emailDomainService } from '../../services/email/emailDomainService'
import { CopyField } from '../../components/CopyField/CopyField'
import { getErrorMessage } from '../../utils/errorHelper'
import type {
  EmailAuthMode,
  EmailConnection,
  EmailProviderTestResult,
  EmailProviderType,
  SmtpSecurityMode
} from '../../types/email'
import '../Connections/ConnectNewWabaPage.css'
import './ConnectEmail.css'

/**
 * AWS regions SES is available in.
 *
 * A static list rather than an API call: it changes roughly once a year, listing it needs an AWS
 * credential we do not have yet at this point in the wizard, and a free-text region field is a
 * reliable way to produce a connection that fails with an unhelpful error.
 */
const SES_REGIONS = [
  { value: 'us-east-1', label: 'us-east-1 (US East — N. Virginia)' },
  { value: 'us-east-2', label: 'us-east-2 (US East — Ohio)' },
  { value: 'us-west-2', label: 'us-west-2 (US West — Oregon)' },
  { value: 'eu-west-1', label: 'eu-west-1 (Europe — Ireland)' },
  { value: 'eu-west-2', label: 'eu-west-2 (Europe — London)' },
  { value: 'eu-central-1', label: 'eu-central-1 (Europe — Frankfurt)' },
  { value: 'ap-south-1', label: 'ap-south-1 (Asia Pacific — Mumbai)' },
  { value: 'ap-southeast-1', label: 'ap-southeast-1 (Asia Pacific — Singapore)' },
  { value: 'ap-southeast-2', label: 'ap-southeast-2 (Asia Pacific — Sydney)' },
  { value: 'ap-northeast-1', label: 'ap-northeast-1 (Asia Pacific — Tokyo)' },
  { value: 'ca-central-1', label: 'ca-central-1 (Canada — Central)' },
  { value: 'sa-east-1', label: 'sa-east-1 (South America — São Paulo)' }
]

/**
 * Step 2 of connecting an email sender: provider credentials, a connectivity test, and the
 * domain authentication the send gate requires.
 *
 * Credentials are write-only throughout. The form starts with the secret fields empty even when
 * a secret is stored, and submitting them empty keeps what is stored — which is why the API never
 * needs to return one. The reference design rendered a live key into a readable input; that puts
 * a working credential in front of anyone who can open the page or screenshot it.
 */
export const ConnectEmail: React.FC = () => {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const configurationId = Number(searchParams.get('emailConfigurationId') || 0)

  const [connection, setConnection] = useState<EmailConnection | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  // ── Provider form ──────────────────────────────────────────────────────────────────────────
  const [provider, setProvider] = useState<EmailProviderType>('AmazonSes')
  const [region, setRegion] = useState('')
  const [authMode, setAuthMode] = useState<EmailAuthMode>('AccessKey')
  const [accessKeyId, setAccessKeyId] = useState('')
  const [secretAccessKey, setSecretAccessKey] = useState('')
  const [configurationSet, setConfigurationSet] = useState('')
  const [sendRate, setSendRate] = useState('')

  const [smtpHost, setSmtpHost] = useState('')
  const [smtpPort, setSmtpPort] = useState('')
  const [smtpSecurity, setSmtpSecurity] = useState<SmtpSecurityMode>('StartTls')
  const [smtpUsername, setSmtpUsername] = useState('')
  const [smtpPassword, setSmtpPassword] = useState('')

  // ── IMAP form ──────────────────────────────────────────────────────────────────────────────
  const [imapHost, setImapHost] = useState('')
  const [imapPort, setImapPort] = useState('')
  const [imapSecurity, setImapSecurity] = useState<SmtpSecurityMode>('SslOnConnect')
  const [imapUsername, setImapUsername] = useState('')
  const [imapPassword, setImapPassword] = useState('')

  // ── Actions in flight ──────────────────────────────────────────────────────────────────────
  const [isTesting, setIsTesting] = useState(false)
  const [testResult, setTestResult] = useState<EmailProviderTestResult | null>(null)

  const [testEmailAddress, setTestEmailAddress] = useState('')
  const [isSendingTest, setIsSendingTest] = useState(false)
  const [sendResult, setSendResult] = useState<EmailProviderTestResult | null>(null)

  const [isSaving, setIsSaving] = useState(false)

  const [isSavingImap, setIsSavingImap] = useState(false)
  const [isTestingImap, setIsTestingImap] = useState(false)
  const [imapTestResult, setImapTestResult] = useState<EmailProviderTestResult | null>(null)

  // ── Domain authentication ──────────────────────────────────────────────────────────────────
  const [newDomain, setNewDomain] = useState('')
  const [isProvisioning, setIsProvisioning] = useState(false)
  const [refreshingDomainId, setRefreshingDomainId] = useState<number | null>(null)

  const isSes = provider === 'AmazonSes'

  const load = async () => {
    if (!configurationId) {
      toast.error('No email connection was specified.')
      navigate('/connections')
      return
    }

    const loaded = await emailConnectionService.getConnection(configurationId)
    if (!loaded) {
      toast.error('That email connection could not be found.')
      navigate('/connections')
      return
    }

    setConnection(loaded)
    setProvider(loaded.provider)
    setRegion(loaded.region ?? '')
    setAuthMode(loaded.authMode)
    setAccessKeyId(loaded.accessKeyId ?? '')
    setConfigurationSet(loaded.configurationSet ?? '')
    setSendRate(loaded.maxSendRatePerSecond?.toString() ?? '')
    setSmtpHost(loaded.smtpHost ?? '')
    setSmtpPort(loaded.smtpPort?.toString() ?? '')
    setSmtpSecurity(loaded.smtpSecurity ?? 'StartTls')
    setSmtpUsername(loaded.smtpUsername ?? '')

    // IMAP — host/port/security pre-filled since they are not secrets.
    // Username and password are always left blank: blank means "use SMTP credentials",
    // and filling them would make it look like a separate credential is required.
    setImapHost(loaded.imapHost ?? '')
    setImapPort(loaded.imapPort?.toString() ?? '')
    setImapSecurity(loaded.imapSecurity ?? 'SslOnConnect')
    setImapUsername('')

    // Secret fields are deliberately left blank. The API returns only whether one is stored,
    // and blank-means-keep on save is what makes that possible.
    setSecretAccessKey('')
    setSmtpPassword('')
    setImapPassword('')

    setIsLoading(false)
  }

  useEffect(() => {
    load()
    // Keyed on the configuration id so navigating between connections reloads the form.
  }, [configurationId])

  /** The payload both Save and Test send, so the two can never disagree about the form. */
  const buildPayload = useMemo(
    () => () => ({
      provider,
      region: isSes ? region || undefined : undefined,
      authMode: isSes ? authMode : undefined,
      accessKeyId: isSes && authMode === 'AccessKey' ? accessKeyId.trim() || undefined : undefined,
      secretAccessKey:
        isSes && authMode === 'AccessKey' ? secretAccessKey.trim() || undefined : undefined,
      configurationSet: isSes ? configurationSet.trim() || undefined : undefined,
      smtpHost: !isSes ? smtpHost.trim() || undefined : undefined,
      smtpPort: !isSes && smtpPort ? Number(smtpPort) : undefined,
      smtpSecurity: !isSes ? smtpSecurity : undefined,
      smtpUsername: !isSes ? smtpUsername.trim() || undefined : undefined,
      smtpPassword: !isSes ? smtpPassword.trim() || undefined : undefined,
      maxSendRatePerSecond: sendRate ? Number(sendRate) : undefined,
      isActive: true
    }),
    [
      provider, isSes, region, authMode, accessKeyId, secretAccessKey, configurationSet,
      smtpHost, smtpPort, smtpSecurity, smtpUsername, smtpPassword, sendRate
    ]
  )

  /**
   * Tests the credentials currently in the form, saved or not.
   *
   * Against the unsaved values on purpose: an operator should not have to store a possibly-wrong
   * secret to find out whether it is wrong. Any secret left blank falls back to the stored one,
   * so re-testing after changing only the region does not mean re-typing a key.
   */
  const handleTestConnection = async () => {
    setIsTesting(true)
    setTestResult(null)

    try {
      const result = await emailConnectionService.testUnsavedConnection({
        ...buildPayload(),
        emailConfigurationId: configurationId
      })
      setTestResult(result)
    } catch (err) {
      setTestResult({ success: false, message: getErrorMessage(err, 'The connection test failed.') })
    } finally {
      setIsTesting(false)
    }
  }

  /**
   * The security mode each well-known SMTP port requires.
   *
   * 465 is implicit TLS and 587 is STARTTLS — that is not a convention, it is what the servers
   * on those ports do. Pairing them the other way round does not produce an error: both sides
   * wait for the other to speak first and the connection hangs until it times out.
   *
   * Port 25 is left out deliberately. It is STARTTLS-capable but also the plaintext relay port,
   * so inferring encryption from it would be a guess, and guessing in the direction of "this is
   * encrypted" is the wrong way to be wrong.
   */
  const SECURITY_FOR_PORT: Record<string, SmtpSecurityMode> = {
    '465': 'SslOnConnect',
    '587': 'StartTls',
    '2525': 'StartTls'
  }

  const handlePortChange = (value: string) => {
    setSmtpPort(value)

    // Only moves the mode when the port names one, and never overrides an explicit "None":
    // somebody who turned encryption off meant it, and quietly turning it back on would be a
    // surprise in the opposite direction.
    const implied = SECURITY_FOR_PORT[value.trim()]
    if (implied && smtpSecurity !== 'None') setSmtpSecurity(implied)
  }

  // A mismatch can still be reached by changing the dropdown after the port. Surfaced rather than
  // silently corrected, because the operator just made a deliberate choice — but the server also
  // lets the port win, so this warns instead of claiming the send will fail.
  const portSecurityMismatch = (() => {
    const implied = SECURITY_FOR_PORT[smtpPort.trim()]
    return implied && smtpSecurity !== 'None' && implied !== smtpSecurity ? implied : null
  })()

  const handleSave = async () => {
    setIsSaving(true)
    try {
      const saved = await emailConnectionService.saveProvider(configurationId, buildPayload())
      setConnection(saved)

      // Cleared after a successful save, so a stored secret is never sitting in a DOM input
      // longer than it has to be.
      setSecretAccessKey('')
      setSmtpPassword('')

      toast.success('Provider configuration saved.')
      await load()
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not save the provider configuration.'))
    } finally {
      setIsSaving(false)
    }
  }

  /** Sends one real email. Requires the configuration to be saved, since it sends as it. */
  const handleSendTestEmail = async () => {
    if (!testEmailAddress.trim()) {
      toast.error('Enter an address to send the test to.')
      return
    }

    setIsSendingTest(true)
    setSendResult(null)

    try {
      const result = await emailConnectionService.sendTestEmail(configurationId, {
        toAddress: testEmailAddress.trim()
      })
      setSendResult(result)
    } catch (err) {
      setSendResult({ success: false, message: getErrorMessage(err, 'The test email could not be sent.') })
    } finally {
      setIsSendingTest(false)
    }
  }

  const handleSaveImap = async () => {
    if (!imapHost.trim()) {
      toast.error('Enter an IMAP host before saving.')
      return
    }

    setIsSavingImap(true)
    try {
      const saved = await emailConnectionService.saveImapSettings(configurationId, {
        imapHost: imapHost.trim(),
        imapPort: imapPort ? Number(imapPort) : undefined,
        imapSecurity,
        imapUsername: imapUsername.trim() || undefined,
        imapPassword: imapPassword.trim() || undefined
      })
      setConnection(saved)
      setImapPassword('')
      toast.success('IMAP settings saved. Inbound replies will now be polled.')
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not save IMAP settings.'))
    } finally {
      setIsSavingImap(false)
    }
  }

  const handleTestImap = async () => {
    setIsTestingImap(true)
    setImapTestResult(null)
    try {
      const result = await emailConnectionService.testImapConnection(configurationId)
      setImapTestResult(result)
    } catch (err) {
      setImapTestResult({ success: false, message: getErrorMessage(err, 'IMAP test failed.') })
    } finally {
      setIsTestingImap(false)
    }
  }

  const handleProvisionDomain = async () => {
    if (!newDomain.trim()) {
      toast.error('Enter the domain you send from.')
      return
    }

    setIsProvisioning(true)
    try {
      await emailDomainService.provisionDomain(configurationId, newDomain.trim())
      setNewDomain('')
      toast.success('Domain added. Publish the DNS records below, then refresh.')
      await load()
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not add that domain.'))
    } finally {
      setIsProvisioning(false)
    }
  }

  const handleRefreshDomain = async (domainId: number) => {
    setRefreshingDomainId(domainId)
    try {
      const refreshed = await emailDomainService.refreshDomain(domainId)
      toast.success(
        refreshed.verificationStatus === 'Verified'
          ? `${refreshed.domainName} is verified and ready to send.`
          : `${refreshed.domainName} is ${refreshed.verificationStatus}. DNS changes take time to propagate.`
      )
      await load()
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not refresh the domain status.'))
    } finally {
      setRefreshingDomainId(null)
    }
  }

  if (isLoading) {
    return (
      <motion.div className="waba-wizard-wrapper" {...pageTransitionProps}>
        <div className="waba-wizard-card email-config-loading">
          <Loader2 className="animate-spin" size={28} />
          <p>Loading connection…</p>
        </div>
      </motion.div>
    )
  }

  return (
    <motion.div className="waba-wizard-wrapper wide" {...pageTransitionProps}>
      <div className="waba-wizard-card">
        <div className="waba-wizard-header email">
          <div>
            <h2 className="waba-wizard-title">Connect New Email</h2>
            <p className="waba-wizard-subtitle">
              {connection?.connectionName} — {connection?.defaultFromEmail}
            </p>
          </div>

          <div className="email-config-provider-mark">{isSes ? 'aws' : 'smtp'}</div>
        </div>

        <div className="waba-stepper-bar">
          <div className="waba-step-item">
            <div className="waba-step-badge complete">
              <CheckCircle2 size={16} />
            </div>
            <div className="waba-step-labels">
              <span className="waba-step-title">Basic Details</span>
              <span className="waba-step-sub">{connection?.connectionName}</span>
            </div>
          </div>

          <div className="waba-step-divider" />

          <div className="waba-step-item">
            <div className="waba-step-badge active">2</div>
            <div className="waba-step-labels">
              <span className="waba-step-title">
                {isSes ? 'Amazon SES Configuration' : 'SMTP Configuration'}
              </span>
              <span className="waba-step-sub">Provider credentials</span>
            </div>
          </div>
        </div>

        <div className="waba-wizard-body">
          <section className="email-config-section">
            <div className="email-config-section-head">
              <div>
                <h3 className="waba-wizard-heading">
                  {isSes ? 'Amazon SES Configuration' : 'SMTP Configuration'}
                </h3>
                <p className="waba-wizard-subheading">
                  {isSes
                    ? 'Provide your Amazon SES configuration details.'
                    : 'Provide the SMTP server details for this connection.'}
                </p>
              </div>
            </div>

            <div className="email-field-grid">
              <div className="waba-field-group">
                <label className="waba-field-label">Provider</label>
                <select
                  className="waba-field-input"
                  value={provider}
                  onChange={(e) => {
                    setProvider(e.target.value as EmailProviderType)
                    // A test result belongs to the provider it was run against.
                    setTestResult(null)
                  }}
                >
                  <option value="AmazonSes">Amazon SES</option>
                  <option value="Smtp">SMTP</option>
                </select>
                <p className="waba-field-hint">
                  {isSes
                    ? 'SES reports delivery, bounces and complaints back to this app.'
                    : 'SMTP can send, but reports no delivery or bounce events.'}
                </p>
              </div>

              {isSes ? (
                <div className="waba-field-group">
                  <label className="waba-field-label">
                    AWS Region <span className="waba-field-required">*</span>
                  </label>
                  <select
                    className="waba-field-input"
                    value={region}
                    onChange={(e) => setRegion(e.target.value)}
                  >
                    <option value="">Select a region</option>
                    {SES_REGIONS.map((r) => (
                      <option key={r.value} value={r.value}>{r.label}</option>
                    ))}
                  </select>
                  <p className="waba-field-hint">
                    Must match the region your SES identities are verified in.
                  </p>
                </div>
              ) : (
                <div className="waba-field-group">
                  <label className="waba-field-label">
                    SMTP Host <span className="waba-field-required">*</span>
                  </label>
                  <input
                    type="text"
                    className="waba-field-input"
                    placeholder="e.g. smtp-relay.gmail.com"
                    value={smtpHost}
                    onChange={(e) => setSmtpHost(e.target.value)}
                  />
                </div>
              )}
            </div>

            {isSes ? (
              <>
                <div className="waba-field-group">
                  <label className="waba-field-label">
                    Authentication Method <span className="waba-field-required">*</span>
                  </label>

                  <div className="email-radio-row">
                    <label className={`email-radio ${authMode === 'IamRole' ? 'selected' : ''}`}>
                      <input
                        type="radio"
                        name="auth-mode"
                        checked={authMode === 'IamRole'}
                        onChange={() => setAuthMode('IamRole')}
                      />
                      <span>
                        <strong>IAM Role</strong>
                        {/* Recommended because it stores no credential at all: there is nothing
                            in the database to leak, and nothing to rotate. */}
                        <em>Recommended — no credential is stored</em>
                      </span>
                    </label>

                    <label className={`email-radio ${authMode === 'AccessKey' ? 'selected' : ''}`}>
                      <input
                        type="radio"
                        name="auth-mode"
                        checked={authMode === 'AccessKey'}
                        onChange={() => setAuthMode('AccessKey')}
                      />
                      <span>
                        <strong>Access Key</strong>
                        <em>Access Key ID and Secret Access Key</em>
                      </span>
                    </label>
                  </div>
                </div>

                {authMode === 'AccessKey' && (
                  <div className="email-field-grid">
                    <div className="waba-field-group">
                      <label className="waba-field-label">
                        AWS Access Key ID <span className="waba-field-required">*</span>
                      </label>
                      <input
                        type="text"
                        className="waba-field-input"
                        placeholder="e.g. AKIAIOSFODNN7EXAMPLE"
                        value={accessKeyId}
                        onChange={(e) => setAccessKeyId(e.target.value)}
                        autoComplete="off"
                      />
                    </div>

                    <div className="waba-field-group">
                      <label className="waba-field-label">
                        AWS Secret Access Key{' '}
                        {!connection?.hasSecretAccessKey && <span className="waba-field-required">*</span>}
                      </label>
                      <input
                        type="password"
                        className="waba-field-input"
                        placeholder={
                          connection?.hasSecretAccessKey
                            ? 'Stored — leave blank to keep it'
                            : 'Enter the secret access key'
                        }
                        value={secretAccessKey}
                        onChange={(e) => setSecretAccessKey(e.target.value)}
                        /* new-password stops browsers offering to autofill an unrelated saved
                           credential into a field that writes to a shared configuration. */
                        autoComplete="new-password"
                      />
                      <p className="waba-field-hint">
                        {connection?.hasSecretAccessKey
                          ? 'A key is stored, encrypted. It is never displayed — leave this blank unless you are replacing it.'
                          : 'Stored encrypted and never shown again.'}
                      </p>
                    </div>
                  </div>
                )}

                <div className="email-field-grid">
                  <div className="waba-field-group">
                    <label className="waba-field-label">Configuration Set (Optional)</label>
                    <input
                      type="text"
                      className="waba-field-input"
                      placeholder="e.g. OmniConnect-Email-Tracking"
                      value={configurationSet}
                      onChange={(e) => setConfigurationSet(e.target.value)}
                    />
                    <p className="waba-field-hint">
                      Used for tracking delivery events. Without one, SES sends but reports
                      nothing back.
                    </p>
                  </div>

                  <div className="waba-field-group">
                    <label className="waba-field-label">Default Sending Rate (Optional)</label>
                    <div className="email-input-with-unit">
                      <input
                        type="number"
                        min="0.1"
                        step="0.1"
                        className="waba-field-input"
                        placeholder="e.g. 14"
                        value={sendRate}
                        onChange={(e) => setSendRate(e.target.value)}
                      />
                      <span className="email-input-unit">emails/second</span>
                    </div>
                    <p className="waba-field-hint">
                      Leave empty to use the configured default. Enforced across every running
                      instance.
                    </p>
                  </div>
                </div>
              </>
            ) : (
              <>
                <div className="email-field-grid">
                  <div className="waba-field-group">
                    <label className="waba-field-label">Port</label>
                    <input
                      type="number"
                      className="waba-field-input"
                      placeholder="587"
                      value={smtpPort}
                      onChange={(e) => handlePortChange(e.target.value)}
                    />
                    <p className="waba-field-hint">
                      587 for STARTTLS, 465 for implicit SSL. Security is set to match.
                    </p>
                  </div>

                  <div className="waba-field-group">
                    <label className="waba-field-label">Security</label>
                    <select
                      className="waba-field-input"
                      value={smtpSecurity}
                      onChange={(e) => setSmtpSecurity(e.target.value as SmtpSecurityMode)}
                    >
                      <option value="StartTls">STARTTLS (port 587)</option>
                      <option value="SslOnConnect">SSL on connect (port 465)</option>
                      <option value="None">None — not encrypted</option>
                    </select>
                    {smtpSecurity === 'None' && (
                      <p className="waba-field-hint email-field-warning">
                        Credentials and message content will be sent in clear text.
                      </p>
                    )}
                    {portSecurityMismatch && (
                      <p className="waba-field-hint email-field-warning">
                        Port {smtpPort} expects{' '}
                        {portSecurityMismatch === 'SslOnConnect' ? 'SSL on connect' : 'STARTTLS'}.
                        The server will use that rather than hang on the wrong handshake.
                      </p>
                    )}
                  </div>

                  <div className="waba-field-group">
                    <label className="waba-field-label">Username</label>
                    <input
                      type="text"
                      className="waba-field-input"
                      value={smtpUsername}
                      onChange={(e) => setSmtpUsername(e.target.value)}
                      autoComplete="off"
                    />
                  </div>

                  <div className="waba-field-group">
                    <label className="waba-field-label">Password</label>
                    <input
                      type="password"
                      className="waba-field-input"
                      placeholder={
                        connection?.hasSmtpPassword ? 'Stored — leave blank to keep it' : 'Enter the password'
                      }
                      value={smtpPassword}
                      onChange={(e) => setSmtpPassword(e.target.value)}
                      autoComplete="new-password"
                    />
                  </div>
                </div>

                <div className="waba-field-group">
                  <label className="waba-field-label">Sending Rate (Optional)</label>
                  <div className="email-input-with-unit">
                    <input
                      type="number"
                      min="0.1"
                      step="0.1"
                      className="waba-field-input"
                      placeholder="e.g. 5"
                      value={sendRate}
                      onChange={(e) => setSendRate(e.target.value)}
                    />
                    <span className="email-input-unit">emails/second</span>
                  </div>
                </div>
              </>
            )}

            <div className="email-action-row">
              <button
                type="button"
                className="btn-toolbar"
                onClick={handleTestConnection}
                disabled={isTesting}
              >
                {isTesting ? <Loader2 className="animate-spin" size={14} /> : <Link2 size={14} />}
                {isTesting ? 'Testing…' : 'Test Connection'}
              </button>
            </div>

            {testResult && <ResultBanner result={testResult} />}
          </section>

          {/* Sending a real email requires the saved configuration, so this section follows it. */}
          <section className="email-config-section">
            <div className="email-config-section-head">
              <div>
                <h3 className="waba-wizard-heading">Send Test Email</h3>
                <p className="waba-wizard-subheading">
                  Sends one real email using the saved configuration, to verify end-to-end delivery.
                </p>
              </div>
            </div>

            <div className="email-test-send-row">
              <div className="waba-field-group email-test-send-field">
                <label className="waba-field-label">
                  Test Email Address <span className="waba-field-required">*</span>
                </label>
                <input
                  type="email"
                  className="waba-field-input"
                  placeholder="e.g. yourname@example.com"
                  value={testEmailAddress}
                  onChange={(e) => setTestEmailAddress(e.target.value)}
                />
              </div>

              <button
                type="button"
                className="btn-toolbar"
                onClick={handleSendTestEmail}
                disabled={isSendingTest || !connection?.configuredAt}
                title={
                  connection?.configuredAt
                    ? undefined
                    : 'Save the provider configuration before sending a test email.'
                }
              >
                {isSendingTest ? <Loader2 className="animate-spin" size={14} /> : <Send size={14} />}
                {isSendingTest ? 'Sending…' : 'Send Test Email'}
              </button>
            </div>

            {sendResult && <ResultBanner result={sendResult} />}
          </section>

          {/* IMAP section — only for SMTP connections (SES handles inbound separately). */}
          {!isSes && (
            <section className="email-config-section">
              <div className="email-config-section-head">
                <div>
                  <h3 className="waba-wizard-heading">
                    <Mail size={16} style={{ display: 'inline', marginRight: 6 }} />
                    IMAP — Receive Inbound Replies
                  </h3>
                  <p className="waba-wizard-subheading">
                    Configure your IMAP mailbox so replies sent to this address appear automatically
                    in Chat. Leave username/password blank to reuse the SMTP credentials above.
                  </p>
                </div>
                {connection?.imapHost && (
                  <span className="email-domain-status verified" style={{ alignSelf: 'flex-start' }}>
                    Configured
                  </span>
                )}
              </div>

              <div className="email-field-grid">
                <div className="waba-field-group">
                  <label className="waba-field-label">
                    IMAP Host <span className="waba-field-required">*</span>
                  </label>
                  <input
                    type="text"
                    className="waba-field-input"
                    placeholder="e.g. mail.rma.my"
                    value={imapHost}
                    onChange={(e) => setImapHost(e.target.value)}
                    autoComplete="off"
                  />
                </div>

                <div className="waba-field-group">
                  <label className="waba-field-label">IMAP Port</label>
                  <input
                    type="number"
                    className="waba-field-input"
                    placeholder="993"
                    value={imapPort}
                    onChange={(e) => setImapPort(e.target.value)}
                  />
                  <p className="waba-field-hint">993 for SSL on connect (recommended), 143 for STARTTLS.</p>
                </div>

                <div className="waba-field-group">
                  <label className="waba-field-label">Security</label>
                  <select
                    className="waba-field-input"
                    value={imapSecurity}
                    onChange={(e) => setImapSecurity(e.target.value as SmtpSecurityMode)}
                  >
                    <option value="SslOnConnect">SSL on connect (port 993)</option>
                    <option value="StartTls">STARTTLS (port 143)</option>
                    <option value="None">None — not encrypted</option>
                  </select>
                </div>

                <div className="waba-field-group">
                  <label className="waba-field-label">
                    Username
                    <span className="waba-field-hint" style={{ marginLeft: 6, fontWeight: 400 }}>
                      (defaults to SMTP username)
                    </span>
                  </label>
                  <input
                    type="text"
                    className="waba-field-input"
                    placeholder=""
                    value={imapUsername}
                    onChange={(e) => setImapUsername(e.target.value)}
                    autoComplete="off"
                  />
                </div>

                <div className="waba-field-group">
                  <label className="waba-field-label">
                    Password
                    <span className="waba-field-hint" style={{ marginLeft: 6, fontWeight: 400 }}>
                      (defaults to SMTP password)
                    </span>
                  </label>
                  <input
                    type="password"
                    className="waba-field-input"
                    placeholder=""
                    value={imapPassword}
                    onChange={(e) => setImapPassword(e.target.value)}
                    autoComplete="new-password"
                  />
                  {connection?.hasImapPassword && (
                    <p className="waba-field-hint">A password is stored and encrypted. Leave blank to keep it.</p>
                  )}
                </div>
              </div>

              <div className="email-action-row">
                <button
                  type="button"
                  className="btn-toolbar"
                  onClick={handleTestImap}
                  disabled={isTestingImap || !connection?.imapHost}
                  title={connection?.imapHost ? undefined : 'Save IMAP settings first.'}
                >
                  {isTestingImap ? <Loader2 className="animate-spin" size={14} /> : <ServerCog size={14} />}
                  {isTestingImap ? 'Testing IMAP…' : 'Test IMAP'}
                </button>

                <button
                  type="button"
                  className="btn-wizard-next"
                  onClick={handleSaveImap}
                  disabled={isSavingImap}
                  style={{ marginLeft: 'auto' }}
                >
                  {isSavingImap ? <Loader2 className="animate-spin" size={14} /> : <Save size={14} />}
                  {isSavingImap ? 'Saving…' : 'Save IMAP Settings'}
                </button>
              </div>

              {imapTestResult && <ResultBanner result={imapTestResult} />}
            </section>
          )}

          {/*
            Domain authentication. Not in the reference design, but a campaign cannot send without
            it: mail from an unauthenticated domain is junked by every major provider, so the
            server gates sending on verification. Leaving this out would produce a wizard an
            operator can complete and still not be able to send from.
          */}
          {isSes && (
            <section className="email-config-section">
              <div className="email-config-section-head">
                <div>
                  <h3 className="waba-wizard-heading">Sending Domains (SPF, DKIM, DMARC)</h3>
                  <p className="waba-wizard-subheading">
                    A verified domain is required before this connection can send a campaign.
                  </p>
                </div>
              </div>

              <div className="email-test-send-row">
                <div className="waba-field-group email-test-send-field">
                  <label className="waba-field-label">Domain</label>
                  <input
                    type="text"
                    className="waba-field-input"
                    placeholder="e.g. example.com"
                    value={newDomain}
                    onChange={(e) => setNewDomain(e.target.value)}
                  />
                </div>

                <button
                  type="button"
                  className="btn-toolbar"
                  onClick={handleProvisionDomain}
                  disabled={isProvisioning || !connection?.configuredAt}
                  title={
                    connection?.configuredAt
                      ? undefined
                      : 'Save the provider configuration before adding a domain.'
                  }
                >
                  {isProvisioning ? <Loader2 className="animate-spin" size={14} /> : <Plus size={14} />}
                  Add Domain
                </button>
              </div>

              {connection?.domains.length === 0 && (
                <p className="waba-field-hint">No sending domains yet.</p>
              )}

              {connection?.domains.map((domain) => (
                <div className="email-domain-card" key={domain.id}>
                  <div className="email-domain-head">
                    <div>
                      <strong>{domain.domainName}</strong>
                      <span className={`email-domain-status ${domain.verificationStatus.toLowerCase()}`}>
                        {domain.verificationStatus}
                      </span>
                    </div>

                    <button
                      type="button"
                      className="btn-toolbar-tertiary"
                      onClick={() => handleRefreshDomain(domain.id)}
                      disabled={refreshingDomainId === domain.id}
                    >
                      {refreshingDomainId === domain.id ? (
                        <Loader2 className="animate-spin" size={13} />
                      ) : (
                        <RefreshCw size={13} />
                      )}
                      Refresh
                    </button>
                  </div>

                  {domain.lastCheckMessage && (
                    <p className="waba-field-hint email-field-warning">{domain.lastCheckMessage}</p>
                  )}

                  {domain.verificationStatus !== 'Verified' && domain.requiredDnsRecords.length > 0 && (
                    <>
                      <p className="waba-field-hint">
                        Publish these records in your DNS, then refresh. Propagation can take
                        minutes to hours.
                      </p>

                      <div className="email-dns-table">
                        {domain.requiredDnsRecords.map((record, index) => (
                          <div className="email-dns-row" key={`${record.type}-${record.name}-${index}`}>
                            <span className="email-dns-type">{record.type}</span>
                            <div className="email-dns-fields">
                              <label className="email-dns-label">Name</label>
                              {/* CopyField, because transcribing a DKIM CNAME by hand is where
                                  domain setup usually goes wrong. */}
                              <CopyField value={record.name} readOnly />
                              <label className="email-dns-label">Value</label>
                              <CopyField value={record.value} readOnly />
                            </div>
                            <span className="email-dns-purpose">{record.purpose}</span>
                          </div>
                        ))}
                      </div>
                    </>
                  )}
                </div>
              ))}
            </section>
          )}

          <div className="waba-wizard-footer">
            <button type="button" className="btn-wizard-cancel" onClick={() => navigate('/connections')}>
              <ArrowLeft size={14} />
              Back
            </button>

            <button type="button" className="btn-wizard-next" onClick={handleSave} disabled={isSaving}>
              {isSaving ? <Loader2 className="animate-spin" size={14} /> : <Save size={14} />}
              {isSaving ? 'Saving…' : 'Save Connection'}
            </button>
          </div>
        </div>
      </div>
    </motion.div>
  )
}

/**
 * The success/failure banner under each action.
 *
 * One component for both outcomes, because the failure case is the one that matters: the server's
 * message names what to change ("the secret access key does not match the access key ID"), and
 * that needs to be as prominent as a success.
 */
const ResultBanner: React.FC<{ result: EmailProviderTestResult }> = ({ result }) => (
  <div className={`email-result-banner ${result.success ? 'success' : 'error'}`}>
    {result.success ? <CheckCircle2 size={18} /> : <AlertCircle size={18} />}

    <div>
      <strong>{result.success ? 'Success' : 'Could not connect'}</strong>
      <p>{result.message}</p>

      {result.details && Object.keys(result.details).length > 0 && (
        <dl className="email-result-details">
          {Object.entries(result.details).map(([key, value]) => (
            <div key={key}>
              <dt>{key}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  </div>
)

export default ConnectEmail
