import React, { useState } from 'react'
import { FlaskConical, Loader2, Trophy } from 'lucide-react'
import toast from 'react-hot-toast'
import Can from '../../components/Can/Can'
import { StatusBadge } from '../../components/StatusBadge/StatusBadge'
import useReference from '../../hooks/useReference'
import { campaignService } from '../../services/campaigns/campaignService'
import { referenceService, labelOf } from '../../services/referenceService'
import type { CampaignAbTest } from '../../types/campaigns'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'

interface CampaignAbResultsProps {
  campaignId: number
  channel: 'email' | 'whatsapp'
  status: string
  abTest: CampaignAbTest
  onChanged: () => Promise<void> | void
}

/** Side-by-side results of an A/B test, and choosing the winner early. */
export const CampaignAbResults: React.FC<CampaignAbResultsProps> = ({ campaignId, channel, status, abTest, onChanged }) => {
  const apiChannel = channel === 'email' ? 'Email' : 'WhatsApp'
  const options = useReference(() => referenceService.getCampaignOptions(apiChannel), `campaign-options:${apiChannel}`)
  const [deciding, setDeciding] = useState<number | 'auto' | null>(null)
  const undecided = !abTest.decidedAt
  const canDecide = undecided && status === 'Sending'
  const metric = labelOf(options.data?.abMetrics, abTest.metric)
  const numbers = new Intl.NumberFormat()
  const percent = new Intl.NumberFormat(undefined, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 })

  const decide = async (variantId?: number) => {
    setDeciding(variantId ?? 'auto')
    try {
      await campaignService.decideAbTest(campaignId, variantId)
      toast.success('Winner chosen. The rest of the audience is receiving it now.')
      await onChanged()
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'The winner could not be chosen. Try again.')
    } finally {
      setDeciding(null)
    }
  }

  const facts = [
    `${abTest.testPercent}% of the audience in the test`,
    `winner by ${metric.toLowerCase()}`,
    undecided
      ? (abTest.decideAt ? `decided automatically at ${formatAbsoluteDateTime(abTest.decideAt)}` : null)
      : `decided ${formatAbsoluteDateTime(abTest.decidedAt!)}`,
    undecided && abTest.heldRecipients > 0 ? `${numbers.format(abTest.heldRecipients)} waiting for the winner` : null
  ].filter(Boolean)

  return (
    <section className="campaign-section-card" aria-labelledby="ab-results-title">
      <header className="campaign-section-head">
        <h2 id="ab-results-title"><FlaskConical size={16} aria-hidden="true" /> A/B Test</h2>
        {canDecide && (
          <Can permission="Campaign.Send">
            <button type="button" className="btn-toolbar-tertiary" disabled={deciding !== null} onClick={() => decide()}>
              {deciding === 'auto' ? <Loader2 size={14} className="campaign-spin" aria-hidden="true" /> : <Trophy size={14} aria-hidden="true" />}
              Pick Leading Variant Now
            </button>
          </Can>
        )}
      </header>

      <p className="campaign-section-summary">{facts.join(' · ')}</p>

      <div className="data-table-wrapper">
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">Variant</th>
              <th scope="col" className="text-right">Recipients</th>
              <th scope="col" className="text-right">Sent</th>
              <th scope="col" className="text-right">{metric || 'Rate'}</th>
              <th scope="col" className="text-right"><span className="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {abTest.variants.map(v => (
              <tr key={v.variantId} className={v.isWinner ? 'campaign-row-winner' : undefined}>
                <td>
                  <div className="campaign-variant-cell">
                    <strong>Variant {v.label}</strong>
                    {v.isWinner && <StatusBadge type="success" text="Winner" />}
                  </div>
                  <span className="campaign-cell-sub">{v.templateName}{v.subjectOverride ? ` · “${v.subjectOverride}”` : ''}</span>
                </td>
                <td className="text-right">{numbers.format(v.recipients)}</td>
                <td className="text-right">{numbers.format(v.sent)}</td>
                <td className="text-right"><strong>{percent.format(v.rate / 100)}</strong></td>
                <td className="text-right">
                  {canDecide && (
                    <Can permission="Campaign.Send">
                      <button type="button" className="btn-toolbar-tertiary campaign-row-btn" disabled={deciding !== null}
                        onClick={() => decide(v.variantId)} aria-label={`Send variant ${v.label} to everyone else`}>
                        {deciding === v.variantId && <Loader2 size={14} className="campaign-spin" aria-hidden="true" />}
                        Use Variant {v.label}
                      </button>
                    </Can>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

export default CampaignAbResults
