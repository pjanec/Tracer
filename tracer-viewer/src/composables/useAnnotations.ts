import { ref, watch, type Ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import { useAnnotationStore } from '@/stores/annotationStore';
import type { AnnotationDto, CreateAnnotationDto, UpdateAnnotationDto } from '@/api/tracerApiClient';

export interface AnnotationTarget {
  eventId?: string;
  entityId?: string;
  traceId?: string;
}

/**
 * Composable for annotation CRUD. Loads annotations for a given sessionId
 * and optional target filter. Syncs results into annotationStore.
 */
export function useAnnotations(sessionId: Ref<string | null>) {
  const store = useAnnotationStore();
  const loading = ref(false);
  const error = ref<string | null>(null);

  async function load() {
    const sid = sessionId.value;
    if (!sid) {
      store.clear();
      return;
    }
    loading.value = true;
    error.value = null;
    try {
      const items = await api.listAnnotations(sid);
      store.load(items);
    } catch (err: unknown) {
      error.value = err instanceof Error ? err.message : String(err);
    } finally {
      loading.value = false;
    }
  }

  async function create(
    body: string,
    kind: string,
    target: AnnotationTarget,
    title?: string,
    tags?: string[],
  ): Promise<AnnotationDto> {
    const sid = sessionId.value;
    if (!sid) throw new Error('No active session');
    const author = localStorage.getItem('tracer:authorName') ?? 'anonymous';
    const dto: CreateAnnotationDto = {
      sessionId: sid,
      kind,
      body,
      title,
      tags: tags ?? [],
      author,
      ...target,
    };
    const created = await api.createAnnotation(dto);
    store.upsert(created);
    return created;
  }

  async function update(
    annotationId: string,
    body: string,
    title?: string,
    tags?: string[],
  ): Promise<void> {
    const dto: UpdateAnnotationDto = { body, title, tags };
    const updated = await api.updateAnnotation(annotationId, dto);
    if (updated) store.upsert(updated);
  }

  async function remove(annotationId: string): Promise<void> {
    await api.deleteAnnotation(annotationId);
    store.remove(annotationId);
  }

  const stopWatch = watch(sessionId, () => { void load(); }, { immediate: true });

  return { loading, error, annotations: store.all, create, update, remove, stopWatch };
}
