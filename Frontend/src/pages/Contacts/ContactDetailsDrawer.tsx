import React, { useEffect, useMemo, useState } from 'react'
import { ContactConsentPanel } from './ContactConsentPanel'
import {
  AtSign,
  Building2,
  CalendarClock,
  FileText,
  Flag,
  Globe,
  Hash,
  Languages,
  Layers,
  MapPin,
  Phone,
  StickyNote,
  Tag,
  User,
  UserCheck,
  Users,
  ExternalLink,
} from 'lucide-react'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'
import { Modal } from '../../components/Modal/Modal'
import { Stepper } from '../../components/Stepper/Stepper'
import { DetailRow } from '../../components/AuditEventDetails/AuditEventDetails'
import { contactService } from '../../services/contacts/contactService'
import type {
  AssignedUser,
  ContactGroup,
  ContactLanguage,
  ContactSource,
  ContactStatus,
  ContactType
} from '../../types/contacts'
import '../../components/AuditEventDetails/AuditEventDetails.css'
import './ContactDetailsDrawer.css'

interface ContactDetailsDrawerProps {
  /** Contact to show. Null closes the drawer. */
  contactId: number | null
  onClose: () => void
}

/** Anything absent reads as an em dash, never as the form's placeholder text. */
const EMPTY = '—'

/**
 * Presents a value, or the em dash when there isn't one.
 *
 * A detail panel that renders "Enter Company" for a contact with no company is describing the form
 * that captured it rather than the contact. Whitespace counts as absent: a field holding only
 * spaces is not information.
 */
const show = (value?: string | null): string => {
  const trimmed = (value ?? '').trim()
  return trimmed.length > 0 ? trimmed : EMPTY
}

/** True when there is something worth linking to. */
const has = (value?: string | null): boolean => (value ?? '').trim().length > 0

/**
 * Resolves a stored option value to the label a human reads.
 *
 * Some of these fields store an id (status, source, type, group) and some already store the name
 * (assigned user, language) — a split inherited from the form's own option lists. Rather than
 * encode which is which at every call site, this matches on either: a value that is already a name
 * passes through, an id is looked up.
 */
const labelFor = (
  value: string | number | null | undefined,
  options: { id: string | number; name: string }[]
): string => {
  // Ids arrive as numbers for some lists (groups) and strings for others; compare as text. Calling
  // .trim() on a number used to crash the whole drawer for any contact in a group.
  const raw = String(value ?? '').trim()
  if (!raw) return EMPTY

  const match = options.find(
    (option) => String(option.id) === raw || String(option.name ?? '').toLowerCase() === raw.toLowerCase()
  )

  // Falls back to the raw value rather than the dash: the option list may not have loaded, or the
  // option may since have been deleted. Showing what is stored beats claiming there is nothing.
  return match?.name ?? raw
}

interface ContactNote {
  id: number
  content: string
  createdAt: string
}

/**
 * A contact, read-only, in the same right-hand drawer as an audit event.
 *
 * <para>
 * This used to embed the Add/Edit form with its fieldset disabled. The values were right but the
 * presentation was an edit form wearing a disabled state — full-height inputs, select chevrons,
 * and for anything the contact lacked, the form's own placeholder, so a contact with no company
 * appeared to have one called "Enter Company".
 * </para>
 * <para>
 * It is now built from the audit drawer's own <see cref="DetailRow"/> and stylesheet: tinted icon
 * tile, small uppercase label, plain value. Grouping and column count follow the content — short
 * facts pair two to a row, prose and URLs take the full width — rather than a fixed grid that
 * would either truncate an address or strand a postcode across half a panel.
 * </para>
 * <para>
 * Reading only. Editing remains the full page, which still owns every mutation.
 * </para>
 */
