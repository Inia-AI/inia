import { computed, type Ref } from 'vue'
import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query'
import Service from './service'
import type { CounterpartyType, CounterpartyTypeRequest } from './schema'
import type { OData, ODataQueryParams, ServiceParams } from '../schema'

const COUNTERPARTY_TYPES_KEY = 'counterparty-types'

export default class {
  service: Service

  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetCounterpartyTypes(
    params: Ref<ODataQueryParams>,
  ): UseQueryReturnType<OData<CounterpartyType>, Error> {
    return useQuery({
      queryKey: computed(() => [
        COUNTERPARTY_TYPES_KEY,
        JSON.stringify(params.value),
      ]),
      queryFn: () => this.service.getCounterpartyTypes(params.value),
    })
  }

  useCreateCounterpartyType(): UseMutationReturnType<CounterpartyType, Error, CounterpartyTypeRequest, unknown> {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (request: CounterpartyTypeRequest) => this.service.createCounterpartyType(request),
      onSuccess: () => queryClient.invalidateQueries({ queryKey: [COUNTERPARTY_TYPES_KEY] }),
    })
  }

  useUpdateCounterpartyType(): UseMutationReturnType<
    CounterpartyType,
    Error,
    { id: string; data: CounterpartyTypeRequest },
    unknown
  > {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: ({ id, data }: { id: string; data: CounterpartyTypeRequest }) =>
        this.service.updateCounterpartyType(id, data),
      onSuccess: () => queryClient.invalidateQueries({ queryKey: [COUNTERPARTY_TYPES_KEY] }),
    })
  }

  useDeleteCounterpartyType(): UseMutationReturnType<void, Error, string, unknown> {
    const queryClient = useQueryClient()
    return useMutation({
      mutationFn: (id: string) => this.service.deleteCounterpartyType(id),
      onSuccess: () => queryClient.invalidateQueries({ queryKey: [COUNTERPARTY_TYPES_KEY] }),
    })
  }
}
