import test from 'node:test'
import assert from 'node:assert/strict'
import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import { applyEvent, cardOrder } from '../src/boardState.ts'

const api1 = 'http://127.0.0.1:8081'
const api2 = 'http://127.0.0.1:8082'

async function request(base, path, cookie, body) {
  const response = await fetch(`${base}${path}`, {
    method: body ? 'POST' : 'GET',
    headers: { ...(cookie ? { Cookie: cookie } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: body ? JSON.stringify(body) : undefined
  })
  const content = await response.text()
  if (!response.ok) throw new Error(`${response.status} ${content.slice(0, 300)}`)
  const value = JSON.parse(content)
  return { value, cookie: response.headers.get('set-cookie')?.split(';')[0] ?? cookie }
}

test('20 clients converge after 200 concurrent random operations', { timeout: 180_000 }, async () => {
  const created = await request(api1, '/api/boards', '', { title: 'Convergence', nickname: 'Ada' })
  const token = created.value.token
  const cookie = created.cookie
  assert.ok(cookie)
  const path = `/api/boards/${token}`
  const columns = []
  for (let index = 0; index < 3; index++) {
    const result = await request(api1, `${path}/columns`, cookie,
      { title: `Column ${index}`, previousColumnId: null, nextColumnId: null })
    columns.push(result.value)
  }
  const seeded = []
  for (let index = 0; index < 100; index++) {
    const result = await request(api1, `${path}/columns/${columns[index % 3].id}/cards`, cookie,
      { title: `Seed ${index}`, description: '', previousCardId: null, nextCardId: null })
    seeded.push(result.value)
  }

  const baseline = (await request(api1, path, cookie)).value
  const clients = []
  try {
    for (let index = 0; index < 20; index++) {
      const base = index % 2 ? api2 : api1
      const hub = new HubConnectionBuilder()
        .withUrl(`${base}/hubs/board`, {
          transport: HttpTransportType.WebSockets, skipNegotiation: true,
          headers: { Cookie: cookie }
        }).configureLogging(LogLevel.Error).build()
      const client = { hub, board: structuredClone(baseline), waiting: new Map() }
      hub.on('BoardEvent', event => {
        client.waiting.set(event.seq, event)
        while (client.waiting.has(client.board.seq + 1)) {
          const next = client.waiting.get(client.board.seq + 1)
          client.waiting.delete(next.seq)
          client.board = applyEvent(client.board, next)
        }
      })
      await hub.start()
      await hub.invoke('JoinBoard', token)
      clients.push(client)
    }

    let randomState = 123456789
    const random = max => {
      randomState = (Math.imul(randomState, 1664525) + 1013904223) >>> 0
      return randomState % max
    }
    const operations = []
    for (let index = 0; index < 100; index++) {
      const column = columns[random(columns.length)]
      operations.push(() => request(index % 2 ? api2 : api1,
        `${path}/columns/${column.id}/cards`, cookie,
        { title: `New ${index}`, description: '', previousCardId: null, nextCardId: null }))
    }
    for (let index = 0; index < 40; index++) {
      const card = seeded[index]
      const column = columns[random(columns.length)]
      operations.push(() => request(index % 2 ? api1 : api2,
        `${path}/cards/${card.id}/move`, cookie,
        { newColumnId: column.id, previousCardId: null, nextCardId: null, version: card.version }))
    }
    for (let index = 40; index < 70; index++) {
      const card = seeded[index]
      operations.push(() => fetch(`${index % 2 ? api1 : api2}${path}/cards/${card.id}`, {
        method: 'PATCH', headers: { Cookie: cookie, 'Content-Type': 'application/json' },
        body: JSON.stringify({ title: `Edited ${index}`, description: '', version: card.version })
      }).then(async response => { if (!response.ok) throw new Error(`${response.status} ${await response.text()}`) }))
    }
    for (let index = 70; index < 100; index++) {
      const card = seeded[index]
      operations.push(() => request(index % 2 ? api1 : api2,
        `${path}/cards/${card.id}/archive`, cookie, { version: card.version }))
    }
    // All requests start without an artificial delay. The server chooses commit order.
    const settled = await Promise.allSettled(operations.map(operation => operation()))
    const failed = settled.filter(result => result.status === 'rejected')
    assert.equal(failed.length, 0, failed.slice(0, 5).map(result => String(result.reason)).join('\n'))

    const final = (await request(api1, path, cookie)).value
    assert.equal(final.seq, baseline.seq + 200)
    const deadline = Date.now() + 30_000
    while (clients.some(client => client.board.seq !== final.seq) && Date.now() < deadline)
      await new Promise(resolve => setTimeout(resolve, 50))
    const expected = cardOrder(final)
    for (const client of clients) {
      assert.equal(client.board.seq, final.seq)
      assert.deepEqual(cardOrder(client.board), expected)
    }
  } finally {
    await Promise.allSettled(clients.map(client => client.hub.stop()))
  }
})
