import { type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, validateParameters } from '../index'
import type { CreatePostRequest } from '../models/CreatePostRequest'

/**
 * @see __MinimalApi__::getApiPostsByIdAndSlug
 * @see Program.cs:6
 * @route /api/posts/{id}/{slug?}
 * @param slug - Default: null
 */
export const getApiPostsByIdAndSlug = (
    args: { id: number; slug?: string } | [id: number, slug: string],
    options?: RouteQueryOptions
): RouteDefinition<"get"> => ({ url: getApiPostsByIdAndSlug.url(args, options), method: "get" })

getApiPostsByIdAndSlug.definition = { methods: ["get","head"], url: "/api/posts/{id}/{slug?}" } satisfies RouteDefinitionInfo<["get","head"]>

getApiPostsByIdAndSlug.url = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions) => {
    if (Array.isArray(args)) args = { id: args[0], slug: args[1] }
    args = applyUrlDefaults(args ?? {}) as typeof args
    validateParameters("getApiPostsByIdAndSlug", getApiPostsByIdAndSlug.definition.url, ["id"], args as Record<string, unknown>, ["slug"])
    return getApiPostsByIdAndSlug.definition.url.replace("{id}", String(args.id ?? '')).replace("{slug?}", String(args.slug ?? 'null')).replace(/\/+$/, '') + queryParams(options)
}
getApiPostsByIdAndSlug.get = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions) => ({ url: getApiPostsByIdAndSlug.url(args, options), method: "get" as const })
getApiPostsByIdAndSlug.head = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions) => ({ url: getApiPostsByIdAndSlug.url(args, options), method: "head" as const })
getApiPostsByIdAndSlug.form = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: getApiPostsByIdAndSlug.url(args, options), method: "get",
})
getApiPostsByIdAndSlug.form.get = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: getApiPostsByIdAndSlug.url(args, options), method: "get",
})
getApiPostsByIdAndSlug.form.head = (args: { id: number; slug?: string } | [id: number, slug: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: getApiPostsByIdAndSlug.url(args, options), method: "get", data: { _method: "head" },
})

export type MinimalApipostApiPostsPayload = CreatePostRequest

/**
 * @see __MinimalApi__::postApiPosts
 * @see Program.cs:7
 * @route /api/posts/
 */
export const postApiPosts = (options?: RouteQueryOptions): RouteDefinition<"post"> => ({
    url: postApiPosts.url(options), method: "post",
})
postApiPosts.definition = { methods: ["post"], url: "/api/posts/" } satisfies RouteDefinitionInfo<["post"]>
postApiPosts.url = (options?: RouteQueryOptions) => postApiPosts.definition.url + queryParams(options)
postApiPosts.post = (options?: RouteQueryOptions) => ({ url: postApiPosts.url(options), method: "post" as const })
postApiPosts.form = (options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: postApiPosts.url(options), method: "post",
})
postApiPosts.form.post = (options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: postApiPosts.url(options), method: "post",
})
postApiPosts.body = undefined as unknown as CreatePostRequest

const MinimalApi = { getApiPostsByIdAndSlug, postApiPosts }
export default MinimalApi
