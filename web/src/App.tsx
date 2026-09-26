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
type Presence = { connectionId: string; memberId: string; nickname: string; colour: string; editingCardId: string | null }
type Cursor = Pick<Presence, 'connectionId' | 'nickname' | 'colour'> & { x: number; y: number }
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
  const [presence, setPresence] = useState<Record<string, Presence>>({})
  const [cursors, setCursors] = useState<Record<string, Cursor>>({})
  const current = useRef(state)
  const connection = useRef<HubConnection | null>(null)
  const lastCursor = useRef(0)
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
    hub.on('PresenceSnapshot', (entries: Presence[]) => {
      if (!disposed) setPresence(Object.fromEntries(entries.map(entry => [entry.connectionId, entry])))
    })
    hub.on('PresenceChanged', (entry: Presence) => {
      if (!disposed) setPresence(previous => ({ ...previous, [entry.connectionId]: entry }))
    })
    hub.on('PresenceLeft', (connectionId: string) => {
      if (disposed) return
      setPresence(previous => { const next = { ...previous }; delete next[connectionId]; return next })
      setCursors(previous => { const next = { ...previous }; delete next[connectionId]; return next })
    })
    hub.on('CursorMoved', (cursor: Cursor) => {
      if (!disposed) setCursors(previous => ({ ...previous, [cursor.connectionId]: cursor }))
    })
    hub.onreconnecting(() => { if (!disposed) { setConnected(false); setPresence({}); setCursors({}) } })
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
    const heartbeat = window.setInterval(() => {
      if (hub.state === 'Connected') void hub.invoke('Heartbeat', token).catch(async () => {
        if (!disposed && hub.state === 'Connected') {
          try { await hub.invoke('JoinBoard', token) }
          catch (cause) { setNotice(`Presence rejoin failed: ${message(cause)}`) }
        }
      })
    }, 10000)
    return () => { disposed = true; window.clearInterval(heartbeat); connection.current = null; void hub.stop() }
  }, [token])

  function cursorMoved(event: React.PointerEvent<HTMLElement>) {
    if (!connected || !connection.current || Date.now() - lastCursor.current < 50) return
    lastCursor.current = Date.now()
    const x = Math.max(0, Math.min(1, event.clientX / window.innerWidth))
    const y = Math.max(0, Math.min(1, event.clientY / window.innerHeight))
    void connection.current.invoke('MoveCursor', token, x, y).catch(() => {})
  }

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

  return { board: visibleBoard(state), pending: state.pending, connected, notice, move, hub: connection,
    presence: Object.values(presence), cursors: Object.values(cursors), cursorMoved }
}

