import type { ServiceParams } from './schema'
import axios, { type AxiosInstance } from 'axios'
import { getApiUrl } from '@/config/api'
import { activeOrganizationId, clearSignIn } from '@/lib/utils'
import { sessionJustEnded, signInAgain } from '@/lib/sessionEnded'

declare module 'axios' {
  interface AxiosRequestConfig {
    /** A sign-in step answers 401 for a wrong password, a wrong code or a lockout, and its own screen shows the reason. */
    signInStep?: boolean
  }
}

export class BaseService {
  instance: AxiosInstance
  protected apiVersion = 'v1'

  constructor(
    { base, withCredentials }: ServiceParams = {
      base: '',
      withCredentials: true,
    },
  ) {
    this.instance = axios.create({
      baseURL: `${base || getApiUrl()}`,
      withCredentials,
    })

    this.instance.interceptors.request.use(
      config => {
        config.headers['X-Tenant-Id'] = activeOrganizationId()
        config.headers.Authorization = `Bearer ${localStorage.getItem('accessToken')}`
        return config
      },
      error => {
        return Promise.reject(error)
      },
    )

    this.instance.interceptors.response.use(
      response => response,
      async error => {
        if (error.response?.status === 401 && !error.config?.signInStep) {
          // A browser that held a token had a session, which the API has ended or revoked.
          if (localStorage.getItem('accessToken')) {
            await signInAgain()
          } else if (!sessionJustEnded()) {
            clearSignIn()
            const { default: router } = await import('@/plugins/router')
            router.push({ name: 'Login' })
          }
        }

        return Promise.reject(error)
      },
    )
  }

  protected getApiPath(path: string): string {
    return this.apiVersion ? `api/${this.apiVersion}/${path}` : path
  }
}
