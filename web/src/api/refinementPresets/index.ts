import { useMutation, useQuery, useQueryClient, type UseMutationReturnType, type UseQueryReturnType } from '@tanstack/vue-query'

import { computed, type Ref } from 'vue'
import Service from './service'
import type {
  CreateRefinementPresetRequest,
  UpdateRefinementPresetRequest,
  RefinementPresetResponse,
} from './schema'
import type { ServiceParams } from '../schema'

const QUERY_KEY = ['refinement-presets']

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  usePresets(enabled: Ref<boolean> = computed(() => true)): UseQueryReturnType<RefinementPresetResponse[], Error> {
    return useQuery({
      queryKey: QUERY_KEY,
      queryFn: () => this.service.getPresets(),
      enabled,
    })
  }

  useCreatePreset(): UseMutationReturnType<RefinementPresetResponse, Error, CreateRefinementPresetRequest, unknown> {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (params: CreateRefinementPresetRequest) => this.service.createPreset(params),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: QUERY_KEY })
      },
    })
  }

  useUpdatePreset(): UseMutationReturnType<RefinementPresetResponse, Error, { id: string } & UpdateRefinementPresetRequest, unknown> {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (params: { id: string } & UpdateRefinementPresetRequest) =>
        this.service.updatePreset(params.id, { name: params.name, instruction: params.instruction, isShared: params.isShared }),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: QUERY_KEY })
      },
    })
  }

  useDeletePreset(): UseMutationReturnType<void, Error, string, unknown> {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (id: string) => this.service.deletePreset(id),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: QUERY_KEY })
      },
    })
  }
}
