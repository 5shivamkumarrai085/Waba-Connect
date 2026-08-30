import React from 'react'
import { motion } from 'framer-motion'
import { ReportBuilder } from '../components/ReportBuilder/ReportBuilder'
import { pageTransitionProps } from '../utils/motion'
import './Reporting.css'

/**
 * Reporting & Analytics.
 *
 * <para>
 * A route wrapper only. The page is one report — pick a type, narrow it, group it, read it,
 * export it or save it — and every control on it acts on the same query, so it lives in one
 * component rather than being split across a shell and a body that would have to share all of
 * that state through props.
 * </para>
 * <para>
 * What used to sit above the builder — a metrics grid, an accuracy cross-check, a freshness
 * table, a list of feature bullets and a second export panel — was reporting *about* the
 * reporting, not reporting. It has been removed from the page. The endpoints behind the metrics
 * are untouched and still serve the Activity Logs cards, which is the one place those figures
 * were being read.
 * </para>
 */
export const Reporting: React.FC = () => (
  <motion.div {...pageTransitionProps}>
    <ReportBuilder />
  </motion.div>
)

export default Reporting
