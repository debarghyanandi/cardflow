import type { Board } from './boardState'

async function json<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? problem?.title ?? `Request failed (${response.status})`)
  }
  return response.json() as Promise<T>
}

export async function createBoard(title: string, nickname: string): Promise<{ token: string }> {
  return json(await fetch('/api/boards', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title, nickname })
  }))
}

export async function joinBoard(token: string, nickname: string): Promise<void> {
  await json(await fetch(`/api/boards/${encodeURIComponent(token)}/join`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nickname })
  }))
}

export async function loadBoard(token: string): Promise<Board> {
  return json(await fetch(`/api/boards/${encodeURIComponent(token)}`))
}
