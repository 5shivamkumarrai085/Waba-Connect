import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/layout.css'
import './styles/components.css'
import './styles/dashboard.css'
import './styles/campaign.css'
import './styles/Skeleton.css'
import './styles/responsive.css'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
