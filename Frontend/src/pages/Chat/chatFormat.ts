import type { Message } from '../../types/chat'

/** A date divider goes before the first message and wherever the calendar day changes. */
export const startsNewDay = (message: Message, previous?: Message): boolean =>
  !previous || new Date(message.createdAt).toDateString() !== new Date(previous.createdAt).toDateString()

/** "Today", "Yesterday", or the date in the agent's own locale ("14 October 2026", "October 14, 2026"…). */
export const formatDayDivider = (value: string, now: Date = new Date()): string => {
  const date = new Date(value)
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const day = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  const diffDays = Math.round((today.getTime() - day.getTime()) / 86_400_000)

  const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
  if (diffDays === 0 || diffDays === 1) {
    const word = relative.format(-diffDays, 'day')
    return word.charAt(0).toUpperCase() + word.slice(1)
  }
  return new Intl.DateTimeFormat(undefined, {
    day: 'numeric',
    month: 'long',
    year: date.getFullYear() === now.getFullYear() ? undefined : 'numeric'
  }).format(date)
}

/** "5h 12m" / "38m" for the time left in a window. */
export const formatRemaining = (ms: number): string => {
  const totalMinutes = Math.max(0, Math.floor(ms / 60_000))
  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`
}
