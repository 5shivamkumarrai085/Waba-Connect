import React, { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import { pageTransitionProps } from '../../utils/motion'
import { useNavigate } from 'react-router-dom'
import { useContactStore } from '../../store/contactStore'
import { UploadArea } from '../../components/UploadArea/UploadArea'
import { Modal } from '../../components/Modal/Modal'
import { CsvRowErrors } from '../../components/CsvRowErrors/CsvRowErrors'
import { downloadFromApi } from '../../utils/downloadFile'
import { contactService } from '../../services/contacts/contactService'
import type { ContactImportSummary, CsvContactLayout } from '../../services/contacts/contactService'
import { Download } from 'lucide-react'
import toast from 'react-hot-toast'
import './ImportContacts.css'
import { getErrorMessage } from '../../utils/errorHelper'

export const ImportContacts: React.FC = () => {
  const navigate = useNavigate()
  const { importContacts, loadContacts } = useContactStore()
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [isUploading, setIsUploading] = useState<boolean>(false)
  const [isSampleModalOpen, setIsSampleModalOpen] = useState<boolean>(false)
  const [importSummary, setImportSummary] = useState<ContactImportSummary | null>(null)
  const [isDownloadingSample, setIsDownloadingSample] = useState<boolean>(false)
  // Which columns are required follows Settings › Contacts, so the dialog asks the server rather
  // than listing them here; loaded when the dialog first opens.
  const [layout, setLayout] = useState<CsvContactLayout | null>(null)
  const [layoutError, setLayoutError] = useState<string | null>(null)

  useEffect(() => {
    if (!isSampleModalOpen || layout) return
    let cancelled = false
    setLayoutError(null)
    contactService.getCsvLayout()
      .then(result => { if (!cancelled) setLayout(result) })
      .catch(err => { if (!cancelled) setLayoutError(getErrorMessage(err, 'Could not load the column list.')) })
    return () => { cancelled = true }
  }, [isSampleModalOpen, layout])

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedFile) {
      toast.error('Please choose a CSV file first.')
      return
    }

    setIsUploading(true)
    setImportSummary(null)
    try {
      const res = await importContacts(selectedFile)
      setImportSummary(res.result)

      if (!res.success) {
        // Whole-file rejection: no columns matched, wrong file type, server error.
        toast.error(res.message)
        return
      }

      const rejected = res.result?.invalidCount ?? 0
      if (rejected > 0) {
        // Stay on the page. Navigating away here is what made the old behaviour so hard to
        // work with — the user would never see which rows failed or why.
        toast.error(res.message)
        await loadContacts()
        return
      }

      toast.success(res.message)
      await loadContacts()
      navigate('/contacts')
    } catch (err: any) {
      toast.error(getErrorMessage(err, 'Error importing contacts.'))
    } finally {
      setIsUploading(false)
    }
  }

  const handleDownloadSample = async () => {
    // Goes through the API client so the bearer token is attached. This used to be a bare
    // window.open, which is a plain browser navigation carrying no Authorization header — the
    // endpoint requires Contact.Import, so it answered 401 and downloaded nothing.
    setIsDownloadingSample(true)
    try {
      await downloadFromApi('/Contacts/csv-sample', { fallbackFilename: 'contacts_sample.csv' })
    } catch (err) {
      toast.error(getErrorMessage(err, 'Could not download the sample file.'))
    } finally {
      setIsDownloadingSample(false)
    }
  }

  return (
    <motion.div className="import-contacts-container" {...pageTransitionProps}>
      <div className="omni-page-hero form-page-hero">
        <h2 className="import-contacts-title">Import contacts from CSV file</h2>
        <p>Upload a CSV in the sample's layout; rows with problems are listed so you can fix and re-upload them.</p>
      </div>

      <div className="import-card-wrapper">
        <form onSubmit={handleUpload}>
          <div className="import-card-body">
            <h3 className="import-card-title">Import Contact</h3>
            
            {/* Reusable Upload Area Dropzone */}
            <UploadArea
              selectedFile={selectedFile}
              onFileSelect={(file) => {
                setSelectedFile(file)
                // Results belong to the previous file; keeping them next to a new one would
                // read as though the new file had already been checked.
                setImportSummary(null)
              }}
              onDownloadSampleClick={() => setIsSampleModalOpen(true)}
            />

            {/* What the import actually did, and every row it could not use. Same component
                the Bulk Campaign importer renders, so both report failures identically. */}
            {importSummary && (
              <CsvRowErrors
                summary={
                  <>
                    Out of the {importSummary.totalRecords} record(s) in your CSV file,{' '}
                    <strong className="csv-summary-strong">{importSummary.importedCount}</strong>{' '}
                    {importSummary.importedCount === 1 ? 'contact was' : 'contacts were'} imported.
                    {importSummary.skippedDuplicates > 0 && (
                      <> {importSummary.skippedDuplicates} already existed and {importSummary.skippedDuplicates === 1 ? 'was' : 'were'} skipped.</>
                    )}
                  </>
                }
                summaryIsWarning={importSummary.importedCount === 0}
                errors={importSummary.errors || []}
              />
            )}
          </div>

          {/* Stepper/Upload footer actions bar */}
          <div className="import-card-footer">
            <button
              type="button"
              className="btn-cancel"
              onClick={() => navigate('/contacts')}
            >
              Cancel
            </button>
            <button
              type="submit"
              className="btn-upload"
              disabled={!selectedFile || isUploading}
            >
              {isUploading ? 'Uploading...' : 'Upload'}
            </button>
          </div>
        </form>
      </div>

      {/* Download Sample modal popup dialog */}
      <Modal
        isOpen={isSampleModalOpen}
        onClose={() => setIsSampleModalOpen(false)}
        title="Download Sample"
        size="lg"
        footer={
          <button
            type="button"
            className="oc-dialog-btn oc-dialog-btn-secondary"
            onClick={() => setIsSampleModalOpen(false)}
          >
            Cancel
          </button>
        }
      >
            <div className="modal-body-custom">
              {/* Alert Instructions Box */}
              <div className="sample-rules-alert margin-bottom-20">
                <p className="rule-item">
                  <strong>1. Required columns:</strong> columns marked * must be present and filled in on every row. Phone numbers need the country code with a leading '+'; dates are written as YYYY-MM-DD.
                </p>
                <p className="rule-item">
                  <strong>2. CSV Format and Encoding:</strong> Your CSV data should follow the specified format. The first row of your CSV file must contain the column headers, as shown in the example table. Ensure that your file is encoded in UTF-8 to prevent any encoding issues.
                </p>
              </div>

              {/* Table section header with green download button */}
              <div className="modal-table-header margin-bottom-15">
                <span className="modal-table-title">Contact</span>
                <button
                  type="button"
                  className="btn-download-sample-modal"
                  onClick={handleDownloadSample}
                  disabled={isDownloadingSample}
                >
                  <Download size={14} />
                  <span>{isDownloadingSample ? 'Downloading...' : 'Download Sample'}</span>
                </button>
              </div>

              {/* Columns and required markers come from the server (the same layout the
                  importer enforces), so this table cannot drift from the rules. */}
              <div className="table-responsive">
                {layout ? (
                  <table className="sample-csv-table">
                    <thead>
                      <tr>
                        {layout.columns.map(column => (
                          <th key={column.header} scope="col" title={column.label}>
                            {column.required && <span className="required-asterisk" aria-label="required">* </span>}
                            {column.header}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      <tr>
                        {layout.columns.map(column => <td key={column.header}>{column.example}</td>)}
                      </tr>
                    </tbody>
                  </table>
                ) : (
                  <p className="sample-layout-state" role={layoutError ? 'alert' : 'status'}>
                    {layoutError ?? 'Loading the column list…'}
                  </p>
                )}
              </div>
              {layout && layout.optionalColumns.length > 0 && (
                <p className="sample-optional-columns">
                  Also read when present:{' '}
                  {layout.optionalColumns.map((name, i) => (
                    <React.Fragment key={name}>{i > 0 && ' '}<code>{name}</code></React.Fragment>
                  ))}
                </p>
              )}
            </div>
      </Modal>
    </motion.div>
  )
}
export default ImportContacts
