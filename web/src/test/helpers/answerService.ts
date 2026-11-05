import type { AxiosInstance, AxiosResponse, InternalAxiosRequestConfig } from 'axios'
import { axiosError } from './axiosError'

export interface Answer {
  status: number
  data?: unknown
  headers?: Record<string, string>
}

export interface Sent {
  method: string
  url: string
  body: unknown
  params?: Record<string, string>
}

const bodyOf = ({ data, headers }: InternalAxiosRequestConfig): unknown => {
  if (typeof data !== 'string') {
    return data
  }
  return String(headers['Content-Type']).includes('x-www-form-urlencoded') ? Object.fromEntries(new URLSearchParams(data)) : JSON.parse(data)
}

/** Answers one module service the way the platform does, at the HTTP boundary, and keeps every request it got. */
export function answerService(instance: AxiosInstance, answer: (request: Sent) => Answer): Sent[] {
  const sent: Sent[] = []
  instance.defaults.adapter = async (config: InternalAxiosRequestConfig): Promise<AxiosResponse> => {
    const request = {
      method: (config.method ?? 'get').toUpperCase(),
      url: config.url ?? '',
      body: bodyOf(config),
      params: config.params as Record<string, string> | undefined,
    }
    sent.push(request)
    const { status, data, headers = {} } = answer(request)
    if (status >= 400) {
      throw axiosError(status, data, config, headers)
    }
    return { data, status, statusText: '', headers, config }
  }
  return sent
}

export const SIGN_IN_TOKENS = { access_token: 'access', refresh_token: 'refresh', expires_in: 900, token_type: 'Bearer' }
