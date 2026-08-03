import React, { useEffect, useRef } from 'react'
import { useMotionValue, useSpring, useInView } from 'framer-motion'

interface AnimatedCounterProps {
  value: string | number
  className?: string
}

/**
 * Animated counter that smoothly transitions between numeric values.
 * Falls back to static display for non-numeric values.
 * Respects prefers-reduced-motion.
 */
export const AnimatedCounter: React.FC<AnimatedCounterProps> = ({ value, className }) => {
  const ref = useRef<HTMLSpanElement>(null)
  const isInView = useInView(ref, { once: true })
  
  // Parse numeric value
  const numericValue = typeof value === 'number' ? value : parseFloat(String(value).replace(/,/g, ''))
  const isNumeric = !isNaN(numericValue) && isFinite(numericValue)

  const motionValue = useMotionValue(0)
  const springValue = useSpring(motionValue, {
    stiffness: 100,
    damping: 20,
    mass: 0.8,
  })

  useEffect(() => {
    if (!isNumeric || !isInView) return
    
    // Check reduced motion preference
    const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    if (prefersReduced) {
      if (ref.current) {
        ref.current.textContent = typeof value === 'number' 
          ? value.toLocaleString() 
          : String(value)
      }
      return
    }

    motionValue.set(numericValue)
  }, [numericValue, isInView, isNumeric])

  useEffect(() => {
    if (!isNumeric) return
    const unsubscribe = springValue.on('change', (latest) => {
      if (ref.current) {
        ref.current.textContent = Math.round(latest).toLocaleString()
      }
    })
    return unsubscribe
  }, [springValue, isNumeric])

  // Non-numeric values render directly
  if (!isNumeric) {
    return <span ref={ref} className={className}>{value}</span>
  }

  return <span ref={ref} className={className}>0</span>
}

export default AnimatedCounter
