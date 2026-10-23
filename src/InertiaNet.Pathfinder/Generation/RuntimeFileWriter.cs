namespace InertiaNet.Pathfinder.Generation;

static class RuntimeFileWriter
{
    private const string RuntimeTemplate = """
        export type RouteDefinition<T = string> = { url: string; method: T }
        export type RouteDefinitionInfo<T extends string[] = string[]> = { methods: T; url: string }
        export type QueryParamValue = string | number | bigint | boolean | null | undefined
        export type QueryParams = { [key: string]: QueryParamValue | readonly QueryParamValue[] | QueryParams }
        export type RouteQueryOptions = {
            query?: QueryParams
            mergeQuery?: QueryParams
            arrayFormat?: 'repeat' | 'brackets' | 'indices'
            objectFormat?: 'dots' | 'brackets'
        }
        export type FormDefinition<TMethod extends string = string> = { action: string; method: TMethod; data?: Record<string, string> }
        export type RouteParameterDefinition = {
            name: string
            placeholder: string
            optional: boolean
            defaultValue?: string
            preserveSlashes?: boolean
            host?: boolean
        }

        /** Hidden inputs needed when submitting a native HTML form. */
        export function formFields(form: FormDefinition): { name: string; value: string }[] {
            return Object.entries(form.data ?? {}).map(([name, value]) => ({ name, value }))
        }

        export function queryParams(options?: RouteQueryOptions): string {
            if (!options || (!options.query && !options.mergeQuery)) return ''
            const query = options.query ?? options.mergeQuery ?? {}
            const params = new URLSearchParams(
                options.mergeQuery !== undefined && typeof window !== 'undefined' ? window.location.search : ''
            )
            const append = (key: string, value: QueryParams[string]): void => {
                if (value === null || value === undefined) return
                if (Array.isArray(value)) {
                    value.forEach((item: QueryParamValue, index: number) => {
                        const arrayKey = options.arrayFormat === 'brackets' ? `${key}[]`
                            : options.arrayFormat === 'indices' ? `${key}[${index}]` : key
                        if (item !== null && item !== undefined) params.append(arrayKey, String(item))
                    })
                } else if (typeof value === 'object') {
                    Object.entries(value).forEach(([child, item]) => append(
                        options.objectFormat === 'brackets' ? `${key}[${child}]` : `${key}.${child}`, item
                    ))
                } else {
                    params.append(key, String(value))
                }
            }
            for (const [key, value] of Object.entries(query)) {
                const family = new Set<string>()
                params.forEach((_, existing) => {
                    if (existing === key || existing.startsWith(`${key}[`) || existing.startsWith(`${key}.`)) family.add(existing)
                })
                family.forEach(existing => params.delete(existing))
                append(key, value)
            }
            const qs = params.toString()
            return qs ? `?${qs}` : ''
        }

        type UrlDefaults = Record<string, unknown>
        let _urlDefaults: UrlDefaults | (() => UrlDefaults) = {}

        export function setUrlDefaults(defaults: UrlDefaults | (() => UrlDefaults)): void {
            _urlDefaults = defaults
        }

        export function addUrlDefault(key: string, value: string | number | boolean): void {
            const previous = _urlDefaults
            _urlDefaults = typeof previous === 'function' ? () => ({ ...previous(), [key]: value }) : { ...previous, [key]: value }
        }

        export function applyUrlDefaults(args: Record<string, unknown>): Record<string, unknown> {
            const defaults = typeof _urlDefaults === 'function' ? _urlDefaults() : _urlDefaults
            const merged = { ...args }
            for (const [key, value] of Object.entries(defaults)) {
                if (merged[key] === undefined) merged[key] = value
            }
            return merged
        }

        export function normalizeParameters(args: unknown, names: readonly string[]): Record<string, unknown> {
            if (Array.isArray(args)) return Object.fromEntries(names.map((name, index) => [name, args[index]]))
            if (args !== null && typeof args === 'object') return { ...args }
            if (names.length === 1 && names[0] !== undefined && args !== undefined) return { [names[0]]: args }
            return {}
        }

        const isMissing = (value: unknown): boolean => value === undefined || value === null || value === ''

        export function validateParameters(
            routeName: string,
            url: string,
            required: string[],
            args: Record<string, unknown> | undefined,
            optional: string[] = [],
        ): void {
            const missingRequired = required.filter(key => isMissing(args?.[key]))
            if (missingRequired.length > 0) {
                const suffix = missingRequired.length === 1 ? '' : 's'
                const formatted = missingRequired.map(key => `'${key}'`).join(', ')
                throw new Error(`Missing required parameter${suffix} ${formatted} for route '${routeName}' (${url}).`)
            }
            const missingOptional = optional.filter(key => isMissing(args?.[key]))
            const expectedMissing = optional.slice(optional.length - missingOptional.length)
            for (let i = 0; i < missingOptional.length; i++) {
                if (missingOptional[i] !== expectedMissing[i]) {
                    throw new Error(`Optional parameters for route '${routeName}' must be omitted from the end of the argument list.`)
                }
            }
        }

        function encodePathPart(value: unknown): string {
            if (!['string', 'number', 'boolean', 'bigint'].includes(typeof value)
                || (typeof value === 'number' && !Number.isFinite(value))) {
                throw new Error('Route parameter values must be strings, finite numbers, booleans, or bigints.')
            }
            const text = typeof value === 'boolean' ? (value ? 'True' : 'False') : String(value)
            return encodeURIComponent(text).replace(/[!'()*]/g, char => `%${char.charCodeAt(0).toString(16).toUpperCase()}`)
        }

        export function buildUrl(routeName: string, template: string, args: Record<string, unknown>, definitions: readonly RouteParameterDefinition[]): string {
            const parameters = { ...args }
            for (const definition of definitions) {
                if (parameters[definition.name] === undefined && definition.defaultValue !== undefined)
                    parameters[definition.name] = definition.defaultValue
            }
            validateParameters(routeName, template, definitions.filter(p => !p.optional).map(p => p.name), parameters,
                definitions.filter(p => p.optional).map(p => p.name))
            let url = template
            for (const definition of [...definitions].reverse()) {
                const value = parameters[definition.name]
                const suffix = url.slice(url.indexOf(definition.placeholder) + definition.placeholder.length)
                const omitDefault = definition.defaultValue !== undefined && value === definition.defaultValue && /^\/*$/.test(suffix)
                if (definition.optional && (isMissing(value) || omitDefault)) {
                    url = url.replace(`.${definition.placeholder}`, '').replace(`/${definition.placeholder}`, '').replace(definition.placeholder, '')
                } else {
                    let encoded: string
                    if (definition.host) {
                        encoded = String(value)
                        if (!/^[a-zA-Z0-9.-]+$/.test(encoded)) throw new Error(`Invalid host parameter '${definition.name}'.`)
                    } else {
                        encoded = definition.preserveSlashes && typeof value === 'string' ? value.split('/').map(encodePathPart).join('/') : encodePathPart(value)
                    }
                    url = url.replace(definition.placeholder, encoded)
                }
            }
            return url.replace(/\/+$/, '') || '/'
        }
        """;

    public static void Write(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "index.ts"), RuntimeTemplate);
    }
}
