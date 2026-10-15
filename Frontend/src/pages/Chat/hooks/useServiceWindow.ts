import { useEffect, useState } from 'react'
import { formatRemaining } from '../chatFormat'

export interface ServiceWindowStatus {
  /** Free-form replies are allowed right now. */
  open: boolean
  /** "5h 12m left", "Closed", or "Not opened yet" (the customer has never written). */
  label: string
  /** Under an hour left: shown with a warning tone. */
  closingSoon: boolean
}

/**
 * WhatsApp's customer service window for the open conversation, from the server's
 * `serviceWindowEndsAt`. Re-evaluated every 30 seconds, so the chip counts down and the composer
 * locks the moment the window closes rather than after the next message arrives.
 */
export function useServiceWindow(endsAt: string | null | undefined): ServiceWindowStatus {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (!endsAt) return
    const timer = window.setInterval(() => setNow(Date.now()), 30_000)
    return () => window.clearInterval(timer)
  }, [endsAt])

  if (!endsAt) return { open: false, label: 'Not opened yet', closingSoon: false }

  const remaining = new Date(endsAt).getTime() - now
  if (remaining <= 0) return { open: false, label: 'Reply window closed', closingSoon: false }
  return { open: true, label: `${formatRemaining(remaining)} left to reply`, closingSoon: remaining < 3_600_000 }
}
