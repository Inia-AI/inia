import { computed, type Ref } from 'vue'
import {
  useMutation,
  UseMutationReturnType,
  useQuery,
  useQueryClient,
} from '@tanstack/vue-query'
import Service from './service'
import type {
  ModelPricing,
  ModelPricingListItem,
  CreateModelPricingRequest,
  UpdateModelPricingRequest,
} from './schema'
import type { OData, ODataQueryParams, ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetModelPricings(params: Ref<ODataQueryParams>, config: any = {}) {
    return useQuery<OData<ModelPricingListItem>>({
      queryKey: computed(() => ['modelPricings', JSON.stringify(params.value)]),
      queryFn: () => this.service.getModelPricings(params.value),
      ...config,
    })
  }

  useGetModelPricing(): UseMutationReturnType<ModelPricing, Error, string, unknown> {
    return useMutation({
      mutationFn: (id: string) => this.service.getModelPricing(id),
    })
  }

  useCreateModelPricing(): UseMutationReturnType<ModelPricing, Error, CreateModelPricingRequest, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: (params: CreateModelPricingRequest) =>
        this.service.createModelPricing(params),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['modelPricings'] })
      },
    })
  }

  useUpdateModelPricing(): UseMutationReturnType<void, Error, { id: string; params: UpdateModelPricingRequest }, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({ id, params }: { id: string; params: UpdateModelPricingRequest }) =>
        this.service.updateModelPricing(id, params),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['modelPricings'] })
      },
    })
  }

  useDeleteModelPricing(): UseMutationReturnType<void, Error, string, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: (id: string) => this.service.deleteModelPricing(id),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['modelPricings'] })
      },
    })
  }
}

export * from './schema'
