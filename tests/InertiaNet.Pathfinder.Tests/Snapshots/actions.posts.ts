import { type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, validateParameters } from '../index'
import type { UpdatePostRequest } from '../models/UpdatePostRequest'

/**
 * @route /posts/{id}/{slug?}
 */
export const show = (
    args: { id: number; slug?: string } | [id: number, slug: string],
    options?: RouteQueryOptions
): RouteDefinition<"get"> => ({ url: show.url(args, options), method: "get" })

show.definition = { methods: ["get","head"], url: "/posts/{id}/{slug?}" } satisfies RouteDefinitionInfo<["get","head"]>

show.url = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions) => {
    if (Array.isArray(args)) args = { id: args[0], slug: args[1] }
    args = applyUrlDefaults(args ?? {}) as typeof args
    validateParameters("show", show.definition.url, ["id"], args as Record<string, unknown>, ["slug"])
    return show.definition.url.replace("{id}", String(args.id ?? '')).replace("{slug?}", String(args.slug ?? '')).replace(/\/+$/, '') + queryParams(options)
}
show.get = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions) => ({ url: show.url(args, options), method: "get" as const })
show.head = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions) => ({ url: show.url(args, options), method: "head" as const })
show.form = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: show.url(args, options), method: "get",
})
show.form.get = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: show.url(args, options), method: "get",
})
show.form.head = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: show.url(args, options), method: "get", data: { _method: "head" },
})

export type PostsUpdatePayload = UpdatePostRequest

/**
 * @route /posts/{id}
 */
export const update = (
    args: { id: number } | [id: number] | number,
    options?: RouteQueryOptions
): RouteDefinition<"put"> => ({ url: update.url(args, options), method: "put" })

update.definition = { methods: ["put"], url: "/posts/{id}" } satisfies RouteDefinitionInfo<["put"]>

update.url = (args: { id: number } | [id: number] | number, options?: RouteQueryOptions) => {
    if (typeof args === 'string' || typeof args === 'number') args = { id: args }
    if (Array.isArray(args)) args = { id: args[0] }
    args = applyUrlDefaults(args ?? {}) as typeof args
    validateParameters("update", update.definition.url, ["id"], args as Record<string, unknown>)
    return update.definition.url.replace("{id}", String(args.id ?? '')).replace(/\/+$/, '') + queryParams(options)
}
update.put = (args: { id: number } | [id: number] | number, options?: RouteQueryOptions) => ({ url: update.url(args, options), method: "put" as const })
update.form = (args: { id: number } | [id: number] | number, options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: update.url(args, options), method: "post", data: { _method: "put" },
})
update.form.put = (args: { id: number } | [id: number] | number, options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: update.url(args, options), method: "post", data: { _method: "put" },
})
update.body = undefined as unknown as UpdatePostRequest

const Posts = { show, update }
export default Posts
