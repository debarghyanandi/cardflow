import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { HubConnectionBuilder, HttpTransportType, type HubConnection } from '@microsoft/signalr'
import { DndContext, PointerSensor, pointerWithin, useDroppable, useSensor, useSensors,
  type CollisionDetection, type DragEndEvent } from '@dnd-kit/core'
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { createBoard, joinBoard, loadBoard } from './api'
import { applyEvent, beginMove, cardOrder, rejectMove, visibleBoard,
  type Board, type BoardEvent, type BoardState, type Card, type Column, type Move } from './boardState'

type BoardSync = { seq: number; snapshot: Board | null; events: BoardEvent[] }
const collisionDetection: CollisionDetection = args => pointerWithin({
  ...args, droppableContainers: args.droppableContainers.filter(container => container.id !== args.active.id)
})

function boardToken(): string | null {
  const match = /^\/board\/([^/]+)\/?$/.exec(window.location.pathname)
  return match ? decodeURIComponent(match[1]) : null
}

export function App() {
  const token = boardToken()
  return token ? <BoardPage token={token} /> : <Welcome />
}

function Welcome() {
  const [title, setTitle] = useState('My board')
  const [nickname, setNickname] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  return <main className="welcome">
    <div className="brand">cardflow<span>↗</span></div>
    <h1>Make room for the next idea.</h1>
    <p>A little board for work that moves together.</p>
    <form onSubmit={async event => {
      event.preventDefault(); setBusy(true); setError('')
      try { const board = await createBoard(title, nickname); window.location.assign(`/board/${board.token}`) }
      catch (cause) { setError(message(cause)); setBusy(false) }
    }}>
      <label>Board name<input value={title} onChange={event => setTitle(event.target.value)} required maxLength={120} /></label>
      <label>Your name<input value={nickname} onChange={event => setNickname(event.target.value)} required maxLength={40} /></label>
      <button disabled={busy}>Create board <span>→</span></button>
      {error && <p className="error" role="alert">{error}</p>}
    </form>
    <p className="hint">Have a board link? Open it to join.</p>
  </main>
}

function BoardPage({ token }: { token: string }) {
  const query = useQuery({ queryKey: ['board', token], queryFn: () => loadBoard(token), retry: false })
  const [nickname, setNickname] = useState('')
  const [joinError, setJoinError] = useState('')
  if (query.isPending) return <main className="loading">Loading board…</main>
  if (query.isError) return <main className="welcome">
    <div className="brand">cardflow<span>↗</span></div>
    <h1>Join this board</h1>
    <p>Choose a name. Anyone with the link can collaborate.</p>
    <form onSubmit={async event => {
      event.preventDefault(); setJoinError('')
      try { await joinBoard(token, nickname); await query.refetch() }
      catch (cause) { setJoinError(message(cause)) }
    }}>
      <label>Your name<input value={nickname} onChange={event => setNickname(event.target.value)} required maxLength={40} /></label>
      <button>Join board <span>→</span></button>
      {joinError && <p className="error" role="alert">{joinError}</p>}
    </form>
  </main>
  return <LiveBoard key={token} token={token} snapshot={query.data} />
}

