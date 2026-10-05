import { apiClient } from '../services/apiClient'

/**
 * Resolves a backend-served path to an absolute URL.
 *
 * <p>
 * The API returns storage paths like <code>/uploads/avatars/x.png</code>. Dropped straight into
 * an <code>&lt;img src&gt;</code> they resolve against the *page* origin — the Vite dev server on
 * :5173 — not the API on :5155, and there is no dev proxy. The result is a broken image with no
 * error anywhere, which is why uploaded avatars never appeared despite the files existing in
 * wwwroot/uploads/avatars.
 * </p>
 * <p>
 * The static root is derived from the configured API base by stripping the trailing
 * <code>/api</code>, so it follows the deployment rather than hardcoding a host.
 * </p>
 *
 * Absolute URLs and data URIs pass through untouched — Meta-hosted media and inline previews
 * are already complete.
 */
export const resolveMediaUrl = (url: string | null | undefined): string => {
  if (!url) return ''

  if (url.startsWith('http://') || url.startsWith('https://') || url.startsWith('data:') || url.startsWith('blob:')) {
    return url
  }

  const base = apiClient.defaults.baseURL || 'https://waba-connect-api-wnir.onrender.com/api'
  const staticRoot = base.replace(/\/api\/?$/, '')

  return `${staticRoot}${url.startsWith('/') ? '' : '/'}${url}`
}

export default resolveMediaUrl
