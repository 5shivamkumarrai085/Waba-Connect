import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import toast from 'react-hot-toast'
import { CalendarClock, Loader2, MoreVertical, Pencil, Play, Plus, Trash2 } from 'lucide-react'
import { Menu, MenuItem } from '../Menu/Menu'
import { Modal } from '../Modal/Modal'
import { ConfirmationModal } from '../Modal/ConfirmationModal'
import { EmptyState } from '../EmptyState/EmptyState'
import { Skeleton } from '../Skeleton'
import { StatusBadge } from '../StatusBadge/StatusBadge'
import { Toggle } from '../Toggle/Toggle'
import { SearchableSelect } from '../SearchableSelect/SearchableSelect'
import usePermission from '../../hooks/usePermission'
import useReference from '../../hooks/useReference'
import { reportingService } from '../../services/reportingService'
import { referenceService, labelOf, type TimeZoneOption } from '../../services/referenceService'
import {
  reportScheduleService,
  type ReportSchedule,
  type SaveReportScheduleInput,
  type ScheduleSender
} from '../../services/reportScheduleService'
import type { SavedReport } from '../../types/reporting'
import { getErrorMessage } from '../../utils/errorHelper'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import './ScheduledReports.css'

const FORM_ID = 'report-schedule-form'

/** Weekday names in the viewer's language, Sunday = 0 (the server's numbering). */
const WEEKDAYS = (() => {
  const format = new Intl.DateTimeFormat(undefined, { weekday: 'long' })
  // 2023-01-01 was a Sunday.
  return Array.from({ length: 7 }, (_, i) => format.format(new Date(Date.UTC(2023, 0, 1 + i, 12))))
})()

const ordinal = (n: number) => {
  const rules = new Intl.PluralRules(undefined, { type: 'ordinal' })
  const suffix: Record<string, string> = { one: 'st', two: 'nd', few: 'rd', other: 'th' }
  return `${n}${suffix[rules.select(n)] ?? 'th'}`
}

