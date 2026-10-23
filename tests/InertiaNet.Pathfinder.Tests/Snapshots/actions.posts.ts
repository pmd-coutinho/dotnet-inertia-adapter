import { type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, normalizeParameters, buildUrl } from '../index'
import type { UpdatePostRequest } from '../models/UpdatePostRequest'

/**
 * @route /posts/{id}/{slug?}
 */
export const show = (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): RouteDefinition<"get"> => ({
    url: show.url(args, options), method: "get",
})
show.definition = { methods: ["get","head"], url: "/posts/{id}/{slug?}" } satisfies RouteDefinitionInfo<["get","head"]>
show.url = (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions) => {
    const parameters = applyUrlDefaults(normalizeParameters(args, ["id","slug"]))
    return buildUrl("show", show.definition.url, parameters, [{"name":"id","optional":false,"placeholder":"{id}"},{"name":"slug","optional":true,"placeholder":"{slug?}"}]) + queryParams(options)
}
show.get = (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): RouteDefinition<"get"> => ({ url: show.url(args, options), method: "get" })
show.head = (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): RouteDefinition<"head"> => ({ url: show.url(args, options), method: "head" })
show.form = Object.assign((args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: show.url(args, options), method: "get",
}), {
    get: (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: show.url(args, options), method: "get",
    }),
    head: (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: show.url(args, options), method: "post", data: { _method: "HEAD" },
    }),
})

export type PostsUpdatePayload = UpdatePostRequest

/**
 * @route /posts/{id}
 */
export const update = (args: { "id": number } | [id: number] | number, options?: RouteQueryOptions): RouteDefinition<"put"> => ({
    url: update.url(args, options), method: "put",
})
update.definition = { methods: ["put"], url: "/posts/{id}" } satisfies RouteDefinitionInfo<["put"]>
update.url = (args: { "id": number } | [id: number] | number, options?: RouteQueryOptions) => {
    const parameters = applyUrlDefaults(normalizeParameters(args, ["id"]))
    return buildUrl("update", update.definition.url, parameters, [{"name":"id","optional":false,"placeholder":"{id}"}]) + queryParams(options)
}
update.put = (args: { "id": number } | [id: number] | number, options?: RouteQueryOptions): RouteDefinition<"put"> => ({ url: update.url(args, options), method: "put" })
update.form = Object.assign((args: { "id": number } | [id: number] | number, options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: update.url(args, options), method: "post", data: { _method: "PUT" },
}), {
    put: (args: { "id": number } | [id: number] | number, options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: update.url(args, options), method: "post", data: { _method: "PUT" },
    }),
})
update.body = undefined as unknown as UpdatePostRequest

const Posts = { show, update }
export default Posts
