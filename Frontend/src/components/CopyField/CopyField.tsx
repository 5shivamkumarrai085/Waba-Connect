import React, { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import './CopyField.css'

interface CopyFieldProps {
  value: string
  placeholder?: string
  readOnly?: boolean
  isSensitive?: boolean
  /**
   * When false, a sensitive value can never be unmasked or copied — the eye and the Copy
   * button are both removed.
   *
   * Revealing and copying are the same capability: a Copy button beside a masked field hands
   * over the secret just as effectively as unmasking it, so they are gated together rather
   * than separately.
   */
  allowReveal?: boolean
}

export const CopyField: React.FC<CopyFieldProps> = ({
  value,
  placeholder = '',
  readOnly = true,
  isSensitive = false,
  allowReveal = true
}) => {
  const [copied, setCopied] = useState<boolean>(false)
  const [isHidden, setIsHidden] = useState<boolean>(isSensitive)

  // A sensitive field the caller has locked down stays masked regardless of local state.
  const locked = isSensitive && !allowReveal
  const showValueMasked = locked || isHidden

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
          type={showValueMasked ? 'password' : 'text'}
          className="copy-field-input"
          value={value}
          placeholder={placeholder}
          readOnly={readOnly}
          style={{ flex: 1 }}
        />
        {isSensitive && !locked && (
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
      {!locked && (
        <button
          type="button"
          className={`copy-field-btn ${copied ? 'copied' : ''}`}
          onClick={handleCopy}
        >
          {copied ? 'Copied!' : 'Copy'}
        </button>
      )}
    </div>
  )
}
export default CopyField
