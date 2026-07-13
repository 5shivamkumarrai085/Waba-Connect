import React from 'react'
import { User } from 'lucide-react'
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
        <User size={14} />
      )}
    </div>
  )
}
export default Avatar
