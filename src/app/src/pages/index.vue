<template>
  <div class="flex h-full flex-col gap-3 p-4">
    <div class="shrink-0 rounded-md bg-elevated px-3 py-1.5 text-sm font-semibold">
      Zeeq Planner
    </div>

    <div class="flex min-h-0 flex-1 gap-4">
      <UCard
        class="flex min-h-0 flex-[3] flex-col"
        :ui="{ body: 'flex-1 min-h-0 overflow-hidden p-0 sm:p-0', footer: 'shrink-0' }"
      >
        <UEditor
          v-slot="{ editor }"
          v-model="planContent"
          content-type="markdown"
          placeholder="Start planning..."
          :ui="{ root: 'flex flex-col', content: 'flex-1 min-h-0 overflow-y-auto', base: 'px-4' }"
          class="h-full w-full"
        >
          <UEditorToolbar
            :editor="editor"
            :items="planToolbarItems"
            class="shrink-0 border-b border-muted bg-default px-4 py-2 overflow-x-auto"
          />

          <UEditorDragHandle :editor="editor" />
        </UEditor>

        <template #footer>
          <div class="flex items-center justify-between gap-2">
            <USelect
              v-model="selectedPlanId"
              :items="plans"
              placeholder="Select a plan"
              class="w-48"
            />
            <UButton
              label="Save"
              @click="onSaveClick"
            />
          </div>
        </template>
      </UCard>

      <UModal
        v-model:open="isNameModalOpen"
        title="Name this specification"
        description="Your plan doesn't start with a title, so give it a name to save it."
      >
        <template #body>
          <UFormField
            label="Name"
            required
          >
            <UInput
              v-model="specNameInput"
              placeholder="e.g. Q3 Roadmap"
              autofocus
              class="w-full"
              @keyup.enter="specNameInput.trim() && confirmSpecName()"
            />
          </UFormField>
        </template>

        <template #footer>
          <div class="flex justify-end gap-2">
            <UButton
              label="Cancel"
              color="neutral"
              variant="ghost"
              @click="cancelSpecName"
            />
            <UButton
              label="Save"
              :disabled="!specNameInput.trim()"
              @click="confirmSpecName"
            />
          </div>
        </template>
      </UModal>

      <UCard
        class="flex min-h-0 flex-[2] flex-col"
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
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { Markdown } from '@comark/vue'
import shiki from '@comark/vue/plugins/shiki'
import type { EditorToolbarItem } from '@nuxt/ui'
import { useAgentChat } from '../composables/useAgentChat'
import { extractTitle, usePlanEditor } from '../composables/usePlanEditor'

const input = ref('')
const { messages, status, sendPrompt } = useAgentChat()

function onSubmit() {
  const prompt = input.value
  input.value = ''
  sendPrompt(prompt)
}

const { content: planContent, plans, selectedPlanId, save } = usePlanEditor()

const isNameModalOpen = ref(false)
const specNameInput = ref('')
let resolveSpecName: ((name: string | undefined) => void) | undefined

function promptSpecName() {
  specNameInput.value = ''
  isNameModalOpen.value = true
  return new Promise<string | undefined>((resolve) => {
    resolveSpecName = resolve
  })
}

function confirmSpecName() {
  isNameModalOpen.value = false
}

function cancelSpecName() {
  specNameInput.value = ''
  isNameModalOpen.value = false
}

watch(isNameModalOpen, (open) => {
  if (!open && resolveSpecName) {
    resolveSpecName(specNameInput.value.trim() || undefined)
    resolveSpecName = undefined
  }
})

async function onSaveClick() {
  const name = extractTitle(planContent.value) ?? await promptSpecName()
  if (!name) return
  save(name)
}

const planToolbarItems: EditorToolbarItem[][] = [[{
  icon: 'i-lucide-heading',
  tooltip: { text: 'Headings' },
  content: { align: 'start' },
  items: [{
    kind: 'heading',
    level: 1,
    icon: 'i-lucide-heading-1',
    label: 'Heading 1',
  }, {
    kind: 'heading',
    level: 2,
    icon: 'i-lucide-heading-2',
    label: 'Heading 2',
  }, {
    kind: 'heading',
    level: 3,
    icon: 'i-lucide-heading-3',
    label: 'Heading 3',
  }],
}, {
  icon: 'i-lucide-list',
  tooltip: { text: 'Lists' },
  content: { align: 'start' },
  items: [{
    kind: 'bulletList',
    icon: 'i-lucide-list',
    label: 'Bullet List',
  }, {
    kind: 'orderedList',
    icon: 'i-lucide-list-ordered',
    label: 'Ordered List',
  }],
}, {
  kind: 'blockquote',
  icon: 'i-lucide-text-quote',
  tooltip: { text: 'Blockquote' },
}, {
  kind: 'codeBlock',
  icon: 'i-lucide-square-code',
  tooltip: { text: 'Code Block' },
}], [{
  kind: 'mark',
  mark: 'bold',
  icon: 'i-lucide-bold',
  tooltip: { text: 'Bold' },
}, {
  kind: 'mark',
  mark: 'italic',
  icon: 'i-lucide-italic',
  tooltip: { text: 'Italic' },
}, {
  kind: 'mark',
  mark: 'strike',
  icon: 'i-lucide-strikethrough',
  tooltip: { text: 'Strikethrough' },
}, {
  kind: 'mark',
  mark: 'code',
  icon: 'i-lucide-code',
  tooltip: { text: 'Code' },
}, {
  kind: 'link',
  icon: 'i-lucide-link',
  tooltip: { text: 'Link' },
}]]
</script>
