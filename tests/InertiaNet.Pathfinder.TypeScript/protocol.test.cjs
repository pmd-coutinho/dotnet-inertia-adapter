const assert = require('node:assert/strict')
const { test } = require('node:test')
const { spawnSync } = require('node:child_process')
const { mkdtempSync, readFileSync, rmSync } = require('node:fs')
const os = require('node:os')
const path = require('node:path')

test('actual adapter JSON, initial HTML, and SSR preserve large integers with the real Inertia parser', async () => {
  const temporary = mkdtempSync(path.join(os.tmpdir(), 'inertia-protocol-'))
  try {
    const output = path.join(temporary, 'pages.json')
    const result = spawnSync('dotnet', ['run', '--project', path.resolve(__dirname, '../InertiaNet.ProtocolFixtures'), '--', output], { encoding: 'utf8' })
    assert.equal(result.status, 0, result.stdout + result.stderr)
    const pages = JSON.parse(readFileSync(output, 'utf8'))
    for (const channel of ['', 'Html', 'Ssr']) {
      let text = pages['String' + channel]
      if (channel === 'Html') {
        assert.match(text, /id="custom-root"/)
        text = text.match(/<script[^>]*>(.*?)<\/script>/s)[1]
        assert.ok(!text.includes('<'))
      }
      const page = JSON.parse(text)
      assert.equal(page.props.id, '9223372036854775807')
      assert.equal(page.props.huge, '1' + '0'.repeat(80))
      assert.equal(page.props.nested.values[0], '-9223372036854775808')
      assert.equal(page.props.nested.values[1], null)
      assert.equal(page.flash.receipt, '18446744073709551615')
      assert.equal(page.props.decimal, 12.5)
      assert.equal(page.preserveBigIntegers, undefined)
    }
    const packageInfo = JSON.parse(readFileSync(path.join(__dirname, 'node_modules/@inertiajs/core/package.json'), 'utf8'))
    if (!packageInfo.exports?.['./json']) {
      assert.equal(packageInfo.version, '3.0.2', 'only the pinned legacy client may omit BigInt support')
      console.log('Legacy Inertia 3.0.2: string-policy transport verified; marker mode requires the modern client.')
      return
    }
    const { parsePage, stringifyJson, stringifyPage } = await import('@inertiajs/core/json')
    for (const channel of ['', 'Html', 'Ssr']) {
      const html = pages['BigInt' + channel]
      const text = channel === 'Html' ? html.match(/<script[^>]*>(.*?)<\/script>/s)[1] : html
      const page = parsePage(text)
      assert.equal(page.props.id, 9223372036854775807n)
      assert.equal(page.props.min, -9223372036854775808n)
      assert.equal(page.props.huge, 10n ** 80n)
      assert.equal(page.props.small, 42)
      assert.equal(page.props.nested.safe, 9007199254740991)
      assert.equal(page.props.nested.unsafe, 9007199254740992n)
      assert.equal(page.props.nested.values[0], -9223372036854775808n)
      assert.equal(page.props.nested.values[1], null)
      assert.equal(page.props.lookup['9223372036854775807'], 18446744073709551615n)
      assert.equal(page.flash.receipt, 18446744073709551615n)
      assert.equal(page.props.text, '</script><p>untrusted</p>')
      assert.equal(page.props.decimal, 12.5)
      assert.equal(parsePage(stringifyPage(page)).props.id, page.props.id)
      assert.equal(JSON.parse(stringifyJson({ id: page.props.id })).id, '9223372036854775807')
    }
    assert.deepEqual(parsePage('{"props":{"own":{"$bigint":"123"}}}').props.own, { $bigint: '123' })
  } finally { rmSync(temporary, { recursive: true, force: true }) }
})
