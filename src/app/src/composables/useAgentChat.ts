import { computed, ref } from 'vue'
import { postSendPrompt } from '../api/generated/clients/postSendPrompt'
import { getReadResponse } from '../api/generated/clients/getReadResponse'

export type ChatStatus = 'ready' | 'submitted' | 'streaming' | 'error'

export interface ChatTextPart {
  type: 'text'
  id: string
  text: string
  streaming: boolean
}

export interface ChatReasoningPart {
  type: 'reasoning'
  id: string
  text: string
  streaming: boolean
  startedAt: number
  duration?: number
}

export interface ChatToolPart {
  type: 'tool'
  id: string
  toolCallId: string
  name: string
  description?: string
  state: 'running' | 'success' | 'error'
  progress?: string
  args?: string
  result?: string
  error?: string
  startedAt: number
}

export interface ChatDiffPart {
  type: 'diff'
  id: string
  diff?: string
}

export interface ChatIntentPart {
  type: 'intent'
  id: string
  text: string
  streaming: boolean
}

export interface ChatErrorPart {
  type: 'error'
  id: string
  message: string
}

export type ChatPart =
  | ChatTextPart
  | ChatReasoningPart
  | ChatToolPart
  | ChatDiffPart
  | ChatIntentPart
  | ChatErrorPart

export interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  parts: ChatPart[]
}

/**
 * Discriminated activity events emitted by the backend over the still-untyped
 * `/read-response` SSE stream. Transported as a JSON envelope: `{ type, ... }`.
 */
export type AgentEvent =
  | { type: 'turn_start'; turnId?: string }
  | { type: 'intent'; text: string }
  | { type: 'thinking_delta'; id?: string; text: string }
  | { type: 'thinking_done'; id?: string; text?: string }
  | { type: 'tool_call_start'; id?: string; toolCallId: string; name: string; description?: string; args?: string }
  | { type: 'tool_call_progress'; toolCallId: string; message: string }
  | { type: 'tool_call_result'; toolCallId: string; success: boolean; result?: string; error?: string }
  | { type: 'diff_received'; id?: string; diff?: string }
  | { type: 'text_delta'; id?: string; text: string }
  | { type: 'done'; aborted?: boolean }
  | { type: 'error'; message: string }

/** Dead-man's switch for a backend that dies mid-turn without ever sending a
 * `done` event. The real `done` event is the authoritative end-of-turn signal;
 * this timeout is deliberately long so it never interrupts normal tool latency. */
const IDLE_TIMEOUT_MS = 30_000

const AGENT_EVENT_TYPES = new Set<string>([
  'turn_start',
  'intent',
  'thinking_delta',
  'thinking_done',
  'tool_call_start',
  'tool_call_progress',
  'tool_call_result',
  'diff_received',
  'text_delta',
  'done',
  'error',
])

