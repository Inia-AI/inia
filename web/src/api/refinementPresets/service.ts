import { BaseService } from '../base'
import type {
  CreateRefinementPresetRequest,
  UpdateRefinementPresetRequest,
  RefinementPresetResponse,
} from './schema'
import type { ServiceParams } from '../schema'

export default class Service extends BaseService {
  constructor(params?: ServiceParams) {
    super(params)
  }

  async getPresets(): Promise<RefinementPresetResponse[]> {
    const { data } = await this.instance.get(this.getApiPath('ai/content-refinement/presets'))
    return data
  }

  async createPreset(params: CreateRefinementPresetRequest): Promise<RefinementPresetResponse> {
    const { data } = await this.instance.post(this.getApiPath('ai/content-refinement/presets'), params)
    return data
  }

  async updatePreset(id: string, params: UpdateRefinementPresetRequest): Promise<RefinementPresetResponse> {
    const { data } = await this.instance.put(this.getApiPath(`ai/content-refinement/presets/${id}`), params)
    return data
  }

  async deletePreset(id: string): Promise<void> {
    await this.instance.delete(this.getApiPath(`ai/content-refinement/presets/${id}`))
  }
}
