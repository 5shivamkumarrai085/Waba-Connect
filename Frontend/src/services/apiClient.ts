import axios from 'axios';

export const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5155/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (axios.isCancel(error)) {
    } else {
    }
    return Promise.reject(error);
  }
);

// Map of in-flight promises: key -> Promise
const inflightRequests = new Map<string, Promise<any>>();
// Map of AbortControllers: path -> AbortController
const abortControllers = new Map<string, AbortController>();

const getRequestKey = (url: string, params?: any) => {
  return `${url}?${params ? new URLSearchParams(params).toString() : ''}`;
};

const getPathFromUrl = (url: string) => {
  return url.split('?')[0];
};

const originalGet = apiClient.get;

// Overwrite the get method to support deduplication and cancellation
apiClient.get = function <R = any>(url: string, config?: any): Promise<R> {
  const params = config?.params;
  const key = getRequestKey(url, params);
  const path = getPathFromUrl(url);

  // If a duplicate request (exact same URL and query params) is already running, return its promise
  if (inflightRequests.has(key)) {
    return inflightRequests.get(key) as Promise<R>;
  }

  // Cancel any pending request for the same path (e.g. changing search parameters or route)
  if (abortControllers.has(path)) {
    abortControllers.get(path)?.abort('Stale request cancelled');
  }

  const controller = new AbortController();
  abortControllers.set(path, controller);

  const newConfig = {
    ...config,
    signal: controller.signal,
  };

  const promise = originalGet.call(apiClient, url, newConfig)
    .then((res) => {
      inflightRequests.delete(key);
      if (abortControllers.get(path) === controller) {
        abortControllers.delete(path);
      }
      return res;
    })
    .catch((err) => {
      inflightRequests.delete(key);
      if (abortControllers.get(path) === controller) {
        abortControllers.delete(path);
      }
      if (axios.isCancel(err)) {
        // Return a promise that never resolves to ignore the stale response
        return new Promise(() => {});
      }
      throw err;
    });

  inflightRequests.set(key, promise);
  return promise as Promise<R>;
};

