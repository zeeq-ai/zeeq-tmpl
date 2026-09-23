<script setup lang="ts">
import { Markdown } from '@comark/vue'
import shiki from '@comark/vue/plugins/shiki'
import type { ChatMessage, ChatToolPart } from '../../composables/useAgentChat'

defineProps<{ message: ChatMessage }>()

const plugins = [shiki()]

function humanize(name: string) {
  return name
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, char => char.toUpperCase())
}

function toolIcon(name: string) {
  const normalized = name.toLowerCase()
  if (/diff/.test(normalized)) return 'i-lucide-file-diff'
  if (/search|grep|find/.test(normalized)) return 'i-lucide-search'
  if (/terminal|shell|bash|command|exec|run/.test(normalized)) return 'i-lucide-terminal'
  if (/edit|write|patch|apply/.test(normalized)) return 'i-lucide-file-pen'
  if (/read|cat|view/.test(normalized)) return 'i-lucide-book-open'
  if (/glob|list|folder/.test(normalized)) return 'i-lucide-folder-search'
  if (/fetch|http|web|url/.test(normalized)) return 'i-lucide-globe'
  return 'i-lucide-wrench'
}

function isTool(part: ChatMessage['parts'][number]): part is ChatToolPart {
  return part.type === 'tool'
}
</script>

<template>
  <template
    v-for="part in message.parts"
    :key="part.id"
  >
    <UChatReasoning
      v-if="part.type === 'reasoning'"
      :text="part.text"
      :streaming="part.streaming"
      :duration="part.duration"
      icon="i-lucide-brain"
      chevron="leading"
    >
      <Markdown
        :value="part.text"
        :streaming="part.streaming"
        :plugins="plugins"
      />
    </UChatReasoning>

    <UChatTool
      v-else-if="isTool(part)"
      :text="humanize(part.name)"
      :suffix="part.description"
      :icon="toolIcon(part.name)"
      :loading="part.state === 'running'"
      :streaming="part.state === 'running'"
      chevron="leading"
      variant="card"
    >
      <pre
        v-if="part.result"
        class="text-xs whitespace-pre-wrap"
        v-text="part.result"
      />
      <p
        v-else-if="part.error"
        class="text-sm text-error"
      >
        {{ part.error }}
      </p>
      <p
        v-else-if="part.progress"
        class="text-sm text-dimmed"
      >
        {{ part.progress }}
      </p>
    </UChatTool>

    <UChatTool
      v-else-if="part.type === 'diff'"
      text="Received specification diff"
      icon="i-lucide-file-diff"
      chevron="leading"
      variant="card"
    >
      <Markdown
        v-if="part.diff"
        :value="part.diff"
        :plugins="plugins"
      />
    </UChatTool>

    <template v-else-if="part.type === 'text'">
      <Markdown
        v-if="message.role === 'assistant'"
        :value="part.text"
        :streaming="part.streaming"
        :plugins="plugins"
        unwrap
      />
      <p
        v-else
        class="whitespace-pre-wrap"
      >
        {{ part.text }}
      </p>
    </template>

    <p
      v-else-if="part.type === 'intent'"
      class="flex items-center gap-1.5 text-sm text-muted"
    >
      <UIcon
        name="i-lucide-activity"
        class="size-4 shrink-0"
      />
      <UChatShimmer
        v-if="part.streaming"
        :text="part.text"
      />
      <span v-else>{{ part.text }}</span>
    </p>

    <UAlert
      v-else-if="part.type === 'error'"
      color="error"
      icon="i-lucide-triangle-alert"
      :description="part.message"
    />
  </template>
</template>
