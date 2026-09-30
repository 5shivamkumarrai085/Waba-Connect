import React, { useCallback, useEffect, useState } from 'react'
import { Download, Link2, RefreshCw } from 'lucide-react'
import { EmptyState } from '../../components/EmptyState/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { campaignService, type LinkClicks } from '../../services/campaigns/campaignService'
import { formatAbsoluteDateTime } from '../../utils/dateHelper'

interface CampaignLinkReportProps {
  campaignId: number
  /** Changes whenever the campaign's click total moves, so the table refreshes with the cards. */
  clickedCount: number
}

/** Cells that start with = + - @ are formulas to Excel; prefix them so they open as text. */
const csvCell = (value: string | number) => {
  const text = String(value)
  const safe = /^[=+\-@\t\r]/.test(text) ? `'${text}` : text
  return `"${safe.replace(/"/g, '""')}"`
}

/**
 * Clicks per link for an email campaign: which links people used, how many times, and by how
 * many distinct recipients.
 */
export const CampaignLinkReport: React.FC<CampaignLinkReportProps> = ({ campaignId, clickedCount }) => {
  const [links, setLinks] = useState<LinkClicks[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setIsLoading(true)
    setError(null)
    try {
      setLinks(await campaignService.getLinkReport(campaignId))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The link report could not be loaded.')
    } finally {
      setIsLoading(false)
    }
  }, [campaignId])

  useEffect(() => {
    void load()
  }, [load, clickedCount])

  const max = Math.max(1, ...links.map(l => l.totalClicks))
  const numbers = new Intl.NumberFormat()

  const exportCsv = () => {
    const rows = [
      ['URL', 'Total clicks', 'Unique clickers', 'First click', 'Last click'],
      ...links.map(l => [l.url, l.totalClicks, l.uniqueClickers, l.firstClickAt, l.lastClickAt])
    ]
    const blob = new Blob([rows.map(r => r.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `campaign-${campaignId}-links.csv`
    a.click()
    URL.revokeObjectURL(url)
  }

  const body = () => {
    if (error) {
      return <EmptyState iconName="AlertCircle" title="The link report could not be loaded" message={error}
        action={{ label: 'Try Again', onClick: () => void load() }} />
    }
    if (isLoading && links.length === 0) return <Skeleton variant="table" />
    if (links.length === 0) {
      return <EmptyState iconName="BarChart3" title="No clicks yet" message="Clicks appear here as recipients use the links in the message." />
    }
    return (
      <div className="data-table-wrapper">
        <table className="data-table">
          <thead>
            <tr>
              <th scope="col">Link</th>
              <th scope="col" className="text-right">Unique</th>
              <th scope="col" className="text-right">Total</th>
              <th scope="col">Last Click</th>
            </tr>
          </thead>
          <tbody>
            {links.map(link => (
              <tr key={link.url}>
                <td className="campaign-link-cell">
                  <a href={link.url} target="_blank" rel="noopener noreferrer" className="campaign-link-url" title={link.url} translate="no">
                    {link.url}
                  </a>
                  <progress className="campaign-link-bar" max={max} value={link.totalClicks}
                    aria-label={`${numbers.format(link.totalClicks)} of ${numbers.format(max)} clicks on the most-used link`} />
                </td>
                <td className="text-right">{numbers.format(link.uniqueClickers)}</td>
                <td className="text-right">{numbers.format(link.totalClicks)}</td>
                <td><time dateTime={link.lastClickAt}>{formatAbsoluteDateTime(link.lastClickAt)}</time></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }

  return (
    <section className="campaign-section-card" aria-labelledby="link-report-title" aria-busy={isLoading}>
      <header className="campaign-section-head">
        <h2 id="link-report-title"><Link2 size={16} aria-hidden="true" /> Links</h2>
        <div className="campaign-section-actions">
          <button type="button" className="btn-toolbar-tertiary" onClick={() => void load()} disabled={isLoading} aria-label="Refresh the link report">
            <RefreshCw size={14} aria-hidden="true" className={isLoading ? 'campaign-spin' : undefined} />
          </button>
          <button type="button" className="btn-toolbar-tertiary" onClick={exportCsv} disabled={links.length === 0}>
            <Download size={14} aria-hidden="true" /> Export CSV
          </button>
        </div>
      </header>
      {body()}
    </section>
  )
}

export default CampaignLinkReport
