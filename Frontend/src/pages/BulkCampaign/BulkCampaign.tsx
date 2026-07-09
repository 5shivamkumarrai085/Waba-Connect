import React, { useState } from 'react'
import { UploadArea } from '../../components/UploadArea/UploadArea'
import { campaignUploadService } from '../../services/campaigns/campaignUploadService'
import toast from 'react-hot-toast'
import './BulkCampaign.css'

export const BulkCampaign: React.FC = () => {
  const [campaignName, setCampaignName] = useState('')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [isUploading, setIsUploading] = useState(false)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!campaignName) {
      toast.error('Please enter a Campaign Name.')
      return
    }
    if (!selectedFile) {
      toast.error('Please choose a CSV file.')
      return
    }

    setIsUploading(true)
    try {
      const res = await campaignUploadService.uploadCsv(campaignName, selectedFile)
      if (res.success) {
        toast.success(res.message)
        // Reset form
        setCampaignName('')
        setSelectedFile(null)
      } else {
        toast.error(res.message)
      }
    } catch (err) {
      toast.error('Error uploading bulk campaign CSV.')
    } finally {
      setIsUploading(false)
    }
  }

  return (
    <div className="fade-in bulk-campaign-container">
      <h2 className="bulk-campaign-title">Campaigns from CSV File</h2>

      <div className="bulk-card-wrapper">
        <form onSubmit={handleSubmit}>
          <div className="bulk-card-body">
            <h3 className="bulk-card-title">Campaign</h3>

            {/* Campaign Name Field */}
            <div className="form-group form-group-required">
              <label className="form-label">Campaign Name</label>
              <input
                type="text"
                className="form-control"
                placeholder="Enter campaign name"
                value={campaignName}
                onChange={(e) => setCampaignName(e.target.value)}
                required
              />
            </div>

            {/* Reusable UploadArea Component */}
            <div className="margin-top-20">
              <UploadArea
                selectedFile={selectedFile}
                onFileSelect={setSelectedFile}
              />
            </div>

            {/* Upload submit button */}
            <div className="bulk-upload-footer">
              <button
                type="submit"
                className="btn-bulk-upload"
                disabled={isUploading || !campaignName || !selectedFile}
              >
                {isUploading ? 'Uploading...' : 'Upload'}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  )
}
export default BulkCampaign