function useLiveBoard(token: string, snapshot: Board) {
  const [state, setState] = useState<BoardState>({ confirmed: snapshot, pending: null })
  const [connected, setConnected] = useState(false)
  const [notice, setNotice] = useState('')
  const current = useRef(state)
  const connection = useRef<HubConnection | null>(null)
  const pull = useRef<() => Promise<void>>(async () => {})
  const update = (change: (value: BoardState) => BoardState) => {
    const next = change(current.current)
    current.current = next
    setState(next)
  }

  useEffect(() => {
    let disposed = false
    let syncing = false
    const waiting = new Map<number, BoardEvent>()
    const hub = new HubConnectionBuilder()
      .withUrl('/hubs/board', { transport: HttpTransportType.WebSockets, skipNegotiation: true })
      .withAutomaticReconnect()
      .build()
    connection.current = hub

    function drain() {
      while (waiting.has(current.current.confirmed.seq + 1)) {
        const event = waiting.get(current.current.confirmed.seq + 1)!
        waiting.delete(event.seq)
        update(value => ({
          confirmed: applyEvent(value.confirmed, event),
          pending: event.type === 'CardMoved' &&
            (event.payload as Card).id === value.pending?.cardId ? null : value.pending
        }))
      }
      for (const seq of waiting.keys()) if (seq <= current.current.confirmed.seq) waiting.delete(seq)
      if (!syncing && waiting.size) void catchUp()
    }

    async function catchUp() {
      if (disposed || syncing) return
      syncing = true
      try {
        const result = await hub.invoke<BoardSync>('CatchUp', token, current.current.confirmed.seq)
        if (result.snapshot && result.snapshot.seq >= current.current.confirmed.seq) {
          update(() => ({ confirmed: result.snapshot!, pending: null }))
          setNotice('Board refreshed after reconnect.')
        }
        for (const event of result.events) waiting.set(event.seq, event)
      } catch (cause) { if (!disposed) setNotice(`Sync failed: ${message(cause)}`) }
      finally { syncing = false }
      if (!disposed) drain()
    }
    pull.current = catchUp
    hub.on('BoardEvent', (event: BoardEvent) => { waiting.set(event.seq, event); drain() })
    hub.onreconnecting(() => { if (!disposed) setConnected(false) })
    hub.onreconnected(async () => {
      if (disposed) return
      try { await hub.invoke('JoinBoard', token); setConnected(true); await catchUp() }
      catch (cause) { setNotice(`Reconnect failed: ${message(cause)}`) }
    })
    hub.onclose(() => { if (!disposed) setConnected(false) })

    async function start() {
      while (!disposed) {
        try {
          await hub.start()
          await hub.invoke('JoinBoard', token)
          if (disposed) break
          setConnected(true)
          await catchUp()
          return
        } catch (cause) {
          if (!disposed) { setConnected(false); setNotice(`Waiting for server: ${message(cause)}`) }
          await new Promise(resolve => setTimeout(resolve, 2000))
        }
      }
    }
    void start()
    return () => { disposed = true; connection.current = null; void hub.stop() }
  }, [token])

  async function move(move: Move) {
    update(value => beginMove(value, move))
    setNotice('')
    try {
      if (!connection.current) throw new Error('Not connected to the server.')
      await connection.current.invoke('MoveCard', token, move.cardId, {
        newColumnId: move.newColumnId, previousCardId: move.previousCardId,
        nextCardId: move.nextCardId, version: move.version
      })
      await pull.current()
    } catch (cause) {
      const stillPending = current.current.pending?.cardId === move.cardId
      update(rejectMove)
      setNotice(stillPending ? `Move undone: ${message(cause)}` : 'Move confirmed, but the reply was interrupted.')
    }
  }

  return { board: visibleBoard(state), pending: state.pending, connected, notice, move, hub: connection }
}

