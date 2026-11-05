import { computed, type Ref } from 'vue'
import {
  useMutation,
  UseMutationReturnType,
  useQuery,
  useQueryClient,
} from '@tanstack/vue-query'
import Service from './service'
import type {
  Counterparty,
  CounterpartyBundle,
  CounterpartyImportPlan,
  CreateCounterpartyRequest,
  UpdateCounterpartyRequest,
} from './schema'
import type { OData, ODataQueryParams, ServiceParams } from '../schema'

export default class {
  service: Service
  constructor(params?: ServiceParams) {
    this.service = new Service(params)
  }

  useGetCounterparties(
    params: Ref<ODataQueryParams>,
    config: any = {},
  ) {
    return useQuery<OData<Counterparty>>({
      queryKey: computed(() => [
        'counterparties',
        JSON.stringify(params.value),
      ]),
      queryFn: () => this.service.getCounterparties(params.value),
      ...config,
    })
  }

  useGetCounterparty(): UseMutationReturnType<any, Error, any, unknown> {
    return useMutation({
      mutationFn: (id: string) => this.service.getCounterparty(id),
    })
  }

  useCreateCounterparty(): UseMutationReturnType<any, Error, any, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: (params: CreateCounterpartyRequest) =>
        this.service.createCounterparty(params),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['counterparties'] })
      },
    })
  }

  updateCounterparty(): UseMutationReturnType<any, Error, any, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({
        id,
        params,
      }: {
        id: string
        params: UpdateCounterpartyRequest
      }) => this.service.updateCounterparty(id, params),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['counterparties'] })
      },
    })
  }

  deleteCounterparty(): UseMutationReturnType<any, Error, any, unknown> {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: (id: string) => this.service.deleteCounterparty(id),
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['counterparties'] })
      },
    })
  }

  useExportBundle(): UseMutationReturnType<
    { blob: Blob; filename: string | null },
    Error,
    void,
    unknown
  > {
    return useMutation({
      mutationFn: () => this.service.exportBundle(),
      // The file is consumed by the download click; the mutation cache has no use for it.
      gcTime: 0,
    })
  }

  useImportBundle(): UseMutationReturnType<
    CounterpartyImportPlan,
    Error,
    { bundle: CounterpartyBundle; dryRun: boolean },
    unknown
  > {
    const queryClient = useQueryClient()

    return useMutation({
      mutationFn: ({ bundle, dryRun }) =>
        this.service.importBundle(bundle, dryRun),
      // The dialog holds the plan for as long as it is open; the cache keeping a second copy
      // of the bundle and the plan for five minutes after that would be pure weight.
      gcTime: 0,
      onSuccess: plan => {
        if (!plan.applied) {
          return
        }
        queryClient.invalidateQueries({ queryKey: ['counterparties'] })
        queryClient.invalidateQueries({ queryKey: ['counterparty-types'] })
      },
    })
  }
}
