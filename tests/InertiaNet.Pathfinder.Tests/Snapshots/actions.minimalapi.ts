import { type RouteDefinition, type RouteDefinitionInfo, type RouteQueryOptions, type FormDefinition, queryParams, applyUrlDefaults, normalizeParameters, buildUrl } from '../index'
import type { CreatePostRequest } from '../models/CreatePostRequest'

/**
 * @see __MinimalApi__::getApiPostsByIdAndSlug
 * @see Program.cs:6
 * @route /api/posts/{id}/{slug?}
 */
export const getApiPostsByIdAndSlug = (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): RouteDefinition<"get"> => ({
    url: getApiPostsByIdAndSlug.url(args, options), method: "get",
})
getApiPostsByIdAndSlug.definition = { methods: ["get"], url: "/api/posts/{id}/{slug?}" } satisfies RouteDefinitionInfo<["get"]>
getApiPostsByIdAndSlug.url = (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions) => {
    const parameters = applyUrlDefaults(normalizeParameters(args, ["id","slug"]))
    return buildUrl("Posts.Show", getApiPostsByIdAndSlug.definition.url, parameters, [{"name":"id","optional":false,"placeholder":"{id}"},{"name":"slug","optional":true,"placeholder":"{slug?}"}]) + queryParams(options)
}
getApiPostsByIdAndSlug.get = (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): RouteDefinition<"get"> => ({ url: getApiPostsByIdAndSlug.url(args, options), method: "get" })
getApiPostsByIdAndSlug.form = Object.assign((args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: getApiPostsByIdAndSlug.url(args, options), method: "get",
}), {
    get: (args: { "id": number; "slug"?: string } | [id: number, slug?: string], options?: RouteQueryOptions): FormDefinition<"get"> => ({
    action: getApiPostsByIdAndSlug.url(args, options), method: "get",
    }),
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
postApiPosts.post = (options?: RouteQueryOptions): RouteDefinition<"post"> => ({ url: postApiPosts.url(options), method: "post" })
postApiPosts.form = Object.assign((options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: postApiPosts.url(options), method: "post",
}), {
    post: (options?: RouteQueryOptions): FormDefinition<"post"> => ({
    action: postApiPosts.url(options), method: "post",
    }),
})
postApiPosts.body = undefined as unknown as CreatePostRequest

const MinimalApi = { getApiPostsByIdAndSlug, postApiPosts }
export default MinimalApi
