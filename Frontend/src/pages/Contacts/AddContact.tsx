import React, { useState, useEffect, useRef, useMemo } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate, useParams, useLocation } from 'react-router-dom'
import { useContactStore } from '../../store/contactStore'
import { contactService } from '../../services/contacts/contactService'
import { Stepper } from '../../components/Stepper/Stepper'
import toast from 'react-hot-toast'
import { ALL_COUNTRIES, getFlagEmoji } from '../../utils/countryData'
import { referenceService, type TimeZoneOption, type ContactFieldOptions } from '../../services/referenceService'
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
import { SearchableSelect } from '../../components/SearchableSelect/SearchableSelect'

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
  const [timeZoneVal, setTimeZoneVal] = useState('')
  const [timeZones, setTimeZones] = useState<TimeZoneOption[]>([])

  // The zones the server can resolve, for quiet hours and local-time sends.
  useEffect(() => {
    referenceService.getTimeZones().then(setTimeZones).catch(() => setTimeZones([]))
  }, [])
  const [zipCode, setZipCode] = useState('')
  const [address, setAddress] = useState('')
  const [description, setDescription] = useState('')
  const [dateOfBirth, setDateOfBirth] = useState('')

  // Which fields are required, their lengths and the accepted ages: the same rules the server
  // validates against (GET api/reference/contact-fields). Administrators choose the optional
  // ones under OmniConnect Settings › Contacts, so nothing about "required" is decided here.
  const [fieldRules, setFieldRules] = useState<ContactFieldOptions | null>(null)
  useEffect(() => {
    referenceService.getContactFields().then(setFieldRules).catch(() => setFieldRules(null))
  }, [])
  const isRequired = (key: string) => fieldRules?.fields.some(f => f.key === key && f.required) ?? false
  const maxLengthOf = (key: string) => fieldRules?.fields.find(f => f.key === key)?.maxLength ?? undefined
  const requiredClass = (key: string) => (isRequired(key) ? 'form-group form-group-required' : 'form-group')

  // The accepted date-of-birth window, in local calendar days.
  const isoDay = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const dobBounds = useMemo(() => {
    const today = new Date()
    const oldest = new Date(today)
    oldest.setFullYear(today.getFullYear() - (fieldRules?.ages.max ?? 120))
    const youngest = new Date(today)
    youngest.setFullYear(today.getFullYear() - (fieldRules?.ages.min ?? 0))
    return { min: isoDay(oldest), max: isoDay(youngest) }
  }, [fieldRules])
  const ageFromDob = (value: string): number | null => {
    if (!value) return null
    const dob = new Date(`${value}T00:00:00`)
    if (Number.isNaN(dob.getTime())) return null
    const today = new Date()
    let age = today.getFullYear() - dob.getFullYear()
    const hadBirthday = today.getMonth() > dob.getMonth() || (today.getMonth() === dob.getMonth() && today.getDate() >= dob.getDate())
    if (!hadBirthday) age--
    return age
  }
  const age = ageFromDob(dateOfBirth)

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
            setTimeZoneVal(contact.timeZone || '')
            setZipCode(contact.zipCode || '')
            setAddress(contact.address || '')
            setDescription(contact.description || '')
            setDateOfBirth(contact.dateOfBirth || '')
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
    
    // The same rules the server applies, so most mistakes are caught before the round trip.
    const newErrors: Record<string, string> = {}
    const label = (key: string, fallback: string) => fieldRules?.fields.find(f => f.key === key)?.label ?? fallback
    if (!firstName.trim()) newErrors.firstName = 'First name is required.'
    if (!lastName.trim()) newErrors.lastName = 'Last name is required.'
    if (fieldRules && firstName.trim() && `${firstName} ${lastName}`.trim().length < fieldRules.nameMinLength) {
      newErrors.firstName = `The name must be at least ${fieldRules.nameMinLength} characters.`
    }
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

    // Fields the administrator made required.
    const optionalValues: Record<string, string> = {
      email, company, website, city, state: stateVal, country: countryVal, timeZone: timeZoneVal,
      zipCode, address, description, assignedTo: assignedVal, dateOfBirth
    }
    Object.entries(optionalValues).forEach(([key, value]) => {
      if (isRequired(key) && !value.trim()) newErrors[key] = `${label(key, key)} is required.`
    })

    // Email format validation
    if (email && email.trim() !== '') {
      const emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/
      if (!emailRegex.test(email.trim())) {
        newErrors.email = 'Enter a valid email address, e.g. name@example.com.'
      }
    }

    // Date of birth: not in the future, and inside the accepted age range.
    if (dateOfBirth) {
      if (dateOfBirth > dobBounds.max || dateOfBirth < dobBounds.min) {
        newErrors.dateOfBirth = `Age must be between ${fieldRules?.ages.min ?? 0} and ${fieldRules?.ages.max ?? 120} years.`
      }
    }

    if (Object.keys(newErrors).length > 0) {
      setErrors(newErrors)
      const onOtherDetails = ['city', 'state', 'country', 'timeZone', 'zipCode', 'address', 'description']
      const firstStep = Object.keys(newErrors).every(k => onOtherDetails.includes(k)) ? 1 : 0
      toast.error(`Check the highlighted field${Object.keys(newErrors).length === 1 ? '' : 's'}.`)
      setActiveStep(firstStep)
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
        timeZone: timeZoneVal,
        zipCode,
        address,
        description,
        dateOfBirth
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
      } else if (lowerMsg.includes('date of birth') || lowerMsg.includes('age must')) {
        fieldErrors.dateOfBirth = apiMsg
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
    <motion.div {...pageTransitionProps}>
      <div className="omni-page-hero form-page-hero">
        <h2 className="add-contact-title">{isViewMode ? 'View Contact' : (isEditMode ? 'Edit Contact' : 'Add New Contact')}</h2>
        <p>{isViewMode ? 'Everything recorded about this contact.' : 'Basic details, address and notes. Fields marked * are required; administrators choose them under OmniConnect Settings › Contacts.'}</p>
      </div>

      <div className="add-contact-card">
        {/* Step index buttons indicators */}
        <Stepper
          steps={steps}
          activeStep={activeStep}
          onChangeStep={setActiveStep}
        />

        {/* noValidate: the inline messages below (the server's own rules) replace the browser's bubbles. */}
        <form onSubmit={handleSubmit} noValidate>
          <fieldset disabled={isViewMode} className="contact-form-fieldset">
            <div className="add-contact-body">
            {/* Tab 1: Contact Details */}
            {activeStep === 0 && (
              <div className="fade-in">
                {/* Status, Source, Assigned row */}
                <div className="add-contact-top-grid">
                  <div className="form-group form-group-required">
                    <label className="form-label">Status</label>
                    <SearchableSelect
                      searchThreshold={1}
                      className={errors.statusVal ? 'is-invalid' : undefined}
                      placeholder="Select Status"
                      label="Status"
                      value={statusVal}
                      options={statuses.map(s => ({ value: String(s.id), label: s.name }))}
                      onChange={setStatusVal}
                    />
                    {errors.statusVal && <span className="invalid-feedback">{errors.statusVal}</span>}
                  </div>

                  <div className="form-group form-group-required">
                    <label className="form-label">Source</label>
                    <SearchableSelect
                      searchThreshold={1}
                      className={errors.sourceVal ? 'is-invalid' : undefined}
                      placeholder="Select Source"
                      label="Source"
                      value={sourceVal}
                      options={sources.map(s => ({ value: String(s.id), label: s.name }))}
                      onChange={setSourceVal}
                    />
                    {errors.sourceVal && <span className="invalid-feedback">{errors.sourceVal}</span>}
                  </div>

                  <div className={requiredClass('assignedTo')}>
                    <label className="form-label">Assigned</label>
                    <SearchableSelect
                      searchThreshold={1}
                      placeholder="Select Assigned Name"
                      label="Assigned"
                      value={assignedVal}
                      options={assignedUsers.map(u => ({ value: u.name, label: u.name }))}
                      onChange={setAssignedVal}
                    />
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

                  <div className={requiredClass('company')}>
                    <label className="form-label" htmlFor="contact-company">Company</label>
                    <input
                      id="contact-company"
                      type="text"
                      className={`form-control ${errors.company ? 'is-invalid' : ''}`}
                      placeholder="Enter company…"
                      value={company}
                      maxLength={maxLengthOf('company')}
                      onChange={(e) => setCompany(e.target.value)}
                      disabled={isSaving}
                      autoComplete="organization"
                    />
                    {errors.company && <span className="invalid-feedback">{errors.company}</span>}
                  </div>

                  <div className="form-group form-group-required">
                    <label className="form-label">Type</label>
                    <SearchableSelect
                      searchThreshold={1}
                      className={errors.typeVal ? 'is-invalid' : undefined}
                      placeholder="Select Type"
                      label="Type"
                      value={typeVal}
                      options={types.map(t => ({ value: String(t.id), label: t.name }))}
                      onChange={setTypeVal}
                    />
                    {errors.typeVal && <span className="invalid-feedback">{errors.typeVal}</span>}
                  </div>

                  <div className={requiredClass('email')}>
                    <label className="form-label" htmlFor="contact-email">Email</label>
                    <input
                      id="contact-email"
                      type="email"
                      className={`form-control ${errors.email ? 'is-invalid' : ''}`}
                      placeholder="name@example.com…"
                      value={email}
                      maxLength={maxLengthOf('email')}
                      onChange={(e) => setEmail(e.target.value)}
                      disabled={isSaving}
                      autoComplete="email"
                      spellCheck={false}
                    />
                    {errors.email && <span className="invalid-feedback">{errors.email}</span>}
                  </div>

                  <div className={requiredClass('dateOfBirth')}>
                    <label className="form-label" htmlFor="contact-dob">Date of birth</label>
                    <input
                      id="contact-dob"
                      type="date"
                      className={`form-control ${errors.dateOfBirth ? 'is-invalid' : ''}`}
                      value={dateOfBirth}
                      min={dobBounds.min}
                      max={dobBounds.max}
                      onChange={(e) => setDateOfBirth(e.target.value)}
                      disabled={isSaving}
                      autoComplete="bday"
                    />
                    {errors.dateOfBirth
                      ? <span className="invalid-feedback">{errors.dateOfBirth}</span>
                      : age !== null && <span className="form-hint">Age {age}</span>}
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

                  <div className={requiredClass('website')}>
                    <label className="form-label" htmlFor="contact-website">Website</label>
                    <input
                      id="contact-website"
                      type="url"
                      className={`form-control ${errors.website ? 'is-invalid' : ''}`}
                      placeholder="example.com…"
                      value={website}
                      maxLength={maxLengthOf('website')}
                      onChange={(e) => setWebsite(e.target.value)}
                      spellCheck={false}
                    />
                    {errors.website && <span className="invalid-feedback">{errors.website}</span>}
                  </div>

                  <div className="form-group">
                    <label className="form-label">Default Language</label>
                    <SearchableSelect
                      searchThreshold={1}
                      placeholder="Select Language"
                      label="Default Language"
                      value={languageVal}
                      options={languages.map(l => ({ value: l.name, label: l.name }))}
                      onChange={setLanguageVal}
                    />
                  </div>

                  {/* Groups field */}
                  <div className="form-group">
                    <label className="form-label">Assign to Groups</label>
                    <SearchableSelect
                      searchThreshold={1}
                      placeholder="Select Groups"
                      label="Assign to Groups"
                      value={selectedGroups}
                      options={groups.map(g => ({ value: String(g.id), label: g.name }))}
                      onChange={setSelectedGroups}
                    />
                  </div>
                </div>
              </div>
            )}

            {/* Tab 2: Other Details */}
            {activeStep === 1 && (
              <div className="fade-in">
                <div className="add-contact-form-grid">
                  <div className={requiredClass('city')}>
                    <label className="form-label" htmlFor="contact-city">City</label>
                    <input
                      id="contact-city"
                      type="text"
                      className={`form-control ${errors.city ? 'is-invalid' : ''}`}
                      placeholder="Enter City"
                      value={city}
                      maxLength={maxLengthOf('city')}
                      onChange={(e) => setCity(e.target.value)}
                    />
                    {errors.city && <span className="invalid-feedback">{errors.city}</span>}
                  </div>

                  <div className={requiredClass('state')}>
                    <label className="form-label" htmlFor="contact-state">State</label>
                    <input
                      id="contact-state"
                      type="text"
                      className={`form-control ${errors.state ? 'is-invalid' : ''}`}
                      placeholder="Enter State"
                      value={stateVal}
                      maxLength={maxLengthOf('state')}
                      onChange={(e) => setStateVal(e.target.value)}
                    />
                    {errors.state && <span className="invalid-feedback">{errors.state}</span>}
                  </div>

                  <div className={requiredClass('country')}>
                    <label className="form-label">Country</label>
                    <SearchableSelect
                      searchThreshold={1}
                      placeholder="Select Country"
                      label="Country"
                      value={countryVal}
                      disabled={isViewMode}
                      // The dial code follows the country, exactly as it did on the native select.
                      options={ALL_COUNTRIES.map(c => ({
                        value: c.name,
                        label: c.name,
                        // Lets someone type "+44" or "GB" and still find the United Kingdom.
                        keywords: `${c.code} ${c.dialCode ?? ''}`
                      }))}
                      onChange={(val) => {
                        setCountryVal(val)
                        const matched = ALL_COUNTRIES.find(c => c.name === val || c.code === val)
                        if (matched && matched.dialCode) {
                          setSelectedDialCodeVal(matched.dialCode)
                        }
                      }}
                    />
                  </div>

                  <div className={requiredClass('timeZone')}>
                    <label className="form-label">Time zone</label>
                    <SearchableSelect
                      searchThreshold={1}
                      placeholder="Account default"
                      label="Time zone"
                      value={timeZoneVal}
                      disabled={isViewMode}
                      options={timeZones.map(z => ({ value: z.value, label: z.label }))}
                      onChange={setTimeZoneVal}
                    />
                  </div>

                  <div className={requiredClass('zipCode')}>
                    <label className="form-label" htmlFor="contact-zipCode">Zip Code</label>
                    <input
                      id="contact-zipCode"
                      type="text"
                      className={`form-control ${errors.zipCode ? 'is-invalid' : ''}`}
                      placeholder="Enter Zip Code"
                      value={zipCode}
                      maxLength={maxLengthOf('zipCode')}
                      onChange={(e) => setZipCode(e.target.value)}
                    />
                    {errors.zipCode && <span className="invalid-feedback">{errors.zipCode}</span>}
                  </div>
                </div>

                <div className={`${requiredClass('address')} margin-top-20`}>
                  <label className="form-label" htmlFor="contact-address">Address</label>
                  <textarea
                    id="contact-address"
                    className={`form-control ${errors.address ? 'is-invalid' : ''}`}
                    placeholder="Enter Address"
                    rows={3}
                    value={address}
                    maxLength={maxLengthOf('address')}
                    onChange={(e) => setAddress(e.target.value)}
                  />
                  {errors.address && <span className="invalid-feedback">{errors.address}</span>}
                </div>

                <div className={`${requiredClass('description')} margin-top-20`}>
                  <label className="form-label" htmlFor="contact-description">Description</label>
                  <textarea
                    id="contact-description"
                    className={`form-control ${errors.description ? 'is-invalid' : ''}`}
                    placeholder="Enter Description"
                    rows={3}
                    value={description}
                    maxLength={maxLengthOf('description')}
                    onChange={(e) => setDescription(e.target.value)}
                  />
                  {errors.description && <span className="invalid-feedback">{errors.description}</span>}
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
    </motion.div>
  )
}
export default AddContact