const parseRecipients = (text: string) => text.split(/[\s,;]+/).map(r => r.trim()).filter(Boolean)
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Saved reports emailed on a timetable, under Reporting. */
export const ScheduledReports: React.FC = () => {
  const { has } = usePermission()
  const canSchedule = has('Reporting.Schedule')
  const options = useReference(referenceService.getReportScheduleOptions, 'report-schedule-options')

  const [schedules, setSchedules] = useState<ReportSchedule[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reports, setReports] = useState<SavedReport[]>([])
  const [senders, setSenders] = useState<ScheduleSender[]>([])
  const [zones, setZones] = useState<TimeZoneOption[]>([])
  const [menuId, setMenuId] = useState<number | null>(null)
  const [editing, setEditing] = useState<ReportSchedule | null>(null)
  const [open, setOpen] = useState(false)
  const [opening, setOpening] = useState(false)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [running, setRunning] = useState<number | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<ReportSchedule | null>(null)
  const formErrorRef = useRef<HTMLDivElement>(null)

  const [form, setForm] = useState<SaveReportScheduleInput | null>(null)
  const [recipientsText, setRecipientsText] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setLoadError(null)
    try {
      setSchedules(await reportScheduleService.list())
    } catch (error) {
      setLoadError(getErrorMessage(error, 'Scheduled reports could not be loaded.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void load() }, [load])

  useEffect(() => {
    if (formError) formErrorRef.current?.focus()
  }, [formError])

  const describe = useCallback((s: ReportSchedule) => {
    const when = s.frequency === 'Daily' ? 'Every day'
      : s.frequency === 'Weekly' ? `Every ${WEEKDAYS[s.dayOfWeek ?? 0]}`
      : `Monthly on the ${ordinal(s.dayOfMonth ?? 1)}`
    return `${when} at ${s.timeOfDay}`
  }, [])

  const openForm = async (schedule: ReportSchedule | null) => {
    const o = options.data
    if (!o) return
    setOpening(true)
    try {
      const [r, s, z, localZone] = await Promise.all([
        reportingService.getSavedReports(),
        reportScheduleService.senders(),
        referenceService.getTimeZones(),
        referenceService.resolveBrowserTimeZone()
      ])
      setReports(r)
      setSenders(s)
      setZones(z)
      setEditing(schedule)
      setFormError(null)
      setForm(schedule ? {
        reportDefinitionId: schedule.reportDefinitionId, frequency: schedule.frequency, timeOfDay: schedule.timeOfDay,
        dayOfWeek: schedule.dayOfWeek ?? o.defaultDayOfWeek, dayOfMonth: schedule.dayOfMonth ?? 1, timeZone: schedule.timeZone,
        recipients: schedule.recipients, format: schedule.format, lookbackDays: schedule.lookbackDays,
        senderIdentityId: schedule.senderIdentityId, isActive: schedule.isActive
      } : {
        reportDefinitionId: r[0]?.id ?? 0,
        frequency: (o.frequencies.find(f => f.value === 'Weekly') ?? o.frequencies[0]).value as SaveReportScheduleInput['frequency'],
        timeOfDay: o.defaultTimeOfDay,
        dayOfWeek: o.defaultDayOfWeek,
        dayOfMonth: 1,
        timeZone: localZone ?? z[0]?.value ?? 'UTC',
        recipients: [],
        format: o.formats[0].value as SaveReportScheduleInput['format'],
        lookbackDays: o.lookbackDays.default,
        senderIdentityId: s[0]?.id ?? 0,
        isActive: true
      })
      setRecipientsText(schedule ? schedule.recipients.join(', ') : '')
      setOpen(true)
    } catch (error) {
      toast.error(getErrorMessage(error, 'The schedule form could not be opened.'))
    } finally {
      setOpening(false)
    }
  }

  const recipients = useMemo(() => parseRecipients(recipientsText), [recipientsText])
  const invalidRecipient = recipients.find(r => !EMAIL.test(r))
  const maxRecipients = options.data?.maxRecipients ?? 0
  const tooMany = maxRecipients > 0 && recipients.length > maxRecipients

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!form) return
    setSaving(true)
    setFormError(null)
    try {
      const payload = { ...form, recipients }
      if (editing) await reportScheduleService.update(editing.id, payload)
      else await reportScheduleService.create(payload)
      toast.success(editing ? 'Schedule saved.' : 'Report scheduled.')
      setOpen(false)
      void load()
    } catch (error) {
      setFormError(getErrorMessage(error, 'The schedule could not be saved.'))
    } finally {
      setSaving(false)
    }
  }

  const runNow = async (s: ReportSchedule) => {
    setRunning(s.id)
    setAnnouncement(`Sending ${s.reportName}…`)
    try {
      const result = await reportScheduleService.runNow(s.id)
      const message = result.message || (result.success ? 'Report sent.' : 'The report could not be sent.')
      setAnnouncement(message)
      if (result.success) toast.success(message)
      else toast.error(message)
      void load()
    } catch (error) {
      const message = getErrorMessage(error, 'The report could not be sent.')
      setAnnouncement(message)
      toast.error(message)
    } finally {
      setRunning(null)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    try {
      await reportScheduleService.remove(deleteTarget.id)
      toast.success(`Stopped emailing “${deleteTarget.reportName}”.`)
      void load()
    } catch (error) {
      toast.error(getErrorMessage(error, 'The schedule could not be deleted.'))
    } finally {
      setDeleteTarget(null)
    }
  }

  const set = <K extends keyof SaveReportScheduleInput>(key: K, value: SaveReportScheduleInput[K]) =>
    setForm(f => (f ? { ...f, [key]: value } : f))

  const lastRun = (s: ReportSchedule) => {
    if (!s.lastRunAt) return <span className="scheduled-muted">Never</span>
    return (
      <span className="scheduled-last-run" title={s.lastError ?? undefined}>
        <StatusBadge type={s.lastStatus === 'Sent' ? 'success' : 'error'} text={s.lastStatus === 'Sent' ? 'Sent' : 'Failed'} />
        <time dateTime={s.lastRunAt}>{formatAbsoluteDateTime(s.lastRunAt)}</time>
      </span>
    )
  }

  const canSubmit = !!form && !saving && form.reportDefinitionId > 0 && form.senderIdentityId > 0
    && recipients.length > 0 && !invalidRecipient && !tooMany

  const body = () => {
    if (loading) return <Skeleton variant="table" />
    if (loadError) {
      return <EmptyState iconName="AlertCircle" title="Scheduled reports could not be loaded" message={loadError}
        action={{ label: 'Try Again', onClick: () => void load() }} />
    }
    if (schedules.length === 0) {
      return <EmptyState iconName="Clock" title="No scheduled reports"
        message="Save a report above, then schedule it to arrive in people's inboxes daily, weekly or monthly."
        action={canSchedule && options.data ? { label: 'Schedule a Report', onClick: () => void openForm(null) } : undefined} />
    }
    return (
      <div className="report-table-scroll">
        <table className="report-table">
          <thead>
            <tr>
              <th className="actions-col" scope="col">Actions</th>
              <th scope="col">Report</th>
              <th scope="col">When</th>
              <th scope="col">Recipients</th>
              <th scope="col">Format</th>
              <th scope="col">Next Run</th>
              <th scope="col">Last Run</th>
            </tr>
          </thead>
          <tbody>
            {schedules.map(s => (
              <tr key={s.id}>
                <td className="actions-col">
                  <div className="report-row-actions">
                    {canSchedule && (
                      <button type="button" className="report-icon-btn" onClick={() => runNow(s)} disabled={running === s.id}
                        title="Send now" aria-label={`Send ${s.reportName} now`}>
                        {running === s.id ? <Loader2 size={13} className="scheduled-spin" aria-hidden="true" /> : <Play size={13} aria-hidden="true" />}
                      </button>
                    )}
                    {canSchedule && (
                      <Menu
                        open={menuId === s.id}
                        onOpenChange={(o) => setMenuId(o ? s.id : null)}
                        align="start"
                        offset={4}
                        ariaLabel={`Actions for ${s.reportName}`}
                        trigger={(props) => (
                          <button {...props} type="button" className="report-icon-btn" aria-label={`More actions for ${s.reportName}`}>
                            <MoreVertical size={14} aria-hidden="true" />
                          </button>
                        )}
                      >
                        <MenuItem onSelect={() => openForm(s)}><Pencil size={13} aria-hidden="true" /> <span>Edit</span></MenuItem>
                        <MenuItem destructive onSelect={() => setDeleteTarget(s)}><Trash2 size={13} aria-hidden="true" /> <span>Delete</span></MenuItem>
                      </Menu>
                    )}
                  </div>
                </td>
                <td>
                  <div className="report-name-cell">
                    <span className="report-name">{s.reportName}</span>
                    {s.ownerName && <span className="report-name-desc">By {s.ownerName}</span>}
                  </div>
                </td>
                <td>
                  <div className="report-name-cell">
                    <span>{describe(s)}</span>
                    <span className="report-name-desc" translate="no">{s.timeZone}</span>
                  </div>
                </td>
                <td className="scheduled-recipients" title={s.recipients.join(', ')}>
                  {s.recipients.length === 1 ? s.recipients[0] : `${s.recipients.length} recipients`}
                </td>
                <td>{labelOf(options.data?.formats, s.format)}</td>
                <td>
                  {s.isActive
                    ? <time dateTime={s.nextRunAt}>{formatAbsoluteDateTime(s.nextRunAt)}</time>
                    : <StatusBadge type="closed" text="Paused" />}
                </td>
                <td>{lastRun(s)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }

  return (
    <section className="reporting-section-card" aria-labelledby="scheduled-reports-title">
      <div className="reporting-section-header scheduled-reports-header">
        <div>
          <h2 className="reporting-section-title" id="scheduled-reports-title">Scheduled Reports</h2>
          <p className="reporting-section-subtitle">Saved reports emailed as a file on a timetable. Each run covers the days before it.</p>
        </div>
        {canSchedule && schedules.length > 0 && (
          <button type="button" className="btn-toolbar-primary" onClick={() => openForm(null)} disabled={!options.data || opening}>
            {opening ? <Loader2 size={15} className="scheduled-spin" aria-hidden="true" /> : <Plus size={15} aria-hidden="true" />}
            <span>Schedule a Report</span>
          </button>
        )}
      </div>

      <div className="sr-only" aria-live="polite">{announcement}</div>
      {body()}

      <Modal
        isOpen={open}
        onClose={() => setOpen(false)}
        title={editing ? 'Edit Scheduled Report' : 'Schedule a Report'}
        icon={<CalendarClock size={18} />}
        subtitle="It runs with your access: only the connections you can see are included."
        placement="right"
        size="md"
        footer={
          <button type="submit" form={FORM_ID} className="oc-dialog-btn oc-dialog-btn-primary" disabled={!canSubmit}>
            {saving && <Loader2 size={15} className="scheduled-spin" aria-hidden="true" />}
            {saving ? 'Saving…' : editing ? 'Save Changes' : 'Create Schedule'}
          </button>
        }
      >
        {form && options.data && (
          <form id={FORM_ID} onSubmit={submit} className="setup-modal-form" noValidate>
            {formError && <div ref={formErrorRef} tabIndex={-1} role="alert" className="scheduled-form-error">{formError}</div>}

            <SearchableSelect
              id="sched-report"
              label="Saved report"
              value={String(form.reportDefinitionId || '')}
              options={reports.map(r => ({ value: String(r.id), label: r.name }))}
              onChange={v => set('reportDefinitionId', Number(v))}
              hideAllOption
              placeholder="Choose a report…"
              emptyMessage="Save a report first, then schedule it."
            />

            <div className="setup-field-row scheduled-row">
              <div className="setup-field">
                <label className="setup-label" htmlFor="sched-frequency">Frequency</label>
                <select id="sched-frequency" name="frequency" className="setup-input" value={form.frequency}
                  onChange={e => set('frequency', e.target.value as SaveReportScheduleInput['frequency'])}>
                  {options.data.frequencies.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
                </select>
              </div>
              {form.frequency === 'Weekly' && (
                <div className="setup-field">
                  <label className="setup-label" htmlFor="sched-dow">Day</label>
                  <select id="sched-dow" name="dayOfWeek" className="setup-input" value={form.dayOfWeek ?? options.data.defaultDayOfWeek}
                    onChange={e => set('dayOfWeek', Number(e.target.value))}>
                    {WEEKDAYS.map((d, i) => <option key={d} value={i}>{d}</option>)}
                  </select>
                </div>
              )}
              {form.frequency === 'Monthly' && (
                <div className="setup-field">
                  <label className="setup-label" htmlFor="sched-dom">Day of month</label>
                  <select id="sched-dom" name="dayOfMonth" className="setup-input" value={form.dayOfMonth ?? 1}
                    onChange={e => set('dayOfMonth', Number(e.target.value))}>
                    {Array.from({ length: options.data.maxDayOfMonth }, (_, i) => i + 1).map(d => <option key={d} value={d}>{ordinal(d)}</option>)}
                  </select>
                </div>
              )}
              <div className="setup-field">
                <label className="setup-label" htmlFor="sched-time">Time</label>
                <input id="sched-time" name="timeOfDay" type="time" className="setup-input" value={form.timeOfDay} required
                  onChange={e => set('timeOfDay', e.target.value)} />
              </div>
            </div>

            <SearchableSelect
              id="sched-zone"
              label="Time zone"
              value={form.timeZone}
              options={zones.map(z => ({ value: z.value, label: z.label }))}
              onChange={v => set('timeZone', v)}
              hideAllOption
            />

            <div className="setup-field-row scheduled-row">
              <div className="setup-field">
                <label className="setup-label" htmlFor="sched-format">Format</label>
                <select id="sched-format" name="format" className="setup-input" value={form.format}
                  onChange={e => set('format', e.target.value as SaveReportScheduleInput['format'])}>
                  {options.data.formats.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
                </select>
              </div>
              <div className="setup-field">
                <label className="setup-label" htmlFor="sched-lookback">Covers the last</label>
                <div className="scheduled-suffix-input">
                  <input id="sched-lookback" name="lookbackDays" type="number" inputMode="numeric" className="setup-input"
                    min={options.data.lookbackDays.min} max={options.data.lookbackDays.max} value={form.lookbackDays}
                    onChange={e => set('lookbackDays', Math.max(options.data!.lookbackDays.min, Math.min(options.data!.lookbackDays.max, Number(e.target.value) || options.data!.lookbackDays.min)))} />
                  <span aria-hidden="true">days</span>
                </div>
              </div>
            </div>

            <div className="setup-field">
              <label className="setup-label required" htmlFor="sched-recipients">Recipients</label>
              <textarea id="sched-recipients" name="recipients" className="setup-input setup-textarea" rows={3} value={recipientsText}
                autoComplete="off" spellCheck={false} aria-describedby="sched-recipients-hint" aria-invalid={!!invalidRecipient || tooMany}
                onChange={e => setRecipientsText(e.target.value)} placeholder="finance@example.com, ops@example.com…" />
              <p id="sched-recipients-hint" className={`setup-hint${invalidRecipient || tooMany ? ' scheduled-hint-error' : ''}`}>
                {invalidRecipient
                  ? `“${invalidRecipient}” is not a valid email address.`
                  : tooMany
                    ? `Remove ${recipients.length - maxRecipients} — at most ${maxRecipients} recipients.`
                    : `${recipients.length} of ${maxRecipients}. Separate addresses with commas.`}
              </p>
            </div>

            <SearchableSelect
              id="sched-sender"
              label="Send from"
              value={String(form.senderIdentityId || '')}
              options={senders.map(s => ({ value: String(s.id), label: s.displayName ? `${s.displayName} <${s.emailAddress}>` : s.emailAddress }))}
              onChange={v => set('senderIdentityId', Number(v))}
              hideAllOption
              placeholder="Choose a sender…"
              emptyMessage="No email sender is set up. Add one under Connections."
            />

            <div className="setup-toggle-item">
              <div>
                <span className="setup-toggle-label">Active</span>
                <p className="setup-hint">Paused schedules keep their settings but do not send.</p>
              </div>
              <Toggle checked={form.isActive} onChange={on => set('isActive', on)} ariaLabel="Active" />
            </div>
          </form>
        )}
      </Modal>

      <ConfirmationModal
        isOpen={deleteTarget !== null}
        title="Delete Scheduled Report"
        message={`Stop emailing “${deleteTarget?.reportName}”? The saved report itself is kept.`}
        confirmText="Delete Schedule"
        isDestructive
        onConfirm={confirmDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </section>
  )
}

export default ScheduledReports