function LiveBoard({ token, snapshot }: { token: string; snapshot: Board }) {
  const { board, pending, connected, notice, move, hub, presence, cursors, cursorMoved } = useLiveBoard(token, snapshot)
  const [columnTitle, setColumnTitle] = useState('')
  const [cardTitles, setCardTitles] = useState<Record<string, string>>({})
  const [editing, setEditing] = useState<Card | null>(null)
  const [editTitle, setEditTitle] = useState('')
  const [editDescription, setEditDescription] = useState('')
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }))

  async function invoke(method: string, ...args: unknown[]) {
    if (!hub.current) { window.alert('Not connected to the server.'); return false }
    try { await hub.current.invoke(method, token, ...args); return true }
    catch (cause) { window.alert(message(cause)); return false }
  }

  function openEditor(card: Card) {
    setEditing(card); setEditTitle(card.title); setEditDescription(card.description)
    void invoke('SetEditing', card.id)
  }

  function closeEditor() {
    setEditing(null)
    void invoke('SetEditing', null)
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

  const online = [...new Map(presence.map(entry => [entry.memberId, entry])).values()]
  return <main className="workspace" onPointerMove={cursorMoved}>
    <header className="topbar">
      <div className="brand">cardflow<span>↗</span></div>
      <div className="board-title"><small>BOARD</small><h1>{board.title}</h1></div>
      <div className="top-actions"><span className={connected ? 'online' : 'offline'}>{connected ? '● Live' : '○ Reconnecting'}</span>
        <button className="share" onClick={() => { void navigator.clipboard.writeText(window.location.href) }}>Copy link</button></div>
    </header>
    {notice && <p className="notice" role="status">{notice}</p>}
    <section className="board-meta"><span>{board.columns.length} columns</span><span>·</span><span>{board.members.length} members</span><span>·</span><span>Event #{board.seq}</span></section>
    <section className="members" aria-label="Board members">
      <span>Here now ({online.length})</span>
      {online.map(member => <span key={member.memberId} className="member" title={member.nickname}>
        <i style={{ background: member.colour }} />{member.nickname}
      </span>)}
    </section>
    <DndContext sensors={sensors} collisionDetection={collisionDetection} onDragEnd={dropped}>
      <div className="columns">
        {board.columns.map(column => <BoardColumn key={column.id} column={column} disabled={!!pending}
          presence={presence} onEdit={openEditor}
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
    {cursors.filter(cursor => presence.some(entry => entry.connectionId === cursor.connectionId)).map(cursor => <div key={cursor.connectionId} className="remote-cursor"
      style={{ left: `${cursor.x * 100}%`, top: `${cursor.y * 100}%`, color: cursor.colour }}>
      <span>➤</span><label style={{ background: cursor.colour }}>{cursor.nickname}</label>
    </div>)}
    {editing && <div className="edit-overlay" onClick={closeEditor}>
      <form className="edit-dialog" onClick={event => event.stopPropagation()} onSubmit={event => {
        event.preventDefault()
        void (async () => {
          if (await invoke('EditCard', editing.id, { title: editTitle, description: editDescription, version: editing.version }))
            closeEditor()
        })()
      }}>
        <h2>Edit card</h2>
        <label>Title<input value={editTitle} onChange={event => setEditTitle(event.target.value)} maxLength={160} required /></label>
        <label>Description<textarea value={editDescription} onChange={event => setEditDescription(event.target.value)} maxLength={4000} /></label>
        <div><button type="button" onClick={closeEditor}>Cancel</button><button type="submit">Save</button></div>
      </form>
    </div>}
  </main>
}

function BoardColumn({ column, disabled, title, onTitle, onCreate, presence, onEdit }: {
  column: Column; disabled: boolean; title: string; onTitle: (title: string) => void; onCreate: () => Promise<void>
  presence: Presence[]; onEdit: (card: Card) => void
}) {
  const { setNodeRef, isOver } = useDroppable({ id: column.id })
  return <section ref={setNodeRef} className={`column ${isOver ? 'column-over' : ''}`}>
    <header><h2>{column.title}</h2><span>{column.cards.length}</span></header>
    <SortableContext items={column.cards.map(card => card.id)} strategy={verticalListSortingStrategy}>
      <div className="card-list">{column.cards.map(card => <BoardCard key={card.id} card={card} disabled={disabled}
        editors={presence.filter(member => member.editingCardId === card.id)} onEdit={() => onEdit(card)} />)}</div>
    </SortableContext>
    <form className="add-card" onSubmit={event => { event.preventDefault(); void onCreate() }}>
      <input placeholder="Add a card…" value={title} onChange={event => onTitle(event.target.value)} maxLength={160} />
      <button aria-label={`Add card to ${column.title}`}>+</button>
    </form>
  </section>
}

function BoardCard({ card, disabled, editors, onEdit }: { card: Card; disabled: boolean; editors: Presence[]; onEdit: () => void }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: card.id, disabled })
  return <article ref={setNodeRef} className={`card ${isDragging ? 'dragging' : ''}`}
    style={{ transform: CSS.Transform.toString(transform), transition }} {...attributes} {...listeners}>
    <span className="card-grip">⋮⋮</span><span className="card-content">{card.title}
      {editors.map(editor => <small key={editor.connectionId} style={{ color: editor.colour }}>{editor.nickname} editing</small>)}
    </span><button className="edit-card" onPointerDown={event => event.stopPropagation()} onClick={onEdit}>Edit</button>
  </article>
}

function message(cause: unknown): string { return cause instanceof Error ? cause.message : String(cause) }
