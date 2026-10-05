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

/**
 * Full slide from the right edge — for panels that occupy the edge of the viewport. It travels
 * the panel's own width, so the panel is clearly arriving from off-screen.
 */
export const slideInPanel: Variants = {
  hidden: { x: '100%' },
  visible: { x: 0 },
  exit: { x: '100%' },
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

// ─── Menus & Popovers ───────────────────────────────────────────────

/**
 * Menus open with ease-out and close faster than they open. Asymmetry is
 * deliberate: the user is deciding on the way in and the system is just
 * getting out of the way on the way out.
 */
export const menuTransitions = {
  in: { duration: 0.16, ease: [0.16, 1, 0.3, 1] } as Transition,
  out: { duration: 0.12, ease: [0.4, 0, 1, 1] } as Transition,
}

// ─── Wizard Steps ───────────────────────────────────────────────────

/**
 * Directional slide between wizard steps. 24px, not 100% — a full-width slide
 * inside a dialog reads as a page transition and is far too loud for two steps.
 * `direction` is 1 going forward, -1 going back.
 */
export const stepSlide: Variants = {
  hidden: (direction: number) => ({ opacity: 0, x: direction * 24 }),
  visible: { opacity: 1, x: 0, transition: transitions.normal },
  exit: (direction: number) => ({
    opacity: 0,
    x: direction * -24,
    transition: transitions.snappy,
  }),
}
