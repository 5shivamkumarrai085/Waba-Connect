import React from 'react'
import './Avatar.css'

interface AvatarProps {
  name: string
  url?: string
  size?: 'small' | 'medium' | 'large'
}

export const Avatar: React.FC<AvatarProps> = ({
  name,
  url,
  size = 'medium'
}) => {
  const getInitials = (fullName: string) => {
    const parts = fullName.split(' ')
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase()
    }
    return fullName.substring(0, 2).toUpperCase()
  }

  const sizeClass = size === 'large' ? 'avatar-large' : size === 'small' ? 'avatar-small' : ''

  return (
    <div className={`avatar-wrapper ${sizeClass}`} {...(name ? { title: name } : {})}>
      {url ? (
        <img src={url} alt={name} />
      ) : name ? (
        <span>{getInitials(name)}</span>
      ) : (
        <svg viewBox="0 0 24 24" width="100%" height="100%" fill="currentColor" style={{ color: '#cbd5e1', backgroundColor: '#f1f5f9', display: 'block' }}>
          <path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v3h16v-3c0-2.66-5.33-4-8-4z" />
        </svg>
      )}
    </div>
  )
}
export default Avatar
