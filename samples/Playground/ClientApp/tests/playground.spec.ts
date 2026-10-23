import { test, expect, type Page } from '@playwright/test'

async function token(page: Page) {
  return (await page.context().cookies()).find(cookie => cookie.name === 'XSRF-TOKEN')!.value
}

for (const client of ['react', 'vue']) {
  test(`${client}: SSR, hydration, antiforgery, validation, auth, CRUD, deferred props and scroll merging`, async ({ page }) => {
    const errors: string[] = []
    page.on('pageerror', error => errors.push(error.message))
    page.on('console', message => { if (/hydration|hydrating|mismatch/i.test(message.text())) errors.push(message.text()) })
    const initial = await page.goto(`/${client}/todos`)
    expect(initial!.status()).toBe(200)
    const html = await initial!.text()
    expect(html.match(/id="playground-root"/g)).toHaveLength(1)
    expect(html.match(/data-page="playground-root"/g)).toHaveLength(1)
    expect(html).toContain('data-server-rendered="true"')
    await expect(page.getByTestId('todos').locator('li')).toHaveCount(5)
    await expect(page.getByTestId('stats')).toContainText('counted in a deferred request')
    await page.screenshot({ path: test.info().outputPath(`${client}-workspace.png`), fullPage: true })
    const initialCount = Number(await page.getByTestId('total').textContent())

    const forged = await page.request.post(`/${client}/todos`, { data: { title: 'forged' }, headers: { 'X-Inertia': 'true' } })
    expect(forged.status()).toBe(400)
    const headers = { 'X-Inertia': 'true', 'X-XSRF-TOKEN': await token(page), Precognition: 'true', 'Precognition-Validate-Only': 'title' }
    const valid = await page.request.post(`/${client}/todos`, { data: { title: 'Only validate this' }, headers })
    expect(valid.status()).toBe(204)
    const invalid = await page.request.post(`/${client}/todos`, { data: { title: '' }, headers })
    expect(invalid.status()).toBe(422)
    expect(Array.isArray((await invalid.json()).errors.title)).toBe(true)
    await page.reload()
    await expect(page.getByTestId('total')).toHaveText(String(initialCount))

    await page.getByLabel('Display name').fill(`${client} reader`)
    await page.getByRole('button', { name: 'Sign in', exact: true }).click()
    await expect(page.getByTestId('auth')).toHaveText(`Hello, ${client} reader`)
    await page.getByRole('button', { name: 'Save task', exact: true }).click()
    await expect(page.getByTestId('error')).toContainText(/required/i)
    const title = `${client} </script><b>browser task</b>`
    await page.getByLabel('Task title').fill(title)
    const submission = page.waitForRequest(request => request.method() === 'POST' && request.url().endsWith(`/${client}/todos`))
    await page.getByRole('button', { name: 'Save task', exact: true }).click()
    expect((await submission).headers()['x-xsrf-token']).toBeTruthy()
    await expect(page.getByTestId('flash')).toHaveText('Task created')
    await expect(page.getByTestId('total')).toHaveText(String(initialCount + 1))
    const reloaded = await page.reload()
    const reloadedHtml = await reloaded!.text()
    expect(reloadedHtml.match(/id="playground-root"/g)).toHaveLength(1)
    expect(reloadedHtml.match(/data-page="playground-root"/g)).toHaveLength(1)
    expect(reloadedHtml).not.toContain(title)
    await page.getByRole('button', { name: `Edit ${title}`, exact: true }).click()
    await page.getByLabel('Task title').fill(`${title} edited`)
    await page.getByRole('button', { name: 'Save task', exact: true }).click()
    await expect(page.getByTestId('flash')).toHaveText('Task updated')
    await page.getByRole('button', { name: `Delete ${title} edited`, exact: true }).click()
    await expect(page.getByTestId('flash')).toHaveText('Task deleted')
    await expect(page.getByTestId('total')).toHaveText(String(initialCount))

    await page.getByRole('button', { name: 'Load more', exact: true }).click()
    await expect(page.getByTestId('todos').locator('li')).toHaveCount(10)
    const ids = await page.getByTestId('todos').locator('li').evaluateAll(elements => elements.map(element => element.getAttribute('data-testid')))
    expect(new Set(ids).size).toBe(10)

    await page.goto(`/${client}/todos`)
    const first = page.getByTestId('todos').locator('li').first()
    const nativeTitle = await first.locator('span').first().textContent()
    await first.getByRole('button', { name: `Delete natively ${nativeTitle}`, exact: true }).click()
    await expect(page.getByTestId('total')).toHaveText(String(initialCount - 1))
    await expect(page.getByTestId('flash')).toHaveText('Task deleted')
    expect(errors).toEqual([])
  })

  test(`${client}: SSR failure falls back to one custom root and an interactive CSR page`, async ({ page }) => {
    const errors: string[] = []
    page.on('pageerror', error => errors.push(error.message))
    const response = await page.goto(`/${client}/todos?failSsr=true`)
    expect(response!.status()).toBe(200)
    const html = await response!.text()
    expect(html.match(/id="playground-root"/g)).toHaveLength(1)
    expect(html.match(/data-page="playground-root"/g)).toHaveLength(1)
    expect(html).not.toContain('data-server-rendered="true"')
    await page.getByLabel('Task title').fill(`${client} fallback task`)
    await page.getByRole('button', { name: 'Save task', exact: true }).click()
    await expect(page.getByTestId('flash')).toHaveText('Task created')
    expect(errors).toEqual([])
  })
}
