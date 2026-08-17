import { ref } from 'vue'
import { postSendPrompt } from '../api/generated/clients/postSendPrompt'
import { getReadResponse } from '../api/generated/clients/getReadResponse'

export type ChatStatus = 'ready' | 'submitted' | 'streaming' | 'error'

export interface ChatTextPart {
  type: 'text'
  id: string
  text: string
}

export interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  parts: ChatTextPart[]
}

/** No explicit end-of-turn signal comes from the server, so a response is
 * considered finished once no new chunk has arrived for this long. */
const READY_DELAY_MS = 800

export function useAgentChat() {
  const messages = ref<ChatMessage[]>([])
  const status = ref<ChatStatus>('ready')

  let readyTimeout: ReturnType<typeof setTimeout> | undefined
  let assistantMessage: ChatMessage | undefined
  let responseStreamStarted = false

  function scheduleReady() {
    clearTimeout(readyTimeout)
    readyTimeout = setTimeout(() => {
      status.value = 'ready'
      assistantMessage = undefined
    }, READY_DELAY_MS)
  }

  async function consumeResponseStream() {
    const { stream } = await getReadResponse()

    for await (const event of stream) {
      const chunk = typeof event.data === 'string' ? event.data : ''
      if (!chunk) continue

      if (!assistantMessage) {
        assistantMessage = {
          id: crypto.randomUUID(),
          role: 'assistant',
          parts: [{ type: 'text', id: crypto.randomUUID(), text: '' }],
        }
        messages.value.push(assistantMessage)
      }

      assistantMessage.parts[0].text += chunk
      status.value = 'streaming'
      scheduleReady()
    }
  }

  async function sendPrompt(prompt: string) {
    const trimmed = prompt.trim()
    if (!trimmed || status.value === 'submitted' || status.value === 'streaming') return

    messages.value.push({
      id: crypto.randomUUID(),
      role: 'user',
      parts: [{ type: 'text', id: crypto.randomUUID(), text: trimmed }],
    })

    status.value = 'submitted'
    assistantMessage = undefined

    if (!responseStreamStarted) {
      responseStreamStarted = true
      consumeResponseStream().catch(() => {
        status.value = 'error'
      })
    }

    try {
      await postSendPrompt({ query: { prompt: trimmed } })
    } catch {
      status.value = 'error'
    }
  }

  return { messages, status, sendPrompt }
}
