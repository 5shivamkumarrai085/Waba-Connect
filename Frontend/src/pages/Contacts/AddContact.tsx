import React, { useState, useEffect } from 'react'
import { useNavigate, useParams, useLocation } from 'react-router-dom'
import { useContactStore } from '../../store/contactStore'
import { contactService } from '../../services/contacts/contactService'
import { Stepper } from '../../components/Stepper/Stepper'
import toast from 'react-hot-toast'
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
  const [countriesList, setCountriesList] = useState<any[]>([])

  // Form Fields
  const [statusVal, setStatusVal] = useState('')
  const [sourceVal, setSourceVal] = useState('')
  const [assignedVal, setAssignedVal] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [company, setCompany] = useState('')
  const [typeVal, setTypeVal] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
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

  useEffect(() => {
    const fetchOptionsAndContact = async () => {
      try {
        const [
          fetchedStatuses,
          fetchedSources,
          fetchedAssigned,
          fetchedTypes,
          fetchedLanguages,
          fetchedGroups,
          fetchedCountries
        ] = await Promise.all([
          contactService.getContactStatuses(),
          contactService.getContactSources(),
          contactService.getAssignedUsers(),
          contactService.getContactTypes(),
          contactService.getContactLanguages(),
          contactService.getContactGroups(),
          contactService.getContactCountries()
        ])

        setStatuses(fetchedStatuses)
        setSources(fetchedSources)
        setAssignedUsers(fetchedAssigned)
        setTypes(fetchedTypes)
        setLanguages(fetchedLanguages)
        setGroups(fetchedGroups)
        setCountriesList(fetchedCountries)

        if (id) {
          const contact = await contactService.getContactById(parseInt(id, 10))
          if (contact) {
            setStatusVal(contact.status || '')
            setSourceVal(contact.source || '')
            setAssignedVal(contact.assignedUser?.id || '')
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
            setPhone(contact.phone || '')
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
        console.error('Error fetching form details dropdown values:', err)
      }
    }

    fetchOptionsAndContact()
  }, [id])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    
    // Quick validation on required fields
    const newErrors: Record<string, string> = {}
    if (!firstName) newErrors.firstName = 'First Name is required.'
    if (!lastName) newErrors.lastName = 'Last Name is required.'
    if (!phone) newErrors.phone = 'Phone is required.'
    if (!typeVal) newErrors.typeVal = 'Type is required.'
    if (!statusVal) newErrors.statusVal = 'Status is required.'
    if (!sourceVal) newErrors.sourceVal = 'Source is required.'

    if (Object.keys(newErrors).length > 0) {
      setErrors(newErrors)
      toast.error('Please fill out all required fields marked with an asterisk (*).')
      setActiveStep(0) // redirect back to first step to correct
      return
    }

    setErrors({})
    setIsSaving(true)

    try {
      const payload = {
        status: statusVal,
        source: sourceVal,
        assigned: assignedVal,
        firstName,
        lastName,
        company,
        type: typeVal,
        email,
        phone,
        website,
        language: languageVal,
        groups: selectedGroups,
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

  const getFlagEmoji = (code: string) => {
    switch (code) {
      case 'IN': return '🇮🇳'
      case 'MY': return '🇲🇾'
      case 'SG': return '🇸🇬'
      case 'US': return '🇺🇸'
      case 'GB': return '🇬🇧'
      default: return '🏳️'
    }
  }

  const getSelectedDialCode = () => {
    const sortedCountries = [...countriesList].sort((a, b) => b.dialCode.length - a.dialCode.length);
    for (const c of sortedCountries) {
      if (phone.startsWith(c.dialCode)) {
        return c.dialCode;
      }
    }
    return '+91'; // default to India (+91)
  }

  const handlePhoneCountryChange = (dialCode: string) => {
    let nationalNumber = phone;
    const sortedCountries = [...countriesList].sort((a, b) => b.dialCode.length - a.dialCode.length);
    for (const c of sortedCountries) {
      if (phone.startsWith(c.dialCode)) {
        nationalNumber = phone.slice(c.dialCode.length);
        break;
      }
    }
    nationalNumber = nationalNumber.replace(/^\+/, '');
    setPhone(dialCode + nationalNumber);
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
                      disabled={isSaving}
                      required
                    >
                      <option value="">Select Status</option>
                      {statuses.map(s => (
                        <option key={s.id} value={s.name}>{s.name}</option>
                      ))}
                    </select>
                    {errors.statusVal && <span className="invalid-feedback">{errors.statusVal}</span>}
                  </div>

                  <div className="form-group form-group-required">
                    <label className="form-label">Source</label>
                    <select
                      className={`form-control ${errors.sourceVal ? 'is-invalid' : ''}`}
                      value={sourceVal}
                      onChange={(e) => setSourceVal(e.target.value)}
                      disabled={isSaving}
                      required
                    >
                      <option value="">Select Source</option>
                      {sources.map(s => (
                        <option key={s.id} value={s.name}>{s.name}</option>
                      ))}
                    </select>
                    {errors.sourceVal && <span className="invalid-feedback">{errors.sourceVal}</span>}
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
                        <option key={u.id} value={u.id}>{u.name}</option>
                      ))}
                    </select>
                  </div>
                </div>

                {/* Main details grid */}
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
                    />
                  </div>

                  <div className="form-group form-group-required">
                    <label className="form-label">Type</label>
                    <select
                      className={`form-control ${errors.typeVal ? 'is-invalid' : ''}`}
                      value={typeVal}
                      onChange={(e) => setTypeVal(e.target.value)}
                      disabled={isSaving}
                      required
                    >
                      <option value="">Select Type</option>
                      {types.map(t => (
                        <option key={t.id} value={t.name}>{t.name}</option>
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

                  <div className="form-group form-group-required">
                    <label className="form-label">Phone</label>
                    <div className="phone-input-container">
                      <select
                        className="phone-country-select"
                        value={getSelectedDialCode()}
                        onChange={(e) => handlePhoneCountryChange(e.target.value)}
                      >
                        {countriesList.length === 0 ? (
                          <option value="+91">🇮🇳 IN (+91)</option>
                        ) : (
                          countriesList.map((c) => (
                            <option key={c.id} value={c.dialCode}>
                              {getFlagEmoji(c.code)} {c.code} ({c.dialCode})
                            </option>
                          ))
                        )}
                      </select>
                      <input
                        type="text"
                        className={`form-control phone-input ${errors.phone ? 'is-invalid' : ''}`}
                        placeholder=""
                        value={phone}
                        onChange={(e) => setPhone(e.target.value)}
                        disabled={isSaving}
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
                        <option key={g.id} value={g.name}>{g.name}</option>
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
                        const matched = countriesList.find(c => c.id === val || c.name === val)
                        if (matched && matched.dialCode) {
                          setPhone(matched.dialCode)
                        }
                      }}
                      disabled={isViewMode}
                    >
                      <option value="">Select Country</option>
                      {countriesList.map(c => (
                        <option key={c.id} value={c.name}>{c.name}</option>
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

            {/* Tab 3: Notes (Created placeholder alert) */}
            {activeStep === 2 && (
              <div className="fade-in notes-tab-notice">
                Notes will be available once the contact is created.
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
