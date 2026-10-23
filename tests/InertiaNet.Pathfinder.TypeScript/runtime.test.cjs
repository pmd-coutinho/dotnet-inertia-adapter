const test = require('node:test')
const assert = require('node:assert/strict')
const items = require('./dist/generated/actions/items.js')
const named = require('./dist/generated/routes/Items/index.js')
const rootRoutes = require('./dist/generated/routes/index.js')
const runtime = require('./dist/generated/index.js')
const { Status } = require('./dist/generated/enums/Status.js')

for (const [kind, routes] of [['actions', items], ['named routes', named]]) {
  test(`${kind}: route grammar, default and optional arguments`, () => {
    assert.equal(routes.optional.url(), '/items')
    assert.equal(routes.optional.url(42), '/items/42')
    assert.equal(routes.defaultMethod.url(), '/items')
    assert.equal(routes.defaultMethod.url(1), '/items/1')
    assert.equal(routes.defaultMethod.url(2), '/items/2')
    assert.equal(routes.alias.url(42), '/items/alias/42')
    assert.equal(routes.absolute.url(), '/absolute')
    assert.equal(routes.flag.url(false), '/items/flag/False')
    assert.equal(routes.flag.url(true), '/items/flag/True')
    assert.equal(routes.file.url({ name: 'report' }), '/items/file/report')
    assert.equal(routes.file.url(['report', 'pdf']), '/items/file/report.pdf')
    assert.equal(routes.show.url([42]), '/items/show/42')
    assert.throws(() => routes.show.url(), /Missing required parameter 'id'/)
    assert.equal(routes.show.head, undefined)
  })
  test(`${kind}: URL encoding preserves catch-all semantics`, () => {
    assert.equal(routes.slug.url('a/b?c#d'), '/items/slug/a%2Fb%3Fc%23d')
    assert.equal(routes.catchAll.url('a/b?c#d'), '/items/files/a/b%3Fc%23d')
    assert.equal(routes.singleCatchAll.url('a/b'), '/items/single/a%2Fb')
    assert.equal(routes.slug.url("! '()*"), '/items/slug/%21%20%27%28%29%2A')
  })
  test(`${kind}: URL defaults include parameters absent from the handler`, () => {
    runtime.setUrlDefaults({ locale: 'pt' })
    try { assert.equal(routes.locale.url({ id: 42 }), '/items/locale/pt/posts/42') }
    finally { runtime.setUrlDefaults({}) }
  })
  test(`${kind}: native forms expose uppercase override fields; Inertia uses real methods`, () => {
    assert.deepEqual(routes.update(42), { url: '/items/42', method: 'put' })
    assert.deepEqual(routes.update.form.patch(42), { action: '/items/42', method: 'post', data: { _method: 'PATCH' } })
    assert.deepEqual(runtime.formFields(routes.deleteMethod.form(42)), [{ name: '_method', value: 'DELETE' }])
  })
}
test('MapInertia and explicit methods are discovered', () => {
  assert.deepEqual(rootRoutes.about(), { url: '/about', method: 'get' })
  assert.deepEqual(rootRoutes.methods.options(), { url: '/method', method: 'options' })
})
test('all colliding controller and named route exports remain reachable and executable', () => {
  function urls(module, seen = new Set()) {
    if (module === null || seen.has(module) || !['function', 'object'].includes(typeof module)) return []
    seen.add(module)
    if (typeof module === 'function' && module.definition) return module.definition.url.startsWith('/collision') ? [module.url()] : []
    return Object.values(module).flatMap(value => urls(value, seen))
  }
  const actions = urls(require('./dist/generated/actions/index.js'))
  for (const url of ['/collision/first', '/collision/second', '/collision/index', '/collision/foo', '/collision/bar', '/collision/object/helper', '/collision/object/safe']) {
    assert.ok(actions.includes(url), `missing action ${url}`)
  }
  const routes = urls(rootRoutes)
  for (const url of ['/collision/object/helper', '/collision/object/safe', '/collision/object/class', '/collision/object/class-method', '/collision/object/case', '/collision/object/root', '/collision/object/empty', '/collision/comment*/"quoted']) {
    assert.ok(routes.includes(url), `missing named route ${url}`)
  }
  const { PrototypeEnum } = require('./dist/generated/enums/PrototypeEnum.js')
  assert.equal(Object.hasOwn(PrototypeEnum, '__proto__'), true)
  assert.equal(PrototypeEnum.__proto__, 7)
  const models = require('./dist/generated/models/index.js')
  const validation = Object.values(models).find(value => Object.hasOwn(value, '__proto__'))
  assert.equal(validation.__proto__.required, true)
})
test('C# enum constants include implicit, negative and computed values', () => {
  assert.deepEqual(Status, { Pending: 0, Active: 1, Archived: 5, Next: 6, Negative: -1, Flag: 8, Combined: 13 })
})
test('query replacement removes complete parameter families without iteration skips', () => {
  global.window = { location: { search: '?filter[a]=1&filter[b]=2&filter[c]=3&filter.old=4&tags[]=old&plain=keep' } }
  try {
    assert.equal(runtime.queryParams({ mergeQuery: { filter: null } }), '?tags%5B%5D=old&plain=keep')
    assert.equal(runtime.queryParams({ mergeQuery: { filter: { fresh: 4 }, tags: 'new' } }), '?plain=keep&filter.fresh=4&tags=new')
  } finally { delete global.window }
})
test('queries encode ASP.NET booleans, repeated array keys and nested dot paths', () => {
  assert.equal(runtime.queryParams({ query: { active: true, ids: [1, null, 2, undefined], filter: { name: 'Ana' } } }), '?active=true&ids=1&ids=2&filter.name=Ana')
  assert.equal(runtime.queryParams({ query: { ids: [1, 2] }, arrayFormat: 'brackets' }), '?ids%5B%5D=1&ids%5B%5D=2')
  assert.equal(runtime.queryParams({ query: { filter: { active: false } }, objectFormat: 'brackets' }), '?filter%5Bactive%5D=false')
})
