/**
 * Axis scaling for charts.
 *
 * Recharts' default `domain={[0, max]}` puts the top of the axis on whatever the largest data
 * point happens to be, so the ticks land on arbitrary numbers (0, 3.5, 7, 10.5, 14) and the peak
 * sits flush against the ceiling. This produces the "nice" round scale a reader expects instead.
 */

export interface AxisScale {
  /** Upper bound for `domain={[0, max]}`. Always >= the data maximum. */
  max: number
  /** Explicit tick values, starting at 0 and ending at `max`. */
  ticks: number[]
}

/**
 * The steps an axis is allowed to land on, per power of ten. Everyone reads a scale that counts
 * in 1s, 2s, 2.5s, 5s or 10s; nobody reads one that counts in 3s or 7s.
 */
const NICE_STEPS = [1, 2, 2.5, 5, 10]

/**
 * Builds a readable axis that fully contains `dataMax`.
 *
 * The step is the smallest "nice" value that fits the data into at most `maxTicks` intervals, so
 * the axis grows with the data rather than being pinned to a hardcoded ceiling:
 *
 *   dataMax 8  -> 0, 2, 4, 6, 8, 10
 *   dataMax 15 -> 0, 5, 10, 15, 20
 *   dataMax 37 -> 0, 10, 20, 30, 40
 *
 * Integer-only by default because the callers plot message counts — a "2.5 messages" gridline is
 * meaningless. Pass `allowDecimals` for a series that is genuinely fractional.
 */
export const niceAxisScale = (
  dataMax: number,
  { maxTicks = 5, allowDecimals = false }: { maxTicks?: number; allowDecimals?: boolean } = {}
): AxisScale => {
  // An all-zero series still needs a plottable axis; without this the line would have nowhere to
  // sit and recharts would fall back to its own domain.
  if (!Number.isFinite(dataMax) || dataMax <= 0) {
    return { max: maxTicks, ticks: Array.from({ length: maxTicks + 1 }, (_, i) => i) }
  }

  const rawStep = dataMax / maxTicks
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)))

  // Candidates are filtered to whole numbers up front rather than rounded afterwards. Rounding
  // 2.5 up to 3 would step off the ladder entirely and produce an axis counting 0, 3, 6, 9, 12 —
  // the exact thing this list exists to avoid.
  const steps = NICE_STEPS.map((candidate) => magnitude * candidate).filter(
    (candidate) => allowDecimals || Number.isInteger(candidate)
  )

  let step = steps.find((candidate) => candidate >= rawStep) ?? steps[steps.length - 1] ?? rawStep

  if (!allowDecimals) {
    step = Math.max(1, Math.round(step))
  }

  let max = Math.ceil(dataMax / step) * step

  // Headroom. When the data maximum lands exactly on a tick, the peak would sit flush against the
  // top gridline and read as clipped — which is the complaint this scale exists to answer. One
  // extra step lifts the ceiling clear: a peak of 8 gives 0,2,4,6,8,10 rather than 0,2,4,6,8.
  if (max === dataMax) {
    max += step
  }

  const ticks: number[] = []
  for (let value = 0; value <= max + step / 2; value += step) {
    // Floating-point accumulation drifts (0.1 + 0.2), which would render a tick labelled
    // "6.000000000000001". Rounding to the step's own precision keeps the labels clean.
    ticks.push(allowDecimals ? Number(value.toFixed(10)) : Math.round(value))
  }

  return { max, ticks }
}

export default niceAxisScale
