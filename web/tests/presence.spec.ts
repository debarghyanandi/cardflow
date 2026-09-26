import { test, expect } from '@playwright/test'

test('presence, cursor, editing marker, and tab closed without warning', async ({ browser }) => {
  test.setTimeout(45_000)
  const host = await browser.newContext()
  const guest = await browser.newContext()
  try {
    const ada = await host.newPage()
    await ada.goto('/')
    await ada.getByLabel('Your name').fill('Ada')
    await ada.getByRole('button', { name: /Create board/ }).click()
    await expect(ada.getByText('Here now (1)')).toBeVisible()

    await ada.getByPlaceholder('New column name').fill('Ideas')
    await ada.getByRole('button', { name: 'Add column +' }).click()
    const column = ada.locator('.column')
    await column.getByPlaceholder('Add a card…').fill('Discuss presence')
    await column.getByRole('button', { name: 'Add card to Ideas' }).click()
    await expect(column.getByText('Discuss presence')).toBeVisible()

    const lin = await guest.newPage()
    await lin.goto(ada.url())
    await lin.getByLabel('Your name').fill('Lin')
    await lin.getByRole('button', { name: 'Join board' }).click()
    await expect(lin.getByText('Here now (2)')).toBeVisible()
    await expect(ada.getByText('Here now (2)')).toBeVisible()
    await expect(ada.locator('.member').filter({ hasText: 'Lin' })).toBeVisible()

    await lin.mouse.move(240, 180)
    await expect(ada.locator('.remote-cursor').filter({ hasText: 'Lin' })).toBeVisible()
    await lin.locator('.card').getByRole('button', { name: 'Edit' }).click()
    await expect(ada.getByText('Lin editing')).toBeVisible()

    // Close without running beforeunload: no application cleanup or explicit leave call.
    await lin.close({ runBeforeUnload: false })
    await expect(ada.getByText('Here now (1)')).toBeVisible({ timeout: 29_000 })
    await expect(ada.getByText('Lin editing')).toHaveCount(0)
    await expect(ada.locator('.remote-cursor').filter({ hasText: 'Lin' })).toHaveCount(0)
  } finally {
    await guest.close()
    await host.close().catch(() => {})
  }
})
