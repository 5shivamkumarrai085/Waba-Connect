/**
 * Fun loading quotes for the OmniConnect Dashboard.
 * Professional but with personality — like a Stripe/Linear dashboard.
 */

const loadingQuotes: string[] = [
  "Warming up the WhatsApp engines... 🚀",
  "Crunching numbers faster than your morning coffee kicks in ☕",
  "Fetching your data from the cloud... not the rainy kind 🌤️",
  "Loading campaigns... this is the calm before the conversions 📈",
  "Counting messages... each one a potential customer 💬",
  "Hold tight — great dashboards take a few milliseconds ⚡",
  "Connecting dots between your contacts and campaigns 🔗",
  "Your data is doing its morning stretch... almost ready 🧘",
  "Spinning up insights... patience is a marketing virtue 🎯",
  "Loading your empire one metric at a time 👑",
  "Just a moment — even rockets have a countdown 🚀",
  "Processing... we promise it's faster than a group chat reply ⏱️",
  "Bringing your analytics to life... no magic wand needed 🪄",
  "Good things come to those who wait 0.3 seconds ⚡",
  "Gathering intelligence... the business kind 📊",
  "Almost there — your dashboard is getting dressed up 🎨",
  "Brewing fresh data... served hot and insightful ☕",
  "Template wizardry in progress... 🧙‍♂️",
  "Making your metrics look presentable... 💅",
  "Loading... because even AI needs a dramatic pause 🎭",
]

const emptyStateQuotes: string[] = [
  "It's quiet here... too quiet 🤫",
  "Nothing to see yet — but great things start from zero 🌱",
  "Your first campaign is just a click away! 🚀",
  "Empty for now, but full of potential ✨",
  "This space is reserved for your next big idea 💡",
  "No data yet — time to make some waves 🌊",
  "A blank canvas awaiting your marketing masterpiece 🎨",
  "Zero entries — but hey, every unicorn started somewhere 🦄",
]

const searchEmptyQuotes: string[] = [
  "We looked everywhere... nada 🔍",
  "No matches found — try a different keyword? 🤔",
  "Your search came up empty — but don't give up! 💪",
  "Nothing here matching that query 🕵️",
]

/**
 * Get a random loading quote. Deterministic per session 
 * to avoid layout shift on re-renders.
 */
let _cachedLoadingQuote: string | null = null
export function getLoadingQuote(): string {
  if (!_cachedLoadingQuote) {
    _cachedLoadingQuote = loadingQuotes[Math.floor(Math.random() * loadingQuotes.length)]
  }
  return _cachedLoadingQuote
}

/** Get a fresh random loading quote (not cached) */
export function getRandomLoadingQuote(): string {
  return loadingQuotes[Math.floor(Math.random() * loadingQuotes.length)]
}

/** Get a random empty state quote */
export function getEmptyStateQuote(): string {
  return emptyStateQuotes[Math.floor(Math.random() * emptyStateQuotes.length)]
}

/** Get a random search empty quote */
export function getSearchEmptyQuote(): string {
  return searchEmptyQuotes[Math.floor(Math.random() * searchEmptyQuotes.length)]
}

/** Reset the cached loading quote (call on route change) */
export function resetLoadingQuote(): void {
  _cachedLoadingQuote = null
}
