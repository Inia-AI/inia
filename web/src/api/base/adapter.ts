import { OData, ODataResponse } from '../schema'

export const toOdata = <T>(data: ODataResponse<T>): OData<T> => {
  return {
    count: data['@odata.count'],
    skip: data['@odata.skip'],
    top: data['@odata.top'],
    value: data.value,
  }
}
