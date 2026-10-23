import { router, type LinkComponentBaseProps, type FormComponentProps } from '@inertiajs/core'
import Items, { type ItemsStorePayload } from './generated/actions/items'
import * as NamedItems from './generated/routes/Items'
import { Status } from './generated/enums/Status'

router.visit(Items.show({ id: 42 }))
router.visit(Items.show([42]))
router.visit(Items.optional())
router.visit(Items.defaultMethod())
router.visit(Items.flag(false))
router.visit(Items.store())
router.visit(NamedItems.alias(42))
export const link: LinkComponentBaseProps = { href: Items.slug('hello') }
export const form: FormComponentProps = { action: Items.update(42) }
export const nativeForm = Items.update.form.patch(42)
export const payload: ItemsStorePayload = { state: Status.Active, title: 'Hello' }
// @ts-expect-error int route arguments are numbers
Items.unconstrained('text')
// @ts-expect-error required parameters cannot be omitted
Items.show()
// @ts-expect-error boolean route arguments are not strings
Items.flag('true')
// @ts-expect-error MapGet/HttpGet do not implicitly accept HEAD on ASP.NET
Items.show.head(42)

import type { PropsFor, ComponentName } from './generated/types/page-map'
import type { PageProps } from '@inertiajs/core'
import type { ContractsStorePayload, ContractsSearchQuery, ContractsUploadForm } from './generated/actions/contracts'

export const component: ComponentName = 'Contract/Index'
export const typedPage: PropsFor<'Contract/Index'> = {
  'display-name': 'Typed', presentNull: null,
  item: { id: 1 }, other: { slug: 'two' }, items: [{ id: 1 }, null],
  box: { data: { id: 1 } }, firstState: 0, secondState: 0, asyncItem: { id: 3 },
}
// @ts-expect-error explicit JSON property names replace CLR names
export const wrongPage: PropsFor<'Contract/Index'> = { title: 'Wrong' }
export const bodyContract: ContractsStorePayload = { 'wire-title': 'Body', count: 2 }
export const queryContract: ContractsSearchQuery = { term: 'Query', hidden: 'binder uses CLR fields' }
export const uploadContract: ContractsUploadForm = { 'title-field': 'File', hidden: 'form field', attachment: new Blob() }
// @ts-expect-error the JSON request uses its JSON name, independently of query/form aliases
export const wrongBody: ContractsStorePayload = { term: 'Wrong' }
declare const sharedPage: PageProps
declare const generatedProps: PropsFor<'Contract/Index'>
export const compatiblePageProps: PageProps = generatedProps
export const sharedUserId: number | undefined = sharedPage.auth?.user.id
// @ts-expect-error shared props module augmentation preserves the user ID type
export const wrongSharedUserId: string | undefined = sharedPage.auth?.user.id

import Contracts from './generated/actions/contracts'
router.visit(Items.store(), { data: payload })
router.visit(Contracts.store(), { data: bodyContract })
router.visit(Contracts.upload(), { data: uploadContract })
