import { ref, watch } from 'vue'
import type { SelectItem } from '@nuxt/ui'
import { createPatch } from 'diff'
import { getSpecifications } from '../api/generated/clients/getSpecifications'
import { postSpecifications } from '../api/generated/clients/postSpecifications'
import { postSpecificationsIdDiff } from '../api/generated/clients/postSpecificationsIdDiff'
import type { SpecificationDto } from '../api/generated/types/SpecificationDto'

const TITLE_LINE_RE = /^#\s+(.+?)\s*$/

/** Extracts the name from a markdown H1 title line, if the first line is one. */
export function extractTitle(markdown: string): string | undefined {
  const firstLine = markdown.split('\n', 1)[0] ?? ''
  return firstLine.match(TITLE_LINE_RE)?.[1]
}

export function usePlanEditor() {
  const content = ref('')
  const plans = ref<SelectItem[]>([])
  const selectedPlanId = ref<string>()
  const specifications = ref<SpecificationDto[]>([])

  async function refreshPlans() {
    const result = await getSpecifications()
    const data: SpecificationDto[] = result.data
    specifications.value = data

    const items: { label: string, value: string }[] = []
    for (const spec of data) {
      items.push({ label: spec.name, value: spec.id })
    }
    plans.value = items
  }

  watch(selectedPlanId, (id) => {
    const spec = specifications.value.find(s => s.id === id)
    if (spec) content.value = spec.content
  })

  async function save(name: string) {
    const previousContent = specifications.value.find(s => s.id === selectedPlanId.value)?.content ?? ''
    const newContent = content.value

    const { data } = await postSpecifications({
      body: { id: selectedPlanId.value ?? null, name, content: newContent },
    })
    await refreshPlans()
    selectedPlanId.value = data.id

    if (previousContent === newContent) return

    const diff = createPatch(name, previousContent, newContent)
    try {
      await postSpecificationsIdDiff({ path: { id: data.id }, query: { diff } })
    }
    catch {
      // Best-effort: the spec is already saved even if forwarding the diff to the agent fails.
    }
  }

  refreshPlans()

  return { content, plans, selectedPlanId, save }
}