function newId() {
  return crypto.randomUUID()
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * Parses raw SSE data into an {@link AgentEvent}. Falls back to a plain text
 * delta when the payload is not a recognized `{ type, ... }` envelope, which
 * keeps the legacy raw-string streaming behavior working.
 */
export function toAgentEvent(raw: unknown): AgentEvent {
  let candidate: unknown = raw

  if (typeof raw === 'string') {
    try {
      candidate = JSON.parse(raw)
    } catch {
      return { type: 'text_delta', text: raw }
    }
  }

  if (isRecord(candidate) && typeof candidate.type === 'string' && AGENT_EVENT_TYPES.has(candidate.type)) {
    return candidate as unknown as AgentEvent
  }

  return { type: 'text_delta', text: typeof raw === 'string' ? raw : JSON.stringify(raw) }
}

export function useAgentChat() {
  const messages = ref<ChatMessage[]>([])
  const status = ref<ChatStatus>('ready')

  let readyTimeout: ReturnType<typeof setTimeout> | undefined
  let assistantMessage: ChatMessage | undefined

  /** True while a prompt/turn is in flight (request sent or stream active). */
  const isWorking = computed(() => status.value === 'submitted' || status.value === 'streaming')

  function scheduleIdleTimeout() {
    clearTimeout(readyTimeout)
    readyTimeout = setTimeout(() => {
      console.warn('[useAgentChat] No agent activity for 30s; settling the turn as the backend may have died.')
      finishTurn()
    }, IDLE_TIMEOUT_MS)
  }

  /** Reset the dead-man's switch on new activity. */
  function noteActivity() {
    scheduleIdleTimeout()
  }

  function clearStreamingFlags() {
    if (!assistantMessage) return

    for (const part of assistantMessage.parts) {
      if (part.type === 'reasoning') {
        if (part.streaming && part.duration === undefined) {
          part.duration = (Date.now() - part.startedAt) / 1000
        }
        part.streaming = false
      }
      else if (part.type === 'text' || part.type === 'intent') {
        part.streaming = false
      }
    }
  }

  function finishTurn() {
    clearTimeout(readyTimeout)
    clearStreamingFlags()
    status.value = 'ready'
    assistantMessage = undefined
  }

  function ensureAssistantMessage(): ChatMessage {
    if (!assistantMessage) {
      messages.value.push({ id: newId(), role: 'assistant', parts: [] })
      // Grab the reactive proxy Vue hands back so subsequent in-place mutations
      // (text/reasoning/tool part updates) are tracked and schedule renders.
      assistantMessage = messages.value[messages.value.length - 1]
    }
    return assistantMessage
  }

  function applyEvent(event: AgentEvent) {
    if (event.type === 'done') {
      finishTurn()
      return
    }

    if (event.type === 'turn_start') {
      if (status.value !== 'error') status.value = 'streaming'
      noteActivity()
      return
    }

    const message = ensureAssistantMessage()
    const last = message.parts[message.parts.length - 1]

    if (status.value !== 'error') status.value = 'streaming'

    switch (event.type) {
      case 'intent':
        message.parts.push({ type: 'intent', id: newId(), text: event.text, streaming: true })
        break

      case 'thinking_delta':
        if (last && last.type === 'reasoning' && last.streaming) {
          last.text += event.text
        }
        else {
          message.parts.push({
            type: 'reasoning',
            id: event.id ?? newId(),
            text: event.text,
            streaming: true,
            startedAt: Date.now(),
          })
        }
        break

      case 'thinking_done': {
        const byId = event.id !== undefined
          ? message.parts.find((part): part is ChatReasoningPart => part.type === 'reasoning' && part.id === event.id)
          : undefined
        const reasoning = byId ?? (last && last.type === 'reasoning' ? last : undefined)

        if (reasoning) {
          if (event.text) reasoning.text = event.text
          reasoning.streaming = false
          reasoning.duration = (Date.now() - reasoning.startedAt) / 1000
        }
        break
      }

      case 'tool_call_start':
        message.parts.push({
          type: 'tool',
          id: event.id ?? newId(),
          toolCallId: event.toolCallId,
          name: event.name,
          description: event.description,
          args: event.args,
          state: 'running',
          startedAt: Date.now(),
        })
        break

      case 'tool_call_progress': {
        const tool = findTool(message, event.toolCallId)
        if (tool) tool.progress = event.message
        break
      }

      case 'tool_call_result': {
        const tool = findTool(message, event.toolCallId)
        if (tool) {
          tool.state = event.success ? 'success' : 'error'
          tool.result = event.result
          tool.error = event.error
        }
        break
      }

      case 'diff_received':
        message.parts.push({ type: 'diff', id: event.id ?? newId(), diff: event.diff })
        break

      case 'text_delta':
        if (last && last.type === 'text' && last.streaming) {
          last.text += event.text
        }
        else {
          message.parts.push({ type: 'text', id: event.id ?? newId(), text: event.text, streaming: true })
        }
        break

      case 'error':
        message.parts.push({ type: 'error', id: newId(), message: event.message })
        status.value = 'error'
        break
    }

    if (event.type !== 'error') noteActivity()
  }

  function findTool(message: ChatMessage, toolCallId: string): ChatToolPart | undefined {
    return message.parts.find(
      (part): part is ChatToolPart => part.type === 'tool' && part.toolCallId === toolCallId,
    )
  }

  async function consumeResponseStream() {
    const { stream } = await getReadResponse()

    for await (const event of stream) {
      applyEvent(toAgentEvent(event.data))
    }
  }

  async function sendPrompt(prompt: string) {
    const trimmed = prompt.trim()
    if (!trimmed || status.value === 'submitted' || status.value === 'streaming') return

    messages.value.push({
      id: newId(),
      role: 'user',
      parts: [{ type: 'text', id: newId(), text: trimmed, streaming: false }],
    })

    clearTimeout(readyTimeout)
    assistantMessage = undefined
    status.value = 'submitted'

    try {
      await postSendPrompt({ query: { prompt: trimmed } })
    } catch {
      status.value = 'error'
    }
  }

  // 👇 Open the response stream immediately so agent output triggered outside of
  // chat (e.g. a specification diff research run) still lands in the UI.
  consumeResponseStream().catch((error) => {
    console.error('[useAgentChat] Agent response stream failed:', error)
    status.value = 'error'
  })

  return { messages, status, sendPrompt, isWorking }
}
