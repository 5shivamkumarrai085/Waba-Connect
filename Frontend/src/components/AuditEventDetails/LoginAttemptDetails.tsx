import React, { useMemo } from 'react'
import { AlertTriangle, Clock, Globe, Mail, Monitor, ShieldCheck, XCircle } from 'lucide-react'
import { Modal } from '../Modal/Modal'
import { DetailRow, describeUserAgent } from './AuditEventDetails'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import type { LoginErrorModel, LoginSuccessModel } from '../../types/reporting'
import './AuditEventDetails.css'

type LoginAttempt = LoginErrorModel | LoginSuccessModel

interface LoginAttemptDetailsProps {
  attempt: LoginAttempt | null
  onClose: () => void
}

/** A failed attempt carries a reason; a successful one has nothing to explain. */
const isFailure = (attempt: LoginAttempt): attempt is LoginErrorModel =>
  attempt.status === 'Failed'

/**
 * A sign-in attempt, shown in the same right-hand drawer as an audit event.
 *
 * <para>
 * This was a centred `size="md"` dialog listing four label/value rows. The information was right;
 * the presentation was a second, unrelated pattern for the same job — inspecting one row of an
 * activity table — sitting next to the audit drawer on the very same page.
 * </para>
 * <para>
 * It reuses <c>AuditEventDetails</c>'s <see cref="DetailRow"/>, its user-agent parser and its
 * stylesheet outright rather than restating any of it. That is the point: the two drawers cannot
 * drift apart visually, because there is only one set of styles between them.
 * </para>
 */
export const LoginAttemptDetails: React.FC<LoginAttemptDetailsProps> = ({ attempt, onClose }) => {
  const agent = useMemo(() => describeUserAgent(attempt?.userAgent), [attempt?.userAgent])

  const failed = attempt ? isFailure(attempt) : false

  return (
    <Modal
      isOpen={Boolean(attempt)}
      onClose={onClose}
      placement="right"
      size="custom"
      title="Sign-in Attempt"
      subtitle="Authentication outcome, actor, and device context"
      showCloseButton
    >
      {attempt && (
        <div className="audit-detail">
          {/* ── Outcome ─────────────────────────────────────────────────── */}
          <div className="audit-detail-section">
            <h3 className="audit-detail-section-title">
              <ShieldCheck size={12} />
              <span>Outcome</span>
            </h3>

            <div className="audit-detail-list">
              <DetailRow
                icon={failed ? <XCircle size={15} /> : <ShieldCheck size={15} />}
                tone={failed ? 'danger' : 'success'}
                label="Result"
              >
                <span className={`audit-pill audit-pill-result${failed ? ' is-failed' : ''}`}>
                  <span className="audit-pill-dot" />
                  {failed ? 'Failed' : 'Success'}
                </span>
              </DetailRow>

              <DetailRow icon={<Clock size={15} />} tone="blue" label="Timestamp">
                {formatAbsoluteDateTime(attempt.time)}
              </DetailRow>
            </div>
          </div>

          {/* ── Actor ───────────────────────────────────────────────────── */}
          <div className="audit-detail-section">
            <h3 className="audit-detail-section-title">
              <Mail size={12} />
              <span>Actor &amp; Origin</span>
            </h3>

            <div className="audit-field-grid">
              {/* "Email tried", not "Email": on a failed attempt this is the address that was
                  submitted, which may well belong to no account at all. */}
              <DetailRow icon={<Mail size={15} />} tone="neutral" label="Email Tried">
                {attempt.email}
              </DetailRow>

              <DetailRow icon={<Globe size={15} />} tone="blue" label="Client IP">
                <span className="audit-pill audit-pill-mono">
                  <span className="audit-pill-dot" />
                  {attempt.ipAddress || '—'}
                </span>
              </DetailRow>
            </div>
          </div>

          {/* ── Device ──────────────────────────────────────────────────── */}
          <div className="audit-detail-section">
            <h3 className="audit-detail-section-title">
              <Globe size={12} />
              <span>Device &amp; Environment Context</span>
            </h3>

            <div className="audit-field-grid">
              <DetailRow icon={<Globe size={15} />} tone="blue" label="Browser">
                <span className="audit-pill">{agent.browser}</span>
              </DetailRow>

              <DetailRow icon={<Monitor size={15} />} tone="neutral" label="Operating System">
                <span className="audit-pill">{agent.os}</span>
              </DetailRow>
            </div>

            <div className="audit-ua-label">Raw User Agent</div>
            <pre className="audit-ua-box">{attempt.userAgent || '—'}</pre>
          </div>

          {/* Only a failure has something to explain, and it is the reason the row was opened. */}
          {failed && (attempt as LoginErrorModel).reason && (
            <div className="audit-detail-section">
              <h3 className="audit-detail-section-title">
                <AlertTriangle size={12} />
                <span>Why It Failed</span>
              </h3>
              <p className="audit-detail-description">{(attempt as LoginErrorModel).reason}</p>
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}

export default LoginAttemptDetails
