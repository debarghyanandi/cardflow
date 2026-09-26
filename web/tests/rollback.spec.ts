import { test, expect, type WebSocketRoute } from '@playwright/test'

test('a drag snaps back when the server becomes unreachable', async ({ page }) => {
  let socket: WebSocketRoute | null = null
  let unavailable = false
  await page.routeWebSocket('**/hubs/board', route => {
    if (unavailable) { void route.close({ code: 1011, reason: 'Server unavailable' }); return }
    socket = route
    route.connectToServer()
  })
  await page.goto('/')
  await page.getByLabel('Your name').fill('Ada')
  await page.getByRole('button', { name: /Create board/ }).click()
  await expect(page).toHaveURL(/\/board\//)
  await expect(page.getByText('● Live')).toBeVisible()

  await page.getByPlaceholder('New column name').fill('To do')
  await page.getByRole('button', { name: 'Add column +' }).click()
  await expect(page.getByRole('heading', { name: 'To do' })).toBeVisible()
  await page.getByPlaceholder('New column name').fill('Done')
  await page.getByRole('button', { name: 'Add column +' }).click()
  await expect(page.getByRole('heading', { name: 'Done' })).toBeVisible()

  const todo = page.locator('.column').filter({ has: page.getByRole('heading', { name: 'To do' }) })
  const done = page.locator('.column').filter({ has: page.getByRole('heading', { name: 'Done' }) })
  await todo.getByPlaceholder('Add a card…').fill('Try me')
  await todo.getByRole('button', { name: 'Add card to To do' }).click()
  await expect(todo.getByText('Try me')).toBeVisible()

  await todo.getByText('Try me').dragTo(done, { steps: 20, targetPosition: { x: 150, y: 100 } })
  await expect(done.getByText('Try me')).toBeVisible()
  await expect(page.getByText('Event #5')).toBeVisible()
  await expect(todo.getByText('Try me')).toHaveCount(0)

  unavailable = true
  if (!socket) throw new Error('The board WebSocket did not connect.')
  await (socket as WebSocketRoute).close({ code: 1011, reason: 'Server unavailable' })
  await expect(page.getByText('○ Reconnecting')).toBeVisible()
  await done.getByText('Try me').dragTo(todo, { steps: 20, targetPosition: { x: 150, y: 100 } })
  await expect(page.getByText(/Move undone:/)).toBeVisible()
  await expect(done.getByText('Try me')).toBeVisible()
  await expect(todo.getByText('Try me')).toHaveCount(0)
})

test('a reconnect replays fifty missed changes', async ({ page }) => {
  let socket: WebSocketRoute | null = null
  let unavailable = false
  await page.routeWebSocket('**/hubs/board', route => {
    if (unavailable) { void route.close({ code: 1011, reason: 'Server unavailable' }); return }
    socket = route
    route.connectToServer()
  })

  await page.goto('/')
  await page.getByLabel('Your name').fill('Ada')
  await page.getByRole('button', { name: /Create board/ }).click()
  await expect(page.getByText('● Live')).toBeVisible()
  await page.getByPlaceholder('New column name').fill('Incoming')
  await page.getByRole('button', { name: 'Add column +' }).click()
  await expect(page.getByText('Event #2')).toBeVisible()

  const token = new URL(page.url()).pathname.split('/').at(-1)!
  const column = page.locator('.column')
  const board = await page.request.get(`/api/boards/${token}`)
  const columnId = (await board.json()).columns[0].id
  unavailable = true
  if (!socket) throw new Error('The board WebSocket did not connect.')
  await (socket as WebSocketRoute).close({ code: 1011, reason: 'Server unavailable' })
  await expect(page.getByText('○ Reconnecting')).toBeVisible()

  for (let index = 0; index < 50; index++) {
    const response = await page.request.post(`/api/boards/${token}/columns/${columnId}/cards`, {
      data: { title: `Missed ${index}`, description: '', previousCardId: null, nextCardId: null }
    })
    expect(response.ok()).toBeTruthy()
  }
  expect(await column.locator('.card').count()).toBe(0)

  unavailable = false
  await expect(page.getByText('● Live')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByText('Event #52')).toBeVisible({ timeout: 20_000 })
  await expect(column.locator('.card')).toHaveCount(50)
})
