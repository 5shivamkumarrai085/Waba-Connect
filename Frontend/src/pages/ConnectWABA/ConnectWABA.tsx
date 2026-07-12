import React, { useEffect, useState } from 'react'
import { useWabaStore } from '../../store/wabaStore'
// mockConnectionRequirements removed, defining inline
import { CopyField } from '../../components/CopyField/CopyField'
import { PhoneCard } from '../../components/PhoneCard/PhoneCard'
import { HealthCard } from '../../components/HealthCard/HealthCard'
import { InfoCard } from '../../components/InfoCard/InfoCard'
import { ConfirmationModal } from '../../components/Modal/ConfirmationModal'
import toast from 'react-hot-toast'
import { QrCode, HelpCircle, Key, Send, Globe, Link2, RefreshCw, XCircle, Eye, EyeOff } from 'lucide-react'
import './ConnectWABA.css'

export const ConnectWABA: React.FC = () => {
  const [showSetupAccessToken, setShowSetupAccessToken] = useState(false)
  const {
    isConnected,
    isLoading,
    isConnecting,
    isSendingMessage,
    isVerifyingWebhook,

    facebookAppId,
    facebookAppSecret,
    wabaId,
    accessToken,

    webhookUrl,
    verifyToken,

    phoneInfo,
    tokenInfo,
    healthInfo,

    setFacebookAppId,
    setFacebookAppSecret,
    setWabaId,
    setAccessToken,

    loadWabaData,
    connectApp,
    configureWaba,
    disconnectWaba,
    sendTestMessage,
    verifyWebhook,
    refreshHealth
  } = useWabaStore()

  const [testNumber, setTestNumber] = useState<string>('')
  const [isDisconnectModalOpen, setIsDisconnectModalOpen] = useState(false)

  useEffect(() => {
    // Attempt to load existing WABA data on mount to see if we're partially or fully connected
    loadWabaData()
  }, [])

  const handleConnectApp = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!facebookAppId || !facebookAppSecret) {
      toast.error('Facebook App ID and App Secret are required.')
      return
    }
    const res = await connectApp(facebookAppId, facebookAppSecret)
    if (res.success) toast.success(res.message)
    else toast.error(res.message)
  }

  const handleConfigureWaba = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!wabaId || !accessToken) {
      toast.error('WABA ID and Access Token are required.')
      return
    }
    const res = await configureWaba(wabaId, accessToken)
    if (res.success) toast.success(res.message)
    else toast.error(res.message)
  }

  const handleDisconnectClick = () => {
    setIsDisconnectModalOpen(true)
  }

  const confirmDisconnect = async () => {
    setIsDisconnectModalOpen(false)
    const res = await disconnectWaba()
    if (res.success) toast.success(res.message)
    else toast.error(res.message)
  }

  const handleSendTestMessage = async (e: React.FormEvent) => {
    e.preventDefault()
    const res = await sendTestMessage(testNumber)
    if (res.success) {
      toast.success(res.message)
      setTestNumber('')
    } else {
      toast.error(res.message)
    }
  }

  const handleVerifyWebhook = async () => {
    const res = await verifyWebhook()
    if (res.success) {
      toast.success(res.message)
    } else {
      toast.error(res.message)
    }
  }

  const handleRefreshHealth = async () => {
    await refreshHealth()
    toast.success('Health status refreshed!')
  }

  const handleGetQrCode = () => {
    toast.success('QR Code generation triggered successfully! Scan code to initialize chats.')
  }

  if (isLoading && !facebookAppId && !isConnected) {
    return (
      <div className="fade-in page-loader">
        <p className="page-loader-text">Loading WABA account details...</p>
      </div>
    )
  }

  const isStep1Done = !!webhookUrl && !!verifyToken;

  return (
    <div className="fade-in">
      {/* Title Header with Top Action Button triggers */}
      <div className="waba-page-header">
        <div className="waba-page-title">
          <h1>WhatsApp Business Account</h1>
        </div>
        {isConnected && (
          <div className="waba-top-actions">
            <button
              type="button"
              className="btn-top-action btn-qr-code"
              onClick={handleGetQrCode}
            >
              <QrCode size={16} />
              <span>Click to get QR Code</span>
            </button>
            <button
              type="button"
              className="btn-top-action btn-disconnect"
              onClick={handleDisconnectClick}
            >
              <XCircle size={16} />
              <span>Disconnect Account</span>
            </button>
          </div>
        )}
      </div>

      {/* Disconnected Form Setup view */}
      {!isConnected ? (
        <div className="waba-layout-grid">
          {/* Setup steps */}
          <div>
            {/* Step 1 */}
            <div className="waba-setup-card">
              <h3 className="waba-setup-title">Step - 1: Facebook Developer Account & Facebook App</h3>

              <form onSubmit={handleConnectApp}>
                <div className="form-group">
                  <div className="waba-input-label-row">
                    <label className="waba-input-label">
                      <HelpCircle size={14} color="var(--text-light)" />
                      Facebook App ID
                    </label>
                    <span className="waba-help-link" onClick={() => window.open('https://developers.facebook.com/', '_blank')}>Help</span>
                  </div>
                  <input
                    type="text"
                    className="form-control"
                    placeholder="Enter Facebook App ID"
                    value={facebookAppId}
                    onChange={(e) => setFacebookAppId(e.target.value)}
                    required
                  />
                </div>

                <div className="form-group">
                  <div className="waba-input-label-row">
                    <label className="waba-input-label">
                      Facebook App Secret
                    </label>
                  </div>
                  <input
                    type="password"
                    className="form-control"
                    placeholder="Enter Facebook App Secret"
                    value={facebookAppSecret}
                    onChange={(e) => setFacebookAppSecret(e.target.value)}
                    required
                  />
                </div>

                <div className="waba-card-footer">
                  <button
                    type="submit"
                    className="btn-waba-action"
                    disabled={isConnecting}
                  >
                    <Link2 size={16} />
                    <span>{isConnecting ? 'Connecting...' : 'Connect Webhook'}</span>
                  </button>
                </div>
              </form>

              {isStep1Done && (
                <div style={{ marginTop: '20px', borderTop: '1px solid var(--border-light)', paddingTop: '16px' }}>
                  <div className="form-group">
                    <label className="waba-input-label">Callback URL</label>
                    <CopyField value={webhookUrl} isSensitive={true} />
                  </div>
                  <div className="form-group form-group-no-margin">
                    <label className="waba-input-label">Verify Token</label>
                    <CopyField value={verifyToken} isSensitive={true} />
                  </div>
                </div>
              )}
            </div>

            {/* Step 2 */}
            <div className="waba-setup-card" style={{ opacity: isStep1Done ? 1 : 0.5, pointerEvents: isStep1Done ? 'auto' : 'none' }}>
              <h3 className="waba-setup-title">Step - 2: WhatsApp Integration Setup</h3>

              <form onSubmit={handleConfigureWaba}>
                <div className="form-group">
                  <div className="waba-input-label-row">
                    <label className="waba-input-label">
                      <HelpCircle size={14} color="var(--text-light)" />
                      Your WhatsApp Business Account (WABA) ID
                    </label>
                  </div>
                  <input
                    type="text"
                    className="form-control"
                    placeholder="Enter WABA ID"
                    value={wabaId}
                    onChange={(e) => setWabaId(e.target.value)}
                    required
                    disabled={!isStep1Done}
                  />
                </div>

                <div className="form-group">
                  <div className="waba-input-label-row">
                    <label className="waba-input-label">
                      <HelpCircle size={14} color="var(--text-light)" />
                      WhatsApp Access Token
                    </label>
                  </div>
                  <div className="waba-input-with-button">
                    <div style={{ position: 'relative', flex: 1, display: 'flex', alignItems: 'center' }}>
                      <input
                        type={showSetupAccessToken ? "text" : "password"}
                        className="form-control"
                        placeholder="Enter Access Token"
                        value={accessToken}
                        onChange={(e) => setAccessToken(e.target.value)}
                        required
                        disabled={!isStep1Done}
                        style={{ paddingRight: '40px' }}
                      />
                      <button
                        type="button"
                        onClick={() => setShowSetupAccessToken(!showSetupAccessToken)}
                        disabled={!isStep1Done}
                        style={{
                          position: 'absolute',
                          right: '8px',
                          background: 'none',
                          border: 'none',
                          cursor: 'pointer',
                          color: 'var(--text-muted)',
                          display: 'flex',
                          alignItems: 'center'
                        }}
                        title={showSetupAccessToken ? 'Hide Token' : 'Show Token'}
                      >
                        {showSetupAccessToken ? <EyeOff size={16} /> : <Eye size={16} />}
                      </button>
                    </div>
                    <button
                      type="button"
                      className="btn-debug-token"
                      onClick={() => window.open('https://developers.facebook.com/tools/debug/accesstoken/', '_blank')}
                      disabled={!isStep1Done}
                    >
                      <RefreshCw size={12} />
                      <span>Debug Token</span>
                    </button>
                  </div>
                </div>

                <div className="waba-card-footer">
                  <button
                    type="submit"
                    className="btn-waba-action"
                    disabled={isConnecting || !isStep1Done}
                  >
                    <Link2 size={16} />
                    <span>{isConnecting ? 'Connecting...' : 'Configure'}</span>
                  </button>
                </div>
              </form>
            </div>
          </div>

          {/* Setup requirements & status sidebar */}
          <div>
            <div className="waba-requirements-card">
              <h3 className="waba-requirements-title">Connection Requirements</h3>
              <p className="waba-requirements-intro">
                You will require the following information to activate your WhatsApp Business Cloud API:
              </p>

              <div className="waba-requirements-list">
                {[
                  {
                    step: 1,
                    title: 'Facebook Developer Account',
                    description: 'A registered developer account to create and manage Facebook Apps.'
                  },
                  {
                    step: 2,
                    title: 'Facebook App (Business type)',
                    description: 'An app with WhatsApp product added to manage API connections.'
                  },
                  {
                    step: 3,
                    title: 'WhatsApp Business Account (WABA)',
                    description: 'A dedicated business account linked to your Meta Business Manager.'
                  },
                  {
                    step: 4,
                    title: 'System User & Access Token',
                    description: 'A system user with admin permissions and a permanent access token.'
                  },
                  {
                    step: 5,
                    title: 'Verified Phone Number',
                    description: 'A clean phone number not currently registered with regular WhatsApp.'
                  }
                ].map((req) => (
                  <div key={req.step} className="waba-requirement-item">
                    <div className="waba-requirement-step-circle">{req.step}</div>
                    <div className="waba-requirement-content">
                      <span className="waba-requirement-title-text">{req.title}</span>
                      <span className="waba-requirement-desc">{req.description}</span>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <InfoCard title="Need Help?" theme="blue" iconName="help">
              For detailed instructions, visit the{' '}
              <span className="info-card-link" onClick={() => window.open('https://developers.facebook.com/docs/whatsapp/cloud-api', '_blank')}>
                WhatsApp Cloud API Documentation
              </span>
            </InfoCard>

            <InfoCard title="Connection Status" theme="orange" iconName="alert">
              Your WhatsApp Business API is not connected. Complete the steps above to establish a connection.
            </InfoCard>
          </div>
        </div>
      ) : (
        /* Connected Information dashboard layout */
        <div className="waba-layout-grid">
          {/* Connected Details details */}
          <div>
            {/* Access Token Details */}
            <div className="waba-setup-card">
              <div className="waba-card-icon-header">
                <div className="waba-card-icon-box">
                  <Key size={18} />
                </div>
                <h3 className="waba-card-title">Access Token Information</h3>
              </div>

              <div className="form-group">
                <label className="waba-input-label">Access token</label>
                <CopyField value={tokenInfo?.token || ''} isSensitive={true} />
              </div>

              <div className="form-group">
                <label className="waba-input-label">Permission scopes</label>
                <div className="waba-scopes-container">
                  {tokenInfo?.permissions.map((scope) => (
                    <span key={scope} className="waba-scope-badge">
                      {scope}
                    </span>
                  ))}
                </div>
              </div>

              <div className="form-group">
                <label className="waba-input-label margin-bottom-2">Issued at</label>
                <p className="waba-date-text">{tokenInfo?.issuedAt}</p>
              </div>

              <div className="form-group form-group-no-margin">
                <label className="waba-input-label">Webhook URL</label>
                <CopyField value={webhookUrl || tokenInfo?.webhookUrl || ''} isSensitive={true} />
              </div>
            </div>

            {/* Send Test Message */}
            <div className="waba-setup-card">
              <div className="waba-card-icon-header">
                <div className="waba-card-icon-box green-theme">
                  <Send size={18} />
                </div>
                <h3 className="waba-card-title">Send Test Message</h3>
              </div>

              <form onSubmit={handleSendTestMessage}>
                <div className="form-group">
                  <label className="waba-input-label">
                    <HelpCircle size={14} color="var(--text-light)" />
                    WhatsApp number
                  </label>
                  <input
                    type="text"
                    className="form-control"
                    placeholder="Enter WhatsApp number with country code"
                    value={testNumber}
                    onChange={(e) => setTestNumber(e.target.value)}
                    required
                  />
                </div>

                <div className="waba-card-footer">
                  <button
                    type="submit"
                    className="btn-waba-action"
                    disabled={isSendingMessage}
                  >
                    <Send size={14} />
                    <span>{isSendingMessage ? 'Sending...' : 'Send Message'}</span>
                  </button>
                </div>
              </form>
            </div>

            {/* Verify Webhook */}
            <div className="waba-setup-card">
              <div className="waba-card-icon-header">
                <div className="waba-card-icon-box purple-theme">
                  <Globe size={18} />
                </div>
                <h3 className="waba-card-title">Verify Webhook</h3>
              </div>

              <button
                type="button"
                className="purple-btn-webhook-verify"
                onClick={handleVerifyWebhook}
                disabled={isVerifyingWebhook}
              >
                <span>{isVerifyingWebhook ? 'Verifying...' : 'Verify Webhook'}</span>
              </button>
            </div>
          </div>

          {/* Connected Phone & Health Cards details */}
          <div>
            <PhoneCard phoneInfo={phoneInfo} />
            <div className="waba-cards-grid margin-top-20">
              <HealthCard healthInfo={healthInfo} onRefresh={handleRefreshHealth} />
            </div>
          </div>
        </div>
      )}

      <ConfirmationModal
        isOpen={isDisconnectModalOpen}
        title="Disconnect Account"
        message="Are You Sure You Want to Disconnect?"
        confirmText="Disconnect"
        isDestructive={true}
        showWarningIcon={true}
        onConfirm={confirmDisconnect}
        onCancel={() => setIsDisconnectModalOpen(false)}
      />
    </div>
  )
}
export default ConnectWABA