export const ContactDetailsDrawer: React.FC<ContactDetailsDrawerProps> = ({
  contactId,
  onClose
}) => {
  const steps = ['Contact Details', 'Other Details', 'Notes', 'Consent']
  const [activeStep, setActiveStep] = useState(0)

  const [contact, setContact] = useState<any | null>(null)
  const [isLoading, setIsLoading] = useState(false)

  const [statuses, setStatuses] = useState<ContactStatus[]>([])
  const [sources, setSources] = useState<ContactSource[]>([])
  const [assignedUsers, setAssignedUsers] = useState<AssignedUser[]>([])
  const [types, setTypes] = useState<ContactType[]>([])
  const [languages, setLanguages] = useState<ContactLanguage[]>([])
  const [groups, setGroups] = useState<ContactGroup[]>([])

  const [notes, setNotes] = useState<ContactNote[]>([])
  const [isLoadingNotes, setIsLoadingNotes] = useState(false)

  // Opening a different row resets to the first tab, so the panel never opens on Notes purely
  // because that is where the previous contact was left.
  useEffect(() => {
    if (contactId !== null) setActiveStep(0)
  }, [contactId])

  useEffect(() => {
    if (contactId === null) return

    let cancelled = false
    setIsLoading(true)

    // The option lists are what resolve ids into names, so they are fetched alongside the contact
    // rather than after it — otherwise every classification renders as a raw id first.
    Promise.all([
      contactService.getContactById(contactId),
      contactService.getContactStatuses(),
      contactService.getContactSources(),
      contactService.getAssignedUsers(),
      contactService.getContactTypes(),
      contactService.getContactLanguages(),
      contactService.getContactGroups()
    ])
      .then(([fetched, s, so, a, t, l, g]) => {
        if (cancelled) return
        setContact(fetched)
        setStatuses(s)
        setSources(so)
        setAssignedUsers(a)
        setTypes(t)
        setLanguages(l)
        setGroups(g)
      })
      .catch(() => {
        // Left to the empty state below. A drawer that throws would take the list down with it.
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [contactId])

  // Notes load with their tab, as the form did — most viewers never open it, and it is its own
  // request.
  useEffect(() => {
    if (contactId === null || activeStep !== 2) return

    let cancelled = false
    setIsLoadingNotes(true)

    contactService
      .getContactNotes(contactId)
      .then((list) => {
        if (!cancelled) setNotes(list || [])
      })
      .catch(() => {
        if (!cancelled) setNotes([])
      })
      .finally(() => {
        if (!cancelled) setIsLoadingNotes(false)
      })

    return () => {
      cancelled = true
    }
  }, [contactId, activeStep])

  // The form splits a legacy single `name` into first and last when the dedicated fields are
  // absent. The same rule applies here so both render the contact identically.
  const { firstName, lastName } = useMemo(() => {
    if (!contact) return { firstName: '', lastName: '' }

    if (contact.name && !contact.firstName && !contact.lastName) {
      const parts = String(contact.name).split(' ')
      return { firstName: parts[0] || '', lastName: parts.slice(1).join(' ') || '' }
    }

    return { firstName: contact.firstName || '', lastName: contact.lastName || '' }
  }, [contact])

  const groupValue = useMemo(() => {
    if (!contact) return ''
    return Array.isArray(contact.groups) ? contact.groups[0]?.id || '' : contact.groups || ''
  }, [contact])

  const website = String(contact?.website ?? '').trim()
  const websiteHref = website && !/^https?:\/\//i.test(website) ? `https://${website}` : website
  const email = String(contact?.email ?? '').trim()
  const phone = String(contact?.phone ?? '').trim()

  return (
    <Modal
      isOpen={contactId !== null}
      onClose={onClose}
      placement="right"
      size="custom"
      title="Contact Details"
      subtitle="Contact information, other details, notes and consent"
      showCloseButton
    >
      <div className="contact-drawer">
        <Stepper steps={steps} activeStep={activeStep} onChangeStep={setActiveStep} />

        {isLoading && !contact ? (
          <p className="audit-detail-empty">Loading contact…</p>
        ) : !contact ? (
          <p className="audit-detail-empty">This contact could not be loaded.</p>
        ) : (
          <>
            {/* ── Tab 1: Contact Details ─────────────────────────────────── */}
            {activeStep === 0 && (
              <div className="audit-detail">
                <section className="audit-detail-section">
                  <h3 className="audit-detail-section-title">
                    <User size={12} />
                    <span>Overview</span>
                  </h3>

                  <div className="audit-field-grid">
                    <DetailRow icon={<User size={15} />} tone="blue" label="First Name">
                      {show(firstName)}
                    </DetailRow>

                    <DetailRow icon={<User size={15} />} tone="blue" label="Last Name">
                      {show(lastName)}
                    </DetailRow>

                    <DetailRow icon={<Building2 size={15} />} tone="neutral" label="Company">
                      {show(contact.company)}
                    </DetailRow>

                    <DetailRow icon={<Tag size={15} />} tone="purple" label="Type">
                      <span className="audit-pill">{labelFor(contact.type, types)}</span>
                    </DetailRow>
                  </div>
                </section>

                <section className="audit-detail-section">
                  <h3 className="audit-detail-section-title">
                    <AtSign size={12} />
                    <span>Contact Information</span>
                  </h3>

                  <div className="audit-field-grid">
                    <DetailRow icon={<AtSign size={15} />} tone="blue" label="Email">
                      {has(email) ? (
                        <a className="contact-drawer-link" href={`mailto:${email}`}>
                          {email}
                        </a>
                      ) : (
                        EMPTY
                      )}
                    </DetailRow>

                    <DetailRow icon={<Phone size={15} />} tone="success" label="Phone">
                      {has(phone) ? (
                        <a className="contact-drawer-link" href={`tel:${phone}`}>
                          {phone}
                        </a>
                      ) : (
                        EMPTY
                      )}
                    </DetailRow>

                    {/* A URL is long and unbreakable — given half the panel it either truncates or
                        forces its row taller than every other. It takes the full width. */}
                    <div className="is-full">
                      <DetailRow icon={<Globe size={15} />} tone="blue" label="Website">
                        {has(website) ? (
                          <a
                            className="contact-drawer-link"
                            href={websiteHref}
                            target="_blank"
                            rel="noopener noreferrer"
                          >
                            {website}
                          </a>
                        ) : (
                          EMPTY
                        )}
                      </DetailRow>
                    </div>
                  </div>
                </section>

                <section className="audit-detail-section">
                  <h3 className="audit-detail-section-title">
                    <Layers size={12} />
                    <span>Assignment &amp; Classification</span>
                  </h3>

                  <div className="audit-field-grid">
                    <DetailRow icon={<Flag size={15} />} tone="success" label="Status">
                      <span className="audit-pill">{labelFor(contact.status, statuses)}</span>
                    </DetailRow>

                    <DetailRow icon={<Hash size={15} />} tone="neutral" label="Source">
                      <span className="audit-pill">{labelFor(contact.source, sources)}</span>
                    </DetailRow>

                    <DetailRow icon={<UserCheck size={15} />} tone="purple" label="Assigned">
                      {labelFor(contact.assignedTo, assignedUsers)}
                    </DetailRow>

                    <DetailRow icon={<Languages size={15} />} tone="blue" label="Default Language">
                      {labelFor(contact.language, languages)}
                    </DetailRow>

                    <div className="is-full">
                      <DetailRow icon={<Users size={15} />} tone="neutral" label="Groups">
                        {groupValue ? (
                          <span className="audit-pill">{labelFor(groupValue, groups)}</span>
                        ) : (
                          EMPTY
                        )}
                      </DetailRow>
                    </div>
                  </div>
                </section>
              </div>
            )}

            {/* ── Tab 2: Other Details ───────────────────────────────────── */}
            {activeStep === 1 && (
              <div className="audit-detail">
                <section className="audit-detail-section">
                  <h3 className="audit-detail-section-title">
                    <MapPin size={12} />
                    <span>Address Information</span>
                  </h3>

                  <div className="audit-field-grid">
                    <DetailRow icon={<MapPin size={15} />} tone="blue" label="City">
                      {show(contact.city)}
                    </DetailRow>

                    <DetailRow icon={<MapPin size={15} />} tone="blue" label="State">
                      {show(contact.state)}
                    </DetailRow>

                    <DetailRow icon={<Globe size={15} />} tone="neutral" label="Country">
                      {show(contact.country)}
                    </DetailRow>

                    <DetailRow icon={<Globe size={15} />} tone="neutral" label="Time zone">
                      {show(contact.timeZone)}
                    </DetailRow>

                    <DetailRow icon={<Hash size={15} />} tone="neutral" label="Zip Code">
                      {show(contact.zipCode)}
                    </DetailRow>

                    {/* A street address wraps over several lines; in a half-width cell it would
                        make every other row in the grid as tall as this one. */}
                    <div className="is-full">
                      <DetailRow icon={<MapPin size={15} />} tone="purple" label="Address">
                        <span className="contact-drawer-prose">{show(contact.address)}</span>
                      </DetailRow>
                    </div>
                  </div>
                </section>

                <section className="audit-detail-section">
                  <h3 className="audit-detail-section-title">
                    <FileText size={12} />
                    <span>Additional Details</span>
                  </h3>

                  {/* Free prose — full width by definition. */}
                  <div className="audit-detail-list">
                    <DetailRow icon={<FileText size={15} />} tone="neutral" label="Description">
                      <span className="contact-drawer-prose">{show(contact.description)}</span>
                    </DetailRow>
                    {(contact.adSourceId || contact.adHeadline) && (
                      <DetailRow icon={<Globe size={15} />} tone="blue" label="Came from ad">
                        <span className="contact-drawer-prose">
                          {contact.adHeadline || `Ad ${contact.adSourceId}`}
                          {contact.adSourceUrl && /^https:\/\//i.test(contact.adSourceUrl) && (
                            <> · <a className="contact-ad-link" href={contact.adSourceUrl} target="_blank" rel="noopener noreferrer">
                              Open the ad<ExternalLink size={12} aria-hidden="true" /><span className="sr-only"> (opens in a new tab)</span>
                            </a></>
                          )}
                          {contact.adAttributedAt && (
                            <> · First contact <time dateTime={contact.adAttributedAt}>{formatAbsoluteDateTime(contact.adAttributedAt)}</time></>
                          )}
                        </span>
                      </DetailRow>
                    )}
                  </div>
                </section>
              </div>
            )}

            {/* ── Tab 3: Notes ───────────────────────────────────────────── */}
            {activeStep === 2 && (
              <div className="audit-detail">
                <section className="audit-detail-section">
                  <h3 className="audit-detail-section-title">
                    <StickyNote size={12} />
                    <span>Notes</span>
                    {notes.length > 0 && <span className="audit-msg-count">{notes.length}</span>}
                  </h3>

                  {isLoadingNotes ? (
                    <p className="audit-detail-empty">Loading notes…</p>
                  ) : notes.length === 0 ? (
                    <p className="audit-detail-empty">No notes added yet for this contact.</p>
                  ) : (
                    <ol className="contact-drawer-notes">
                      {notes.map((note) => (
                        <li key={note.id} className="contact-drawer-note">
                          <p className="contact-drawer-note-content">{note.content}</p>
                          <span className="contact-drawer-note-date">
                            <CalendarClock size={11} />
                            {new Date(note.createdAt).toLocaleString()}
                          </span>
                        </li>
                      ))}
                    </ol>
                  )}
                </section>
              </div>
            )}

            {/* ── Tab 4: Consent ─────────────────────────────────────────── */}
            {activeStep === 3 && contactId !== null && <ContactConsentPanel contactId={contactId} />}
          </>
        )}
      </div>
    </Modal>
  )
}

export default ContactDetailsDrawer
