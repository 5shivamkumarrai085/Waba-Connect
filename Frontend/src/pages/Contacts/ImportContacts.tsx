import React, { useState } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import { useContactStore } from '../../store/contactStore'
import { UploadArea } from '../../components/UploadArea/UploadArea'
import { apiClient } from '../../services/apiClient'
import { Download, X } from 'lucide-react'
import toast from 'react-hot-toast'
import './ImportContacts.css'
import { getErrorMessage } from '../../utils/errorHelper'

export const ImportContacts: React.FC = () => {
  const navigate = useNavigate()
  const { importContacts, loadContacts } = useContactStore()
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [isUploading, setIsUploading] = useState<boolean>(false)
  const [isSampleModalOpen, setIsSampleModalOpen] = useState<boolean>(false)

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedFile) {
      toast.error('Please choose a CSV file first.')
      return
    }

    setIsUploading(true)
    try {
      const res = await importContacts(selectedFile)
      if (res.success) {
        toast.success(res.message)
        await loadContacts()
        navigate('/contacts')
      } else {
        toast.error(res.message)
      }
    } catch (err: any) {
      toast.error(getErrorMessage(err, 'Error importing contacts.'))
    } finally {
      setIsUploading(false)
    }
  }

  const handleDownloadSample = () => {
    const cleanBase = apiClient.defaults.baseURL?.endsWith('/api')
      ? apiClient.defaults.baseURL.slice(0, -4)
      : apiClient.defaults.baseURL
    window.open(`${cleanBase}/api/Contacts/csv-sample`, '_blank')
  }

  return (
    <div className="fade-in import-contacts-container">
      <h2 className="import-contacts-title">Import contacts from CSV file</h2>

      <div className="import-card-wrapper">
        <form onSubmit={handleUpload}>
          <div className="import-card-body">
            <h3 className="import-card-title">Import Contact</h3>
            
            {/* Reusable Upload Area Dropzone */}
            <UploadArea
              selectedFile={selectedFile}
              onFileSelect={setSelectedFile}
              onDownloadSampleClick={() => setIsSampleModalOpen(true)}
            />
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
      {isSampleModalOpen && createPortal(
        <div className="modal-overlay-custom" onClick={() => setIsSampleModalOpen(false)}>
          <div className="modal-content-custom" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header-custom">
              <h4 className="modal-title-custom">Download Sample</h4>
              <button 
                type="button" 
                className="btn-modal-close-custom"
                onClick={() => setIsSampleModalOpen(false)}
              >
                <X size={18} />
              </button>
            </div>

            <div className="modal-body-custom">
              {/* Alert Instructions Box */}
              <div className="sample-rules-alert margin-bottom-20">
                <p className="rule-item">
                  <strong>1. Phone Number Column Requirement:</strong> Your CSV file must include a column named phone. Each record in this column should contain a valid contact number, correctly formatted with the country code, including the '+' sign.
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
                >
                  <Download size={14} />
                  <span>Download Sample</span>
                </button>
              </div>

              {/* CSV Columns Sample Table */}
              <div className="table-responsive">
                <table className="sample-csv-table">
                  <thead>
                    <tr>
                      <th><span className="required-asterisk">*</span> STATUS_ID</th>
                      <th><span className="required-asterisk">*</span> SOURCE_ID</th>
                      <th>ASSIGNED_ID</th>
                      <th><span className="required-asterisk">*</span> FIRST NAME</th>
                      <th><span className="required-asterisk">*</span> LAST NAME</th>
                      <th>COMPANY</th>
                      <th><span className="required-asterisk">*</span> TYPE</th>
                      <th>EMAIL</th>
                      <th><span className="required-asterisk">*</span> PHONE</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td>2</td>
                      <td>4</td>
                      <td>1</td>
                      <td>Sample Data</td>
                      <td>Sample Data</td>
                      <td>Sample Data</td>
                      <td>lead/customer</td>
                      <td>abc@gmail.com</td>
                      <td>+1 555 123 4567</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>

            <div className="modal-footer-custom justify-end">
              <button 
                type="button" 
                className="btn-cancel"
                onClick={() => setIsSampleModalOpen(false)}
              >
                Cancel
              </button>
            </div>
          </div>
        </div>,
        document.body
      )}
    </div>
  )
}
export default ImportContacts
