import React, { useState, useRef } from 'react'
import { UploadCloud, FileText } from 'lucide-react'
import toast from 'react-hot-toast'
import './UploadArea.css'

interface UploadAreaProps {
  onFileSelect: (file: File) => void
  selectedFile: File | null
  onDownloadSampleClick?: () => void
}

export const UploadArea: React.FC<UploadAreaProps> = ({
  onFileSelect,
  selectedFile,
  onDownloadSampleClick
}) => {
  const [dragActive, setDragActive] = useState<boolean>(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const handleDrag = (e: React.DragEvent) => {
    e.preventDefault()
    e.stopPropagation()
    if (e.type === 'dragenter' || e.type === 'dragover') {
      setDragActive(true)
    } else if (e.type === 'dragleave') {
      setDragActive(false)
    }
  }

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    e.stopPropagation()
    setDragActive(false)
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      const file = e.dataTransfer.files[0]
      if (file.name.endsWith('.csv')) {
        onFileSelect(file)
      } else {
        toast.error('Invalid file format. Please select a CSV file.')
      }
    }
  }

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    e.preventDefault()
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0]
      if (file.name.endsWith('.csv')) {
        onFileSelect(file)
      } else {
        toast.error('Invalid file format. Please select a CSV file.')
      }
    }
  }

  const onButtonClick = () => {
    fileInputRef.current?.click()
  }

  const formatBytes = (bytes: number, decimals = 2) => {
    if (bytes === 0) return '0 Bytes'
    const k = 1024
    const dm = decimals < 0 ? 0 : decimals
    const sizes = ['Bytes', 'KB', 'MB', 'GB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i]
  }

  return (
    <div className="upload-area-card">
      <div className="upload-area-header">
        <span className="upload-area-label">Choose CSV File</span>
        <button 
          type="button"
          className="upload-area-link"
          onClick={(e) => {
            e.preventDefault()
            e.stopPropagation()
            if (onDownloadSampleClick) {
              onDownloadSampleClick()
            } else {
              toast.success('Downloading sample contacts CSV layout...')
            }
          }}
        >
          Download Sample File & Read Rules
        </button>
      </div>

      <div
        className={`upload-dropzone ${dragActive ? 'drag-active' : ''}`}
        onDragEnter={handleDrag}
        onDragOver={handleDrag}
        onDragLeave={handleDrag}
        onDrop={handleDrop}
        onClick={onButtonClick}
      >
        <input
          ref={fileInputRef}
          type="file"
          className="hidden-input"
          accept=".csv"
          onChange={handleChange}
        />

        <div className="upload-icon-wrapper">
          <UploadCloud size={44} strokeWidth={1} />
        </div>

        <p className="upload-main-text">Drag your file here or click in this area.</p>
        <p className="upload-sub-text">CSV file only</p>
      </div>

      {selectedFile && (
        <div className="upload-file-details">
          <FileText className="upload-file-icon" size={24} />
          <div className="upload-file-info">
            <span className="upload-file-name">{selectedFile.name}</span>
            <span className="upload-file-size">{formatBytes(selectedFile.size)}</span>
          </div>
        </div>
      )}
    </div>
  )
}
export default UploadArea
