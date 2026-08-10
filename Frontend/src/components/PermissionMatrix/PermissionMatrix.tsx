import React, { useMemo } from 'react'
import { Puzzle, KeyRound } from 'lucide-react'
import type { PermissionCatalog } from '../../types/setup'

interface PermissionMatrixProps {
  catalog: PermissionCatalog | null
  /** Currently granted permission keys. */
  value: string[]
  onChange: (next: string[]) => void
  disabled?: boolean
  isLoading?: boolean
}

/**
 * Features × Capabilities grid.
 *
 * Renders exactly the capabilities the server declares per feature, which are genuinely
 * ragged — Connect Account has Connect/Disconnect, bots have Clone, Template has
 * "Load template". Generating a uniform CRUD grid instead would show checkboxes that grant
 * nothing, and adding a feature later would need a frontend change rather than a seed row.
 */
export const PermissionMatrix: React.FC<PermissionMatrixProps> = ({
  catalog,
  value,
  onChange,
  disabled = false,
  isLoading = false
}) => {
  const granted = useMemo(() => new Set(value), [value])

  const toggleKey = (key: string) => {
    const next = new Set(granted)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    onChange([...next])
  }

  /** Select-all for one feature row — the common case is "give them everything here". */
  const toggleFeature = (keys: string[], allGranted: boolean) => {
    const next = new Set(granted)
    keys.forEach((key) => (allGranted ? next.delete(key) : next.add(key)))
    onChange([...next])
  }

  const toggleGroup = (keys: string[], allGranted: boolean) => {
    const next = new Set(granted)
    keys.forEach((key) => (allGranted ? next.delete(key) : next.add(key)))
    onChange([...next])
  }

  if (isLoading) {
    return <div className="perm-matrix-empty">Loading permissions…</div>
  }

  if (!catalog || catalog.groups.length === 0) {
    return <div className="perm-matrix-empty">No permissions are available.</div>
  }

  return (
    <div className={`perm-matrix ${disabled ? 'is-disabled' : ''}`}>
      <div className="perm-matrix-head">
        <div className="perm-matrix-head-cell">
          <Puzzle size={15} />
          <span>Features</span>
        </div>
        <div className="perm-matrix-head-cell">
          <KeyRound size={15} />
          <span>Capabilities</span>
        </div>
      </div>

      <div className="perm-matrix-body">
        {catalog.groups.map((group) => {
          const groupKeys = group.features.flatMap((f) => f.capabilities.map((c) => c.key))
          const groupAllGranted = groupKeys.every((key) => granted.has(key))
          const groupSomeGranted = !groupAllGranted && groupKeys.some((key) => granted.has(key))

          return (
            <div className="perm-matrix-group" key={group.name}>
              <div className="perm-matrix-group-head">
                <span className="perm-matrix-group-name">{group.name}</span>
                <button
                  type="button"
                  className="perm-matrix-group-toggle"
                  onClick={() => toggleGroup(groupKeys, groupAllGranted)}
                  disabled={disabled}
                >
                  {groupAllGranted ? 'Clear all' : groupSomeGranted ? 'Select all' : 'Select all'}
                </button>
              </div>

              {group.features.map((feature) => {
                const featureKeys = feature.capabilities.map((c) => c.key)
                const featureAllGranted = featureKeys.every((key) => granted.has(key))

                return (
                  <div className="perm-matrix-row" key={feature.feature}>
                    <div className="perm-matrix-feature">
                      <button
                        type="button"
                        className="perm-matrix-feature-name"
                        onClick={() => toggleFeature(featureKeys, featureAllGranted)}
                        disabled={disabled}
                        title={featureAllGranted ? 'Clear all capabilities' : 'Select all capabilities'}
                      >
                        {feature.displayName}
                      </button>
                    </div>

                    <div className="perm-matrix-capabilities">
                      {feature.capabilities.map((capability) => (
                        <label className="perm-matrix-check" key={capability.key}>
                          <input
                            type="checkbox"
                            checked={granted.has(capability.key)}
                            onChange={() => toggleKey(capability.key)}
                            disabled={disabled}
                          />
                          <span>{capability.displayName}</span>
                        </label>
                      ))}
                    </div>
                  </div>
                )
              })}
            </div>
          )
        })}
      </div>
    </div>
  )
}

export default PermissionMatrix
