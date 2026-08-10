import React, { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { motion } from 'framer-motion'
import toast from 'react-hot-toast'
import { UserCircle, ShieldCheck, Upload, Info } from 'lucide-react'
import PermissionMatrix from '../../../components/PermissionMatrix/PermissionMatrix'
import { setupService } from '../../../services/setup/setupService'
import { getErrorMessage } from '../../../utils/errorHelper'
import { pageTransitionProps } from '../../../utils/motion'
import type { PermissionCatalog, SetupRoleListItem, SetupUserPayload } from '../../../types/setup'
import { resolveMediaUrl } from '../../../utils/mediaUrl'

const emptyForm: SetupUserPayload = {
  firstName: '',
  lastName: '',
  email: '',
  phoneNumber: '',
  dialCode: '+91',
  profileImageUrl: '',
  defaultLanguageCode: '',
  password: '',
  confirmPassword: '',
  isActive: true,
  isVerified: false,
  sendWelcomeEmail: false,
  isAdministrator: false,
  roleId: null,
  usesCustomPermissions: false,
  permissionKeys: []
}

export const UserForm: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const userId = id ? parseInt(id, 10) : null
  const isEditMode = userId !== null

  const [form, setForm] = useState<SetupUserPayload>(emptyForm)
  const [catalog, setCatalog] = useState<PermissionCatalog | null>(null)
  const [roles, setRoles] = useState<SetupRoleListItem[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  /** Role presets, so switching roles can refill the matrix without another round-trip. */
  const rolePresets = useRef<Map<number, string[]>>(new Map())

  useEffect(() => {
    const load = async () => {
      setIsLoading(true)
      try {
        const [catalogData, rolesData] = await Promise.all([
          setupService.getPermissionCatalog(),
          setupService.getRoles()
        ])
        setCatalog(catalogData)
        setRoles(rolesData)

        if (isEditMode && userId) {
          const user = await setupService.getUser(userId)
          setForm({
            firstName: user.firstName,
            lastName: user.lastName ?? '',
            email: user.email,
            phoneNumber: user.phoneNumber ?? '',
            dialCode: user.dialCode ?? '+91',
            profileImageUrl: user.profileImageUrl ?? '',
            defaultLanguageCode: user.defaultLanguageCode ?? '',
            // Blank means "keep the existing password" on update.
            password: '',
            confirmPassword: '',
            isActive: user.isActive,
            isVerified: user.isVerified,
            sendWelcomeEmail: user.sendWelcomeEmail,
            isAdministrator: user.isAdministrator,
            roleId: user.roleId ?? null,
            usesCustomPermissions: user.usesCustomPermissions,
            permissionKeys: user.permissionKeys
          })
        }
      } catch (error) {
        toast.error(getErrorMessage(error, 'Failed to load the user form.'))
        // Same reasoning as the role form: never leave a blank edit form for a record that
        // couldn't be loaded.
        if (isEditMode) navigate('/setup/users', { replace: true })
      } finally {
        setIsLoading(false)
      }
    }
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userId])

  const update = <K extends keyof SetupUserPayload>(key: K, value: SetupUserPayload[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }))

  /**
   * Switching role refills the matrix from that role's grants and drops back to role-driven
   * permissions — otherwise the previous role's checkboxes linger and silently become the
   * user's custom set.
   */
  const handleRoleChange = async (nextRoleId: number | null) => {
    setForm((prev) => ({ ...prev, roleId: nextRoleId, usesCustomPermissions: false }))
    if (nextRoleId === null) {
      update('permissionKeys', [])
      return
    }

    const cached = rolePresets.current.get(nextRoleId)
    if (cached) {
      update('permissionKeys', cached)
      return
    }

    try {
      const role = await setupService.getRole(nextRoleId)
      rolePresets.current.set(nextRoleId, role.permissionKeys)
      setForm((prev) => ({ ...prev, permissionKeys: role.permissionKeys }))
    } catch {
      // Non-fatal: the matrix simply stays as it was, and the role still saves correctly.
    }
  }

  /**
   * Any manual edit means these are no longer the role's grants. Flipping the flag here (rather
   * than diffing on save) keeps the meaning explicit: what's checked is what the user gets.
   */
  const handlePermissionsChange = (next: string[]) =>
    setForm((prev) => ({ ...prev, permissionKeys: next, usesCustomPermissions: true }))

  const handleAvatarUpload = async (file: File) => {
    try {
      const url = await setupService.uploadAvatar(file)
      update('profileImageUrl', url)
      toast.success('Profile image uploaded.')
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to upload the image.'))
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSubmitting(true)

    try {
      const payload: SetupUserPayload = {
        ...form,
        lastName: form.lastName || undefined,
        phoneNumber: form.phoneNumber || undefined,
        profileImageUrl: form.profileImageUrl || undefined,
        defaultLanguageCode: form.defaultLanguageCode || undefined,
        roleId: form.isAdministrator ? null : form.roleId,
        password: form.password || undefined,
        confirmPassword: form.confirmPassword || undefined
      }

      if (isEditMode && userId) {
        await setupService.updateUser(userId, payload)
        toast.success('User updated successfully.')
      } else {
        await setupService.createUser(payload)
        toast.success('User created successfully.')
      }
      navigate('/setup/users')
    } catch (error) {
      toast.error(getErrorMessage(error, 'Failed to save the user.'))
    } finally {
      setIsSubmitting(false)
    }
  }

  if (isLoading) {
    return <div className="setup-form-loading">Loading…</div>
  }

  return (
    <motion.div {...pageTransitionProps}>
      <form className="setup-form" onSubmit={handleSubmit}>
        <div className="setup-form-grid">
          {/* ── Personal information ─────────────────────────────────── */}
          <section className="setup-panel">
            <header className="setup-panel-head">
              <UserCircle size={18} />
              <h2>Personal Information</h2>
            </header>

            <div className="setup-panel-body">
              <div className="setup-field">
                <label className="setup-label">Profile Image</label>
                <div className="setup-avatar-row">
                  <div className="setup-avatar-preview">
                    {form.profileImageUrl
                      ? <img src={resolveMediaUrl(form.profileImageUrl)} alt="" />
                      : <UserCircle size={34} />}
                  </div>
                  <button
                    type="button"
                    className="btn-toolbar-secondary"
                    onClick={() => fileInputRef.current?.click()}
                  >
                    <Upload size={14} />
                    <span>Change</span>
                  </button>
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept="image/*"
                    hidden
                    onChange={(e) => {
                      const file = e.target.files?.[0]
                      if (file) void handleAvatarUpload(file)
                      e.target.value = ''
                    }}
                  />
                </div>
              </div>

              <div className="setup-field-row">
                <div className="setup-field">
                  <label className="setup-label required" htmlFor="firstName">First Name</label>
                  <input
                    id="firstName"
                    className="setup-input"
                    value={form.firstName}
                    onChange={(e) => update('firstName', e.target.value)}
                    placeholder="Enter first name"
                    required
                  />
                </div>
                <div className="setup-field">
                  <label className="setup-label" htmlFor="lastName">Last Name</label>
                  <input
                    id="lastName"
                    className="setup-input"
                    value={form.lastName ?? ''}
                    onChange={(e) => update('lastName', e.target.value)}
                    placeholder="Enter last name"
                  />
                </div>
              </div>

              <div className="setup-field">
                <label className="setup-label required" htmlFor="email">Email</label>
                <input
                  id="email"
                  type="email"
                  className="setup-input"
                  value={form.email}
                  onChange={(e) => update('email', e.target.value)}
                  placeholder="Enter email address"
                  required
                />
              </div>

              <div className="setup-field-row">
                <div className="setup-field setup-field-narrow">
                  <label className="setup-label" htmlFor="dialCode">Code</label>
                  <input
                    id="dialCode"
                    className="setup-input"
                    value={form.dialCode ?? ''}
                    onChange={(e) => update('dialCode', e.target.value)}
                    placeholder="+91"
                  />
                </div>
                <div className="setup-field">
                  <label className="setup-label" htmlFor="phone">Phone</label>
                  <input
                    id="phone"
                    className="setup-input"
                    value={form.phoneNumber ?? ''}
                    onChange={(e) => update('phoneNumber', e.target.value)}
                    placeholder="Enter phone number"
                  />
                </div>
              </div>

              <div className="setup-field">
                <label className="setup-label" htmlFor="password">
                  {isEditMode ? 'New Password' : 'Password'}
                </label>
                <input
                  id="password"
                  type="password"
                  className="setup-input"
                  value={form.password ?? ''}
                  onChange={(e) => update('password', e.target.value)}
                  placeholder={isEditMode ? 'Leave blank to keep current password' : 'Enter password'}
                  autoComplete="new-password"
                  required={!isEditMode}
                />
                {isEditMode && (
                  <p className="setup-hint">
                    Setting a password here forces the user to choose a new one at next sign-in.
                  </p>
                )}
              </div>

              <div className="setup-field">
                <label className="setup-label" htmlFor="confirmPassword">Confirm Password</label>
                <input
                  id="confirmPassword"
                  type="password"
                  className="setup-input"
                  value={form.confirmPassword ?? ''}
                  onChange={(e) => update('confirmPassword', e.target.value)}
                  placeholder="Re-enter password"
                  autoComplete="new-password"
                  required={!isEditMode}
                />
              </div>

              <div className="setup-toggle-row">
                <div className="setup-toggle-item">
                  <div>
                    <span className="setup-toggle-label">Active</span>
                    <p className="setup-hint">Inactive users cannot sign in.</p>
                  </div>
                  <button
                    type="button"
                    className={`setup-switch ${form.isActive ? 'on' : 'off'}`}
                    onClick={() => update('isActive', !form.isActive)}
                    aria-pressed={form.isActive}
                    aria-label="Active"
                  >
                    <span className="setup-switch-knob" />
                  </button>
                </div>

                <div className="setup-toggle-item">
                  <div>
                    <span className="setup-toggle-label">Is Verified User</span>
                    <p className="setup-hint">Marks the email address as verified.</p>
                  </div>
                  <button
                    type="button"
                    className={`setup-switch ${form.isVerified ? 'on' : 'off'}`}
                    onClick={() => update('isVerified', !form.isVerified)}
                    aria-pressed={form.isVerified}
                    aria-label="Is verified user"
                  >
                    <span className="setup-switch-knob" />
                  </button>
                </div>

                <div className="setup-toggle-item">
                  <div>
                    <span className="setup-toggle-label">Send Welcome Email</span>
                    {/* Stated plainly: this build ships template management only, with no
                        SMTP configured, so nothing is actually sent. */}
                    <p className="setup-hint">Recorded only — no email is sent (SMTP is not configured).</p>
                  </div>
                  <button
                    type="button"
                    className={`setup-switch ${form.sendWelcomeEmail ? 'on' : 'off'}`}
                    onClick={() => update('sendWelcomeEmail', !form.sendWelcomeEmail)}
                    aria-pressed={form.sendWelcomeEmail}
                    aria-label="Send welcome email"
                  >
                    <span className="setup-switch-knob" />
                  </button>
                </div>
              </div>
            </div>
          </section>

          {/* ── Roles & permissions ──────────────────────────────────── */}
          <section className="setup-panel">
            <header className="setup-panel-head">
              <ShieldCheck size={18} />
              <h2>Roles &amp; Permissions</h2>
            </header>

            <div className="setup-panel-body">
              <div className="setup-toggle-item setup-admin-toggle">
                <div>
                  <span className="setup-toggle-label">Administrator Access</span>
                  <p className="setup-hint">
                    Administrator users have unrestricted access to all features and functions.
                  </p>
                </div>
                <button
                  type="button"
                  className={`setup-switch ${form.isAdministrator ? 'on' : 'off'}`}
                  onClick={() => update('isAdministrator', !form.isAdministrator)}
                  aria-pressed={form.isAdministrator}
                  aria-label="Administrator access"
                >
                  <span className="setup-switch-knob" />
                </button>
              </div>

              {form.isAdministrator ? (
                <div className="setup-info-box">
                  <Info size={16} />
                  <div>
                    <strong>Administrator Information</strong>
                    <p>
                      Administrators have full access to all features and settings of the system.
                      Permissions added in future updates apply to them automatically.
                    </p>
                  </div>
                </div>
              ) : (
                <>
                  <div className="setup-field">
                    <label className="setup-label required" htmlFor="roleId">Role</label>
                    <select
                      id="roleId"
                      className="setup-input"
                      value={form.roleId ?? ''}
                      onChange={(e) => handleRoleChange(e.target.value ? Number(e.target.value) : null)}
                    >
                      <option value="">Select Role</option>
                      {roles.map((role) => (
                        <option key={role.id} value={role.id}>{role.name}</option>
                      ))}
                    </select>
                    {form.usesCustomPermissions && (
                      <p className="setup-hint setup-hint-warning">
                        Permissions have been customised for this user and no longer follow the role.
                      </p>
                    )}
                  </div>

                  <PermissionMatrix
                    catalog={catalog}
                    value={form.permissionKeys}
                    onChange={handlePermissionsChange}
                  />
                </>
              )}
            </div>
          </section>
        </div>

        <div className="setup-form-actions">
          <button type="button" className="btn-toolbar-tertiary" onClick={() => navigate('/setup/users')}>
            Cancel
          </button>
          <button type="submit" className="btn-toolbar-primary" disabled={isSubmitting}>
            {isSubmitting ? 'Saving…' : isEditMode ? 'Save Changes' : 'Add'}
          </button>
        </div>
      </form>
    </motion.div>
  )
}

export default UserForm
