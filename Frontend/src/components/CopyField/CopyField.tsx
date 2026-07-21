import React, { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import './CopyField.css'

interface CopyFieldProps {
  value: string
  placeholder?: string
  readOnly?: boolean
  isSensitive?: boolean
}

export const CopyField: React.FC<CopyFieldProps> = ({
  value,
  placeholder = '',
  readOnly = true,
  isSensitive = false
}) => {
  const [copied, setCopied] = useState<boolean>(false)
  const [isHidden, setIsHidden] = useState<boolean>(isSensitive)

  const handleCopy = async () => {
    if (!value) return
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      setTimeout(() => {
        setCopied(false)
      }, 2000)
    } catch (err) {
    }
  }

  return (
    <div className="copy-field-container">
      <div className="copy-field-input-wrapper" style={{ display: 'flex', alignItems: 'center', width: '100%' }}>
        <input
          type={isHidden ? 'password' : 'text'}
          className="copy-field-input"
          value={value}
          placeholder={placeholder}
          readOnly={readOnly}
          style={{ flex: 1 }}
        />
        {isSensitive && (
          <button
            type="button"
            className="copy-field-visibility-btn"
            onClick={() => setIsHidden(!isHidden)}
            style={{
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              padding: '0 8px',
              display: 'flex',
              alignItems: 'center',
              color: 'var(--text-muted)'
            }}
            title={isHidden ? 'Show' : 'Hide'}
          >
            {isHidden ? <Eye size={16} /> : <EyeOff size={16} />}
          </button>
        )}
      </div>
      <button
        type="button"
        className={`copy-field-btn ${copied ? 'copied' : ''}`}
        onClick={handleCopy}
      >
        {copied ? 'Copied!' : 'Copy'}
      </button>
    </div>
  )
}
export default CopyField
