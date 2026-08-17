<template>
  <div class="flex h-full justify-center p-4">
    <UCard
      class="flex h-full w-full max-w-md flex-col"
      :ui="{ body: 'flex-1 min-h-0 overflow-y-auto', footer: 'shrink-0' }"
    >
      <UChatMessages
        :messages="messages"
        :status="status"
      >
        <template #content="{ message }">
          <template
            v-for="part in message.parts"
            :key="part.id"
          >
            <Markdown
              v-if="part.type === 'text'"
              :value="part.text"
              :plugins="[shiki()]"
              :streaming="message.role === 'assistant' && status === 'streaming'"
              unwrap
            />
          </template>
        </template>
      </UChatMessages>

      <template #footer>
        <UChatPrompt
          v-model="input"
          :status="status"
          @submit="onSubmit"
        >
          <UChatPromptSubmit :status="status" />
        </UChatPrompt>
      </template>
    </UCard>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { Markdown } from '@comark/vue'
import shiki from '@comark/vue/plugins/shiki'
import { useAgentChat } from '../composables/useAgentChat'

const input = ref('')
const { messages, status, sendPrompt } = useAgentChat()

function onSubmit() {
  const prompt = input.value
  input.value = ''
  sendPrompt(prompt)
}
</script>