function LiveBoard({ token, snapshot }: { token: string; snapshot: Board }) {
  const { board, pending, connected, notice, move, hub } = useLiveBoard(token, snapshot)
  const [columnTitle, setColumnTitle] = useState('')
  const [cardTitles, setCardTitles] = useState<Record<string, string>>({})
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }))

  async function invoke(method: string, ...args: unknown[]) {
    if (!hub.current) return
    try { await hub.current.invoke(method, token, ...args) }
    catch (cause) { window.alert(message(cause)) }
  }

  function dropped(event: DragEndEvent) {
    const activeId = String(event.active.id)
    const overId = event.over ? String(event.over.id) : null
    if (!overId || activeId === overId || pending) return
    const source = board.columns.find(column => column.cards.some(card => card.id === activeId))
    const target = board.columns.find(column => column.id === overId || column.cards.some(card => card.id === overId))
    const card = source?.cards.find(item => item.id === activeId)
    if (!source || !card || !target) return
    const cards = target.cards.filter(item => item.id !== activeId)
    const index = cards.findIndex(item => item.id === overId)
    const slot = index >= 0 ? index : cards.length
    const previousCardId = slot > 0 ? cards[slot - 1].id : null
    const nextCardId = slot < cards.length ? cards[slot].id : null
    if (source.id === target.id && card.id === nextCardId) return
    void move({ cardId: card.id, newColumnId: target.id, previousCardId, nextCardId, version: card.version })
  }

  return <main className="workspace">
    <header className="topbar">
      <div className="brand">cardflow<span>↗</span></div>
      <div className="board-title"><small>BOARD</small><h1>{board.title}</h1></div>
      <div className="top-actions"><span className={connected ? 'online' : 'offline'}>{connected ? '● Live' : '○ Reconnecting'}</span>
        <button className="share" onClick={() => { void navigator.clipboard.writeText(window.location.href) }}>Copy link</button></div>
    </header>
    {notice && <p className="notice" role="status">{notice}</p>}
    <section className="board-meta"><span>{board.columns.length} columns</span><span>·</span><span>{board.members.length} members</span><span>·</span><span>Event #{board.seq}</span></section>
    <DndContext sensors={sensors} collisionDetection={collisionDetection} onDragEnd={dropped}>
      <div className="columns">
        {board.columns.map(column => <BoardColumn key={column.id} column={column} disabled={!!pending}
          title={cardTitles[column.id] ?? ''} onTitle={value => setCardTitles(current => ({ ...current, [column.id]: value }))}
          onCreate={async () => {
            const title = cardTitles[column.id]?.trim()
            if (!title) return
            await invoke('CreateCard', column.id, { title, description: '', previousCardId: null, nextCardId: null })
            setCardTitles(current => ({ ...current, [column.id]: '' }))
          }} />)}
        <form className="new-column" onSubmit={event => {
          event.preventDefault()
          const title = columnTitle.trim()
          if (title) { void invoke('CreateColumn', { title, previousColumnId: null, nextColumnId: null }); setColumnTitle('') }
        }}>
          <input placeholder="New column name" value={columnTitle} onChange={event => setColumnTitle(event.target.value)} maxLength={120} />
          <button>Add column +</button>
        </form>
      </div>
    </DndContext>
  </main>
}

function BoardColumn({ column, disabled, title, onTitle, onCreate }: {
  column: Column; disabled: boolean; title: string; onTitle: (title: string) => void; onCreate: () => Promise<void>
}) {
  const { setNodeRef, isOver } = useDroppable({ id: column.id })
  return <section ref={setNodeRef} className={`column ${isOver ? 'column-over' : ''}`}>
    <header><h2>{column.title}</h2><span>{column.cards.length}</span></header>
    <SortableContext items={column.cards.map(card => card.id)} strategy={verticalListSortingStrategy}>
      <div className="card-list">{column.cards.map(card => <BoardCard key={card.id} card={card} disabled={disabled} />)}</div>
    </SortableContext>
    <form className="add-card" onSubmit={event => { event.preventDefault(); void onCreate() }}>
      <input placeholder="Add a card…" value={title} onChange={event => onTitle(event.target.value)} maxLength={160} />
      <button aria-label={`Add card to ${column.title}`}>+</button>
    </form>
  </section>
}

function BoardCard({ card, disabled }: { card: Card; disabled: boolean }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: card.id, disabled })
  return <article ref={setNodeRef} className={`card ${isDragging ? 'dragging' : ''}`}
    style={{ transform: CSS.Transform.toString(transform), transition }} {...attributes} {...listeners}>
    <span className="card-grip">⋮⋮</span><span>{card.title}</span>
  </article>
}

function message(cause: unknown): string { return cause instanceof Error ? cause.message : String(cause) }
