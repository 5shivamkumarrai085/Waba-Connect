import React, { useState, useEffect, useRef, useMemo } from 'react'
import { useNavigate, useParams, useLocation } from 'react-router-dom'
import { useContactStore } from '../../store/contactStore'
import { contactService } from '../../services/contacts/contactService'
import { Stepper } from '../../components/Stepper/Stepper'
import toast from 'react-hot-toast'
import { ALL_COUNTRIES, getFlagEmoji } from '../../utils/countryData'
import type { 
  ContactStatus, 
  ContactSource, 
  AssignedUser, 
  ContactType, 
  ContactLanguage, 
  ContactGroup 
} from '../../types/contacts'
import './AddContact.css'
import { getErrorMessage } from '../../utils/errorHelper'

export const AddContact: React.FC = () => {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()
  const location = useLocation()
  const queryParams = new URLSearchParams(location.search)
  const isViewMode = queryParams.get('view') === 'true'
  const isEditMode = !!id
  const { addContact } = useContactStore()
  const [isSaving, setIsSaving] = useState(false)
  const [errors, setErrors] = useState<Record<string, string>>({})

  // Steps
  const steps = ['Contact Details', 'Other Details', 'Notes']
  const [activeStep, setActiveStep] = useState<number>(0)

  // Options states
  const [statuses, setStatuses] = useState<ContactStatus[]>([])
  const [sources, setSources] = useState<ContactSource[]>([])
  const [assignedUsers, setAssignedUsers] = useState<AssignedUser[]>([])
  const [types, setTypes] = useState<ContactType[]>([])
  const [languages, setLanguages] = useState<ContactLanguage[]>([])
  const [groups, setGroups] = useState<ContactGroup[]>([])

  // Country Code Picker states
  const [showCountryPicker, setShowCountryPicker] = useState(false)
  const [countrySearch, setCountrySearch] = useState('')
  const countryPickerRef = useRef<HTMLDivElement>(null)

  // Form Fields
  const [statusVal, setStatusVal] = useState('')
  const [sourceVal, setSourceVal] = useState('')
  const [assignedVal, setAssignedVal] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [company, setCompany] = useState('')
  const [typeVal, setTypeVal] = useState('')
  const [email, setEmail] = useState('')
  const [selectedDialCodeVal, setSelectedDialCodeVal] = useState('+91')
  const [localPhone, setLocalPhone] = useState('')
  const [website, setWebsite] = useState('')
  const [languageVal, setLanguageVal] = useState('')
  const [selectedGroups, setSelectedGroups] = useState('')

  // Other Details Fields
  const [city, setCity] = useState('')
  const [stateVal, setStateVal] = useState('')
  const [countryVal, setCountryVal] = useState('')
  const [zipCode, setZipCode] = useState('')
  const [address, setAddress] = useState('')
  const [description, setDescription] = useState('')

  // Notes Tab states
  const [notes, setNotes] = useState<any[]>([])
  const [newNote, setNewNote] = useState('')
  const [isLoadingNotes, setIsLoadingNotes] = useState(false)
  const [isAddingNote, setIsAddingNote] = useState(false)

  const loadNotes = async () => {
    if (!id) return
    setIsLoadingNotes(true)
    try {
      const list = await contactService.getContactNotes(parseInt(id, 10))
      setNotes(list || [])
    } catch (err) {
      console.error('Failed to load notes', err)
    } finally {
      setIsLoadingNotes(false)
    }
  }

  // Load notes when on Tab 3
  useEffect(() => {
    if (activeStep === 2 && id) {
      void loadNotes()
    }
  }, [activeStep, id])

  const handleAddNote = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!id || !newNote.trim()) return
    setIsAddingNote(true)
    try {
      await contactService.addContactNote(parseInt(id, 10), newNote.trim())
      setNewNote('')
      toast.success('Note added successfully')
      void loadNotes()
    } catch (err) {
      toast.error('Failed to add note')
    } finally {
      setIsAddingNote(false)
    }
  }

  const handleDeleteNote = async (noteId: number) => {
    if (!id) return
    try {
      await contactService.deleteContactNote(parseInt(id, 10), noteId)
      toast.success('Note deleted successfully')
      void loadNotes()
    } catch (err) {
      toast.error('Failed to delete note')
    }
  }

  // Close country picker on click outside or Escape key
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (countryPickerRef.current && !countryPickerRef.current.contains(e.target as Node)) {
        setShowCountryPicker(false)
      }
    }
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setShowCountryPicker(false)
      }
    }
    document.addEventListener('mousedown', handleClickOutside)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [])

  useEffect(() => {
    const fetchOptionsAndContact = async () => {
      try {
        const [
          fetchedStatuses,
          fetchedSources,
          fetchedAssigned,
          fetchedTypes,
          fetchedLanguages,
          fetchedGroups
        ] = await Promise.all([
          contactService.getContactStatuses(),
          contactService.getContactSources(),
          contactService.getAssignedUsers(),
          contactService.getContactTypes(),
          contactService.getContactLanguages(),
          contactService.getContactGroups()
        ])

        setStatuses(fetchedStatuses)
        setSources(fetchedSources)
        setAssignedUsers(fetchedAssigned)
        setTypes(fetchedTypes)
        setLanguages(fetchedLanguages)
        setGroups(fetchedGroups)

        if (id) {
          const contact = await contactService.getContactById(parseInt(id, 10))
          if (contact) {
            setStatusVal(contact.status || '')
            setSourceVal(contact.source || '')
            setAssignedVal(contact.assignedTo || '')
            if (contact.name && !contact.firstName && !contact.lastName) {
              const parts = contact.name.split(' ')
              setFirstName(parts[0] || '')
              setLastName(parts.slice(1).join(' ') || '')
            } else {
              setFirstName(contact.firstName || '')
              setLastName(contact.lastName || '')
            }
            setCompany(contact.company || '')
            setTypeVal(contact.type || '')
            setEmail(contact.email || '')
            const fullPhone = contact.phone || ''
            let matchedDialCode = '+91'
            let matchedLocal = fullPhone
            const sortedCountries = [...ALL_COUNTRIES].sort((a, b) => b.dialCode.length - a.dialCode.length)
            for (const c of sortedCountries) {
              if (fullPhone.startsWith(c.dialCode)) {
                matchedDialCode = c.dialCode
                matchedLocal = fullPhone.slice(c.dialCode.length)
                break
              }
            }
            setSelectedDialCodeVal(matchedDialCode)
            setLocalPhone(matchedLocal)
            setWebsite(contact.website || '')
            setLanguageVal(contact.language || '')
            setSelectedGroups(Array.isArray(contact.groups) ? contact.groups[0]?.id || '' : contact.groups || '')
            setCity(contact.city || '')
            setStateVal(contact.state || '')
            setCountryVal(contact.country || '')
            setZipCode(contact.zipCode || '')
            setAddress(contact.address || '')
            setDescription(contact.description || '')
          }
        }
      } catch (err) {
      }
    }

    fetchOptionsAndContact()
  }, [id])

  const selectedCountry = useMemo(() => {
    return ALL_COUNTRIES.find((c) => c.dialCode === selectedDialCodeVal) || ALL_COUNTRIES[0]
  }, [selectedDialCodeVal])

  const filteredCountries = useMemo(() => {
    const q = countrySearch.toLowerCase().trim()
    if (!q) return ALL_COUNTRIES
    return ALL_COUNTRIES.filter(
      (c) =>
        c.name.toLowerCase().includes(q) ||
        c.dialCode.includes(q) ||
        c.code.toLowerCase().includes(q)
    )
  }, [countrySearch])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    
    // Quick validation on required fields
    const newErrors: Record<string, string> = {}
    if (!firstName) newErrors.firstName = 'First Name is required.'
    if (!lastName) newErrors.lastName = 'Last Name is required.'
    if (!localPhone) newErrors.phone = 'Phone is required.'
    if (!typeVal) newErrors.typeVal = 'Type is required.'
    if (!statusVal) newErrors.statusVal = 'Status is required.'
    if (!sourceVal) newErrors.sourceVal = 'Source is required.'

    // Per-country phone length validation
    if (localPhone) {
      if (localPhone.length < selectedCountry.minDigits || localPhone.length > selectedCountry.maxDigits) {
        newErrors.phone = `Phone number for ${selectedCountry.name} must be ${selectedCountry.maxDigits} digits.`
      }
    }

    // Email format validation
    if (email && email.trim() !== '') {
      const emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/
      if (!emailRegex.test(email.trim())) {
        newErrors.email = 'Please enter a valid email address.'
      }
    }

    if (Object.keys(newErrors).length > 0) {
      setErrors(newErrors)
      toast.error('Please fill out all required fields correctly.')
      setActiveStep(0) // redirect back to first step to correct
      return
    }

    setErrors({})
    setIsSaving(true)

    try {
      const payload = {
        name: `${firstName} ${lastName}`.trim(),
        firstName,
        lastName,
        phone: selectedDialCodeVal + localPhone,
        type: typeVal,
        status: statusVal,
        source: sourceVal,
        assignedTo: assignedVal,
        assigned: assignedVal,
        groups: Array.isArray(selectedGroups) ? selectedGroups.join(',') : (selectedGroups || ''),
        company,
        email,
        website,
        language: languageVal,
        city,
        state: stateVal,
        country: countryVal,
        zipCode,
        address,
        description
      }

      if (isEditMode && id) {
        await contactService.updateContact(parseInt(id, 10), payload)
        toast.success('Contact updated successfully!')
      } else {
        await addContact(payload)
        toast.success('Contact created successfully!')
      }
      navigate('/contacts')
    } catch (err: any) {
      const apiMsg = getErrorMessage(err, isEditMode ? 'Failed to update contact.' : 'Failed to create contact.')
      
      const fieldErrors: Record<string, string> = {}
      const lowerMsg = apiMsg.toLowerCase()
      if (lowerMsg.includes('phone') || lowerMsg.includes('number')) {
        fieldErrors.phone = apiMsg
        setActiveStep(0)
      } else if (lowerMsg.includes('email')) {
        fieldErrors.email = apiMsg
        setActiveStep(0)
      } else if (lowerMsg.includes('name')) {
        fieldErrors.firstName = apiMsg
        setActiveStep(0)
      }
      
      setErrors(fieldErrors)
      toast.error(apiMsg)
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div className="fade-in">
      <h2 className="add-contact-title">{isViewMode ? 'View Contact' : (isEditMode ? 'Edit Contact' : 'Add New Contact')}</h2>

      <div className="add-contact-card">
        {/* Step index buttons indicators */}
        <Stepper
          steps={steps}
          activeStep={activeStep}
          onChangeStep={setActiveStep}
        />

        <form onSubmit={handleSubmit}>
          <fieldset disabled={isViewMode} style={{ border: 'none', padding: 0, margin: 0 }}>
            <div className="add-contact-body">
            {/* Tab 1: Contact Details */}
            {activeStep === 0 && (
              <div className="fade-in">
                {/* Status, Source, Assigned row */}
                <div className="add-contact-top-grid">
                  <div className="form-group form-group-required">
                    <label className="form-label">Status</label>
                    <select
                      className={`form-control ${errors.statusVal ? 'is-invalid' : ''}`}
                      value={statusVal}
                      onChange={(e) => setStatusVal(e.target.value)}
                      required
                    >
                      <option value="">Select Status</option>
                      {statuses.map(s => (
                        <option key={s.id} value={s.id}>{s.name}</option>
                      ))}
                    </select>
                  </div>

                  <div className="form-group form-group-required">
                    <label className="form-label">Source</label>
                    <select
                      className={`form-control ${errors.sourceVal ? 'is-invalid' : ''}`}
                      value={sourceVal}
                      onChange={(e) => setSourceVal(e.target.value)}
                      required
                    >
                      <option value="">Select Source</option>
                      {sources.map(s => (
                        <option key={s.id} value={s.id}>{s.name}</option>
                      ))}
                    </select>
                  </div>

                  <div className="form-group">
                    <label className="form-label">Assigned</label>
                    <select
                      className="form-control"
                      value={assignedVal}
                      onChange={(e) => setAssignedVal(e.target.value)}
                    >
                      <option value="">Select Assigned Name</option>
                      {assignedUsers.map(u => (
                        <option key={u.id} value={u.name}>{u.name}</option>
                      ))}
                    </select>
                  </div>
                </div>

                {/* Name, Company, Type row */}
                <div className="add-contact-form-grid">
                  <div className="form-group form-group-required">
                    <label className="form-label">First Name</label>
                    <input
                      type="text"
                      className={`form-control ${errors.firstName ? 'is-invalid' : ''}`}
                      placeholder="Enter First Name"
                      value={firstName}
                      onChange={(e) => setFirstName(e.target.value)}
                      disabled={isSaving}
                      required
                    />
                    {errors.firstName && <span className="invalid-feedback">{errors.firstName}</span>}
                  </div>

                  <div className="form-group form-group-required">
                    <label className="form-label">Last Name</label>
                    <input
                      type="text"
                      className={`form-control ${errors.lastName ? 'is-invalid' : ''}`}
                      placeholder="Enter Last Name"
                      value={lastName}
                      onChange={(e) => setLastName(e.target.value)}
                      disabled={isSaving}
                      required
                    />
                    {errors.lastName && <span className="invalid-feedback">{errors.lastName}</span>}
                  </div>

                  <div className="form-group">
                    <label className="form-label">Company</label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder="Enter Company"
                      value={company}
                      onChange={(e) => setCompany(e.target.value)}
                      disabled={isSaving}
                    />
                  </div>

                  <div className="form-group form-group-required">
                    <label className="form-label">Type</label>
                    <select
                      className={`form-control ${errors.typeVal ? 'is-invalid' : ''}`}
                      value={typeVal}
                      onChange={(e) => setTypeVal(e.target.value)}
                      required
                    >
                      <option value="">Select Type</option>
                      {types.map(t => (
                        <option key={t.id} value={t.id}>{t.name}</option>
                      ))}
                    </select>
                    {errors.typeVal && <span className="invalid-feedback">{errors.typeVal}</span>}
                  </div>

                  <div className="form-group">
                    <label className="form-label">Email</label>
                    <input
                      type="email"
                      className={`form-control ${errors.email ? 'is-invalid' : ''}`}
                      placeholder="Enter Email"
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      disabled={isSaving}
                    />
                    {errors.email && <span className="invalid-feedback">{errors.email}</span>}
                  </div>

                  <div className={`form-group form-group-required ${showCountryPicker ? 'has-active-dropdown' : ''}`}>
                    <label className="form-label">Phone</label>
                    <div className="phone-input-container">
                      <div className="country-picker-wrapper" ref={countryPickerRef}>
                        <button
                          type="button"
                          className="country-picker-trigger"
                          onClick={() => setShowCountryPicker(!showCountryPicker)}
                          aria-label="Select Country Code"
                        >
                          <span className="country-flag">{getFlagEmoji(selectedCountry.code)}</span>
                          <span className="country-picker-arrow">▾</span>
                        </button>

                        {showCountryPicker && (
                          <div className="country-picker-dropdown">
                            <div className="country-picker-search">
                              <input
                                type="text"
                                placeholder="Search"
                                value={countrySearch}
                                onChange={(e) => setCountrySearch(e.target.value)}
                                autoFocus
                              />
                            </div>
                            <div className="country-picker-list">
                              {filteredCountries.map((c) => (
                                <button
                                  key={c.code + c.dialCode}
                                  type="button"
                                  className={`country-picker-item ${c.dialCode === selectedDialCodeVal ? 'active' : ''}`}
                                  onClick={() => {
                                    setSelectedDialCodeVal(c.dialCode)
                                    setShowCountryPicker(false)
                                    setCountrySearch('')
                                  }}
                                >
                                  <span className="country-flag">{getFlagEmoji(c.code)}</span>
                                  <span className="country-name">{c.name}</span>
                                  <span className="country-code-text">{c.dialCode}</span>
                                </button>
                              ))}
                              {filteredCountries.length === 0 && (
                                <div className="country-picker-empty">No countries found</div>
                              )}
                            </div>
                          </div>
                        )}
                      </div>
                      <input
                        type="text"
                        className={`form-control phone-input ${errors.phone ? 'is-invalid' : ''}`}
                        placeholder="Enter phone number"
                        value={localPhone}
                        onChange={(e) => {
                          const val = e.target.value.replace(/[^0-9]/g, '')
                          const maxLen = selectedCountry.maxDigits || 10
                          setLocalPhone(val.slice(0, maxLen))
                        }}
                        disabled={isSaving}
                        maxLength={selectedCountry.maxDigits || 10}
                        required
                      />
                    </div>
                    {errors.phone && <span className="invalid-feedback">{errors.phone}</span>}
                  </div>

                  <div className="form-group">
                    <label className="form-label">Website</label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder="Enter Website URL"
                      value={website}
                      onChange={(e) => setWebsite(e.target.value)}
                    />
                  </div>

                  <div className="form-group">
                    <label className="form-label">Default Language</label>
                    <select
                      className="form-control"
                      value={languageVal}
                      onChange={(e) => setLanguageVal(e.target.value)}
                    >
                      <option value="">Select Language</option>
                      {languages.map(l => (
                        <option key={l.id} value={l.name}>{l.name}</option>
                      ))}
                    </select>
                  </div>

                  {/* Groups field */}
                  <div className="form-group">
                    <label className="form-label">Assign to Groups</label>
                    <select
                      className="form-control"
                      value={selectedGroups}
                      onChange={(e) => setSelectedGroups(e.target.value)}
                    >
                      <option value="">Select Groups</option>
                      {groups.map(g => (
                        <option key={g.id} value={g.id}>{g.name}</option>
                      ))}
                    </select>
                  </div>
                </div>
              </div>
            )}

            {/* Tab 2: Other Details */}
            {activeStep === 1 && (
              <div className="fade-in">
                <div className="add-contact-form-grid">
                  <div className="form-group">
                    <label className="form-label">City</label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder="Enter City"
                      value={city}
                      onChange={(e) => setCity(e.target.value)}
                    />
                  </div>

                  <div className="form-group">
                    <label className="form-label">State</label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder="Enter State"
                      value={stateVal}
                      onChange={(e) => setStateVal(e.target.value)}
                    />
                  </div>

                  <div className="form-group">
                    <label className="form-label">Country</label>
                    <select
                      className="form-control"
                      value={countryVal}
                      onChange={(e) => {
                        const val = e.target.value
                        setCountryVal(val)
                        const matched = ALL_COUNTRIES.find(c => c.name === val || c.code === val)
                        if (matched && matched.dialCode) {
                          setSelectedDialCodeVal(matched.dialCode)
                        }
                      }}
                      disabled={isViewMode}
                    >
                      <option value="">Select Country</option>
                      {ALL_COUNTRIES.map(c => (
                        <option key={c.code} value={c.name}>{c.name}</option>
                      ))}
                    </select>
                  </div>

                  <div className="form-group">
                    <label className="form-label">Zip Code</label>
                    <input
                      type="text"
                      className="form-control"
                      placeholder="Enter Zip Code"
                      value={zipCode}
                      onChange={(e) => setZipCode(e.target.value)}
                    />
                  </div>
                </div>

                <div className="form-group margin-top-20">
                  <label className="form-label">Address</label>
                  <textarea
                    className="form-control"
                    placeholder="Enter Address"
                    rows={3}
                    value={address}
                    onChange={(e) => setAddress(e.target.value)}
                  />
                </div>

                <div className="form-group margin-top-20">
                  <label className="form-label">Description</label>
                  <textarea
                    className="form-control"
                    placeholder="Enter Description"
                    rows={3}
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                  />
                </div>
              </div>
            )}

            {/* Tab 3: Notes */}
            {activeStep === 2 && (
              <div className="fade-in notes-tab-content">
                {!id ? (
                  <div className="notes-tab-notice">
                    Notes will be available once the contact is created. Please click Save/Add to create the contact first, then edit it to add notes.
                  </div>
                ) : (
                  <div>
                    {/* Add note section - only if not viewMode */}
                    {!isViewMode && (
                      <div className="notes-add-section">
                        <textarea
                          className="form-control"
                          placeholder="Write a note here..."
                          rows={3}
                          value={newNote}
                          onChange={(e) => setNewNote(e.target.value)}
                          disabled={isAddingNote}
                        />
                        <button
                          type="button"
                          className="btn btn-primary notes-add-btn"
                          onClick={handleAddNote}
                          disabled={isAddingNote || !newNote.trim()}
                        >
                          {isAddingNote ? 'Adding...' : 'Add Note'}
                        </button>
                      </div>
                    )}

                    {isLoadingNotes ? (
                      <div className="notes-loading">Loading notes...</div>
                    ) : notes.length === 0 ? (
                      <div className="notes-empty">No notes added yet for this contact.</div>
                    ) : (
                      <div className="notes-list">
                        {notes.map((note) => (
                          <div key={note.id} className="note-item">
                            <p className="note-content">{note.content}</p>
                            <div className="note-footer">
                              <span className="note-date">
                                {new Date(note.createdAt).toLocaleString()}
                              </span>
                              {!isViewMode && (
                                <button
                                  type="button"
                                  className="note-delete-btn"
                                  onClick={() => handleDeleteNote(note.id)}
                                >
                                  Delete
                                </button>
                              )}
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}
          </div>
        </fieldset>

          {/* Stepper Wizard footer buttons panel */}
          <div className="add-contact-footer">
            <button
              type="button"
              className="btn-cancel"
              onClick={() => navigate('/contacts')}
              disabled={isSaving}
            >
              Cancel
            </button>
            
            <button
              type="submit"
              className="btn-add"
              disabled={isSaving}
              onClick={(e) => {
                if (isViewMode) {
                  e.preventDefault();
                  navigate('/contacts');
                }
              }}
            >
              {isSaving ? 'Saving...' : (isViewMode ? 'Close' : (isEditMode ? 'Save' : 'Add'))}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
export default AddContact
