namespace InertiaNet.Pathfinder.Generation;

static class RuntimeFileWriter
{
    private const string RuntimeTemplate = """
        export type RouteDefinition<T = string> = { url: string; method: T }
        export type RouteDefinitionInfo<T extends string[] = string[]> = { methods: T; url: string }
        export type RouteQueryOptions = { query?: QueryParams; mergeQuery?: QueryParams }
        export type FormDefinition<TMethod extends string = string> = { action: string; method: TMethod }
        type QueryParamValue = string | number | boolean | null | undefined
        type QueryParams = Record<string, QueryParamValue | QueryParamValue[] | Record<string, QueryParamValue | QueryParamValue[]>>

        const getValue = (value: string | number | boolean): string => {
            if (value === true) return '1'
            if (value === false) return '0'
            return value.toString()
        }

        const addNestedParams = (obj: Record<string, unknown>, prefix: string, params: URLSearchParams) => {
            Object.entries(obj).forEach(([subKey, value]) => {
                if (value === undefined) return
                const paramKey = `${prefix}[${subKey}]`
                if (Array.isArray(value)) {
                    value.forEach((v) => params.append(`${paramKey}[]`, getValue(v as string | number | boolean)))
                } else if (value !== null && typeof value === 'object') {
                    addNestedParams(value as Record<string, unknown>, paramKey, params)
                } else if (['string', 'number', 'boolean'].includes(typeof value)) {
                    params.set(paramKey, getValue(value as string | number | boolean))
                }
            })
        }

        export function queryParams(options?: RouteQueryOptions): string {
            if (!options || (!options.query && !options.mergeQuery)) return ''

            const query = options.query ?? options.mergeQuery
            const includeExisting = options.mergeQuery !== undefined

            const params = new URLSearchParams(
                includeExisting && typeof window !== 'undefined' ? window.location.search : ''
            )

            for (const key in query) {
                const queryValue = query[key]

                if (queryValue === undefined || queryValue === null) {
                    params.delete(key)
                    continue
                }

                if (Array.isArray(queryValue)) {
                    if (params.has(`${key}[]`)) params.delete(`${key}[]`)
                    queryValue.forEach((value) => {
                        params.append(`${key}[]`, value.toString())
                    })
                } else if (typeof queryValue === 'object') {
                    params.forEach((_, paramKey) => {
                        if (paramKey.startsWith(`${key}[`)) params.delete(paramKey)
                    })
                    addNestedParams(queryValue as Record<string, unknown>, key, params)
                } else {
                    params.set(key, getValue(queryValue))
                }
            }

            const qs = params.toString()
            return qs ? `?${qs}` : ''
        }

        let _urlDefaults: Record<string, string> | (() => Record<string, string>) = {}

        export function setUrlDefaults(defaults: Record<string, string> | (() => Record<string, string>)): void {
            _urlDefaults = defaults
        }

        export function addUrlDefault(key: string, value: string): void {
            if (typeof _urlDefaults === 'function') {
                const current = _urlDefaults
                _urlDefaults = () => ({ ...current(), [key]: value })
            } else {
                _urlDefaults = { ..._urlDefaults, [key]: value }
            }
        }

        export function applyUrlDefaults(args: Record<string, unknown>): Record<string, unknown> {
            const defaults = typeof _urlDefaults === 'function' ? _urlDefaults() : _urlDefaults
            const merged: Record<string, unknown> = { ...defaults }
            for (const [key, value] of Object.entries(args)) {
                if (value !== null && value !== undefined) {
                    merged[key] = value
                }
            }
            return merged
        }

        export function validateParameters(args: Record<string, unknown> | undefined, optional: string[]): void {
            const missing = optional.filter(key => !args?.[key])
            const expectedMissing = optional.slice(missing.length * -1)
            for (let i = 0; i < missing.length; i++) {
                if (missing[i] !== expectedMissing[i]) {
                    throw new Error('Unexpected optional parameters missing. Unable to generate a URL.')
                }
            }
        }
        """;

    public static void Write(string outputDir)
    {
        var path = Path.Combine(outputDir, "index.ts");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(path, RuntimeTemplate);
    }
}
