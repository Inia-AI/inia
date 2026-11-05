import Service from './service'
import type { ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }
}
