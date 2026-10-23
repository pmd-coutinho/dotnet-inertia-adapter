import type { PropsFor } from './generated/bigint/types/page-map'
import { LargeCode } from './generated/bigint/enums/LargeCode'
import Numbers from './generated/bigint/actions/numbers'

export const page: PropsFor<'Numbers'> = {
  id: 9223372036854775807n, unsigned: 18446744073709551615n, huge: 10n ** 80n,
  count: 3, price: 12.5, code: LargeCode.Unsafe,
}
export const safePage: PropsFor<'Numbers'> = { id: 1, unsigned: 2, huge: 3, count: 4, price: 5, code: LargeCode.Safe }
export const largeUrl = Numbers.show.url(9223372036854775807n)
export const digitUrl = Numbers.show.url('9223372036854775807')
export const queryUrl = Numbers.show.url(1n, { query: { id: 9223372036854775807n } })
// @ts-expect-error Int32 values remain numbers
export const invalidCount: PropsFor<'Numbers'>['count'] = 1n
// @ts-expect-error decimal values remain numbers
export const invalidPrice: PropsFor<'Numbers'>['price'] = 1n
// @ts-expect-error marker objects are revived before components receive props
export const invalidMarker: PropsFor<'Numbers'>['id'] = { $bigint: '123' }
