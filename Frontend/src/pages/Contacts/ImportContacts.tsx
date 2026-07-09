import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useContactStore } from '../../store/contactStore'
import { UploadArea } from '../../components/UploadArea/UploadArea'
import toast from 'react-hot-toast'
import './ImportContacts.css'

export const ImportContacts: React.FC = () => {
  const navigate = useNavigate()
  const { importContacts, loadContacts } = useContactStore()
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [isUploading, setIsUploading] = useState<boolean>(false)

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!selectedFile) {
      toast.error('Please choose a CSV file first.')
      return
    }

    setIsUploading(true)
    try {
      // Read file content
      const reader = new FileReader()
      reader.onload = async (event) => {
        const text = event.target?.result as string
        const res = await importContacts(text)
        if (res.success) {
          toast.success(res.message)
          await loadContacts()
          navigate('/contacts')
        } else {
          toast.error(res.message)
        }
      }
      reader.readAsText(selectedFile)
    } catch (err) {
      toast.error('Error reading CSV file.')
    } finally {
      setIsUploading(false)
    }
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
    </div>
  )
}
export default ImportContacts
