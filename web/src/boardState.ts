export type Card = { id: string; columnId: string; title: string; description: string; rank: string; version: number }
export type Column = { id: string; title: string; rank: string; cards: Card[] }
export type Member = { id: string; nickname: string; colour: string }
export type Board = { id: string; title: string; token: string; columns: Column[]; members: Member[]; seq: number }
export type BoardEvent = { boardId: string; seq: number; type: string; payload: unknown; createdAt: string }
export type Move = { cardId: string; newColumnId: string; previousCardId: string | null; nextCardId: string | null; version: number }
export type BoardState = { confirmed: Board; pending: Move | null }

const ordered = <T extends { id: string; rank: string }>(items: T[]) =>
  items.sort((a, b) => a.rank < b.rank ? -1 : a.rank > b.rank ? 1 : a.id.localeCompare(b.id))

export function cardOrder(board: Board): string[][] {
  return ordered([...board.columns]).map(column => ordered([...column.cards]).map(card => card.id))
}

export function visibleBoard(state: BoardState): Board {
  const board = structuredClone(state.confirmed)
  const move = state.pending
  if (!move) return board
  const card = board.columns.flatMap(column => column.cards).find(item => item.id === move.cardId)
  const destination = board.columns.find(column => column.id === move.newColumnId)
  if (!card || !destination) return board
  for (const column of board.columns) column.cards = column.cards.filter(item => item.id !== card.id)
  card.columnId = destination.id
  const nextIndex = move.nextCardId ? destination.cards.findIndex(item => item.id === move.nextCardId) : -1
  const previousIndex = move.previousCardId ? destination.cards.findIndex(item => item.id === move.previousCardId) : -1
  const index = nextIndex >= 0 ? nextIndex : previousIndex >= 0 ? previousIndex + 1 : destination.cards.length
  destination.cards.splice(index, 0, card)
  return board
}

export function beginMove(state: BoardState, move: Move): BoardState {
  if (state.pending) throw new Error('A card move is already pending.')
  return { ...state, pending: move }
}

export function rejectMove(state: BoardState): BoardState {
  return { ...state, pending: null }
}

export function applyEvent(board: Board, event: BoardEvent): Board {
  if (event.seq <= board.seq) return board
  if (event.seq !== board.seq + 1) throw new Error(`Missing board event ${board.seq + 1}`)
  const next = structuredClone(board)
  const payload = event.payload as Record<string, any>
  switch (event.type) {
    case 'BoardRenamed': next.title = payload.title; break
    case 'MemberJoined': {
      const member = payload as Member
      next.members = [...next.members.filter(item => item.id !== member.id), member]
      break
    }
    case 'ColumnCreated': next.columns.push(payload as Column); ordered(next.columns); break
    case 'ColumnRenamed': {
      const column = next.columns.find(item => item.id === payload.id)
      if (column) column.title = payload.title
      break
    }
    case 'CardCreated':
    case 'CardEdited':
    case 'CardMoved':
    case 'CardArchived': {
      const card = payload as Card
      for (const column of next.columns) column.cards = column.cards.filter(item => item.id !== card.id)
      if (event.type !== 'CardArchived') {
        const column = next.columns.find(item => item.id === card.columnId)
        if (column) { column.cards.push(card); ordered(column.cards) }
      }
      break
    }
    case 'CardsRebalanced': {
      for (const changed of payload.cards as Card[]) {
        const card = next.columns.flatMap(column => column.cards).find(item => item.id === changed.id)
        if (card) { card.rank = changed.rank; card.version = changed.version }
      }
      for (const column of next.columns) ordered(column.cards)
      break
    }
    case 'ColumnsRebalanced': {
      for (const changed of payload.columns as Column[]) {
        const column = next.columns.find(item => item.id === changed.id)
        if (column) column.rank = changed.rank
      }
      ordered(next.columns)
      break
    }
    case 'BoardCreated': break
    default: throw new Error(`Unknown board event: ${event.type}`)
  }
  next.seq = event.seq
  return next
}
