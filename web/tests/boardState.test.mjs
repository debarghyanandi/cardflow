import test from 'node:test'
import assert from 'node:assert/strict'
import { beginMove, rejectMove, visibleBoard, cardOrder, applyEvent } from '../src/boardState.ts'

const original = {
  id: 'board', title: 'Demo', token: 'token', seq: 2, members: [],
  columns: [
    { id: 'left', title: 'Left', rank: '01', cards: [
      { id: 'a', columnId: 'left', title: 'A', description: '', rank: '01', version: 1 },
      { id: 'b', columnId: 'left', title: 'B', description: '', rank: '11', version: 1 }
    ] },
    { id: 'right', title: 'Right', rank: '11', cards: [] }
  ]
}

test('a failed optimistic move restores the exact confirmed order', () => {
  const state = beginMove({ confirmed: original, pending: null },
    { cardId: 'a', newColumnId: 'right', previousCardId: null, nextCardId: null, version: 1 })
  assert.deepEqual(cardOrder(visibleBoard(state)), [['b'], ['a']])
  assert.deepEqual(cardOrder(visibleBoard(rejectMove(state))), [['a', 'b'], []])
  assert.deepEqual(cardOrder(original), [['a', 'b'], []])
})

test('rollback also restores order inside a single column', () => {
  const state = beginMove({ confirmed: original, pending: null },
    { cardId: 'a', newColumnId: 'left', previousCardId: 'b', nextCardId: null, version: 1 })
  assert.deepEqual(visibleBoard(state).columns[0].cards.map(card => card.id), ['b', 'a'])
  assert.deepEqual(visibleBoard(rejectMove(state)).columns[0].cards.map(card => card.id), ['a', 'b'])
})

test('duplicate events do nothing and missing sequences are detected', () => {
  const moved = { boardId: 'board', seq: 3, type: 'CardMoved', createdAt: '',
    payload: { id: 'a', columnId: 'right', title: 'A', description: '', rank: '1', version: 2 } }
  const applied = applyEvent(original, moved)
  assert.deepEqual(cardOrder(applied), [['b'], ['a']])
  assert.equal(applyEvent(applied, moved), applied)
  assert.throws(() => applyEvent(original, { ...moved, seq: 4 }), /Missing board event 3/)
})
