import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'

/** An API answer with an error status, as axios rejects it. */
export const axiosError = (
  status: number,
  data: unknown = {},
  config?: InternalAxiosRequestConfig,
  headers: Record<string, string> = {},
) =>
  new AxiosError('refused', String(status), config, undefined, {
    status,
    statusText: '',
    headers,
    config: config ?? { headers: new AxiosHeaders() },
    data,
  })
