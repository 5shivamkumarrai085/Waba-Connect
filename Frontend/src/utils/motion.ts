/**
 * Motion Animation Utilities for OmniConnect Dashboard
 * 
 * Uses framer-motion (which IS motion.dev — same library, same team).
 * framer-motion v12+ = motion.dev
 * 
 * All animations are:
 * - GPU-accelerated (transform/opacity only)
 * - Respectful of prefers-reduced-motion
 * - Consistent across the entire dashboard
 * - Minimal and premium (Linear/Stripe/Vercel inspired)
 */
import { 
  type Variants, 
  type Transition,
  type MotionProps
} from 'framer-motion'

// ─── Prefers Reduced Motion ─────────────────────────────────────────
export const prefersReducedMotion = (): boolean => {
  if (typeof window === 'undefined') return false
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

// ─── Transition Presets ─────────────────────────────────────────────
export const transitions = {
  /** Snappy — buttons, micro-interactions (150ms) */
  snappy: { duration: 0.15, ease: [0.16, 1, 0.3, 1] } as Transition,
  /** Normal — cards, containers (220ms) */
  normal: { duration: 0.22, ease: [0.16, 1, 0.3, 1] } as Transition,
  /** Smooth — page transitions (250ms) */
  smooth: { duration: 0.25, ease: [0.4, 0, 0.2, 1] } as Transition,
  /** Spring — natural bouncy feel */
  spring: { type: 'spring', stiffness: 400, damping: 30 } as Transition,
  /** Gentle spring — cards */
  gentleSpring: { type: 'spring', stiffness: 260, damping: 25 } as Transition,
}

// ─── Variant Presets (Reusable across components) ───────────────────

/** Fade + slide up — most common entrance */
export const fadeSlideUp: Variants = {
  hidden: { opacity: 0, y: 12 },
  visible: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: -8 },
}

/** Fade + scale — modals, dialogs, dropdowns */
export const fadeScale: Variants = {
  hidden: { opacity: 0, scale: 0.96 },
  visible: { opacity: 1, scale: 1 },
  exit: { opacity: 0, scale: 0.96 },
}

/** Simple fade — page transitions, overlays */
export const fade: Variants = {
  hidden: { opacity: 0 },
  visible: { opacity: 1 },
  exit: { opacity: 0 },
}

/** Slide from right — toasts, drawers */
export const slideFromRight: Variants = {
  hidden: { opacity: 0, x: 24 },
  visible: { opacity: 1, x: 0 },
  exit: { opacity: 0, x: 24 },
}

/** Slide from left — sidebar */
export const slideFromLeft: Variants = {
  hidden: { opacity: 0, x: -16 },
  visible: { opacity: 1, x: 0 },
  exit: { opacity: 0, x: -16 },
}

// ─── Stagger Container ─────────────────────────────────────────────

/** Container variant for staggered children */
export const staggerContainer = (
  staggerDelay = 0.06,
  delayChildren = 0
): Variants => ({
  hidden: { opacity: 1 },
  visible: {
    opacity: 1,
    transition: {
      staggerChildren: staggerDelay,
      delayChildren,
    },
  },
})

/** Child variant for stagger containers */
export const staggerChild: Variants = {
  hidden: { opacity: 0, y: 16 },
  visible: { 
    opacity: 1, 
    y: 0,
    transition: { duration: 0.22, ease: [0.16, 1, 0.3, 1] },
  },
}

// ─── Hover & Tap Props (Micro-interactions) ─────────────────────────

/** Card hover effect — subtle lift + shadow (applied via whileHover/whileTap) */
export const cardHoverProps: MotionProps = {
  whileHover: { y: -3, transition: { duration: 0.2, ease: [0.16, 1, 0.3, 1] } },
  whileTap: { y: 0, scale: 0.99, transition: { duration: 0.1 } },
}

/** Button hover effect — scale 1.03/0.97 */
export const buttonHoverProps: MotionProps = {
  whileHover: { scale: 1.03, transition: { duration: 0.15, ease: [0.16, 1, 0.3, 1] } },
  whileTap: { scale: 0.97, transition: { duration: 0.1 } },
}

/** Subtle hover for interactive elements */
export const subtleHoverProps: MotionProps = {
  whileHover: { scale: 1.02, transition: { duration: 0.15 } },
  whileTap: { scale: 0.98, transition: { duration: 0.1 } },
}

// ─── Page Transition Wrapper Props ──────────────────────────────────

/** Props for page wrapper — fade + slight upward slide, 200-250ms */
export const pageTransitionProps: MotionProps = {
  initial: 'hidden',
  animate: 'visible',
  exit: 'exit',
  variants: fadeSlideUp,
  transition: transitions.smooth,
}

// ─── Counter Animation ─────────────────────────────────────────────

/** Animated counter hook helper — returns spring config for useMotionValue */
export const counterSpring = {
  type: 'spring' as const,
  stiffness: 100,
  damping: 20,
  mass: 0.8,
}
