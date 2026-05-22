import { api } from '@/api/tracerApiClient';
import type { SavedViewDto } from '@/api/tracerApiClient';
import { useRoute } from 'vue-router';
import { usePersona } from '@/composables/usePersona';

export function useBookmarks() {
  const route = useRoute();
  const { persona } = usePersona();

  async function bookmarkCurrentUrl(sessionId: string, viewType: string): Promise<void> {
    const label = buildAutoLabel(route, viewType);
    await api.createSavedView({
      sessionId,
      kind: 'Bookmark',
      viewType,
      url: route.fullPath,
      label,
      persona: persona.value,
      author: localStorage.getItem('tracer:authorName') ?? undefined,
    });
  }

  async function listBookmarks(sessionId: string, viewType?: string): Promise<SavedViewDto[]> {
    const items = await api.listSavedViews({
      sessionId,
      kind: 'Bookmark',
      persona: persona.value,
      orderBy: 'recent',
      limit: 10,
    });
    return items.filter(b => !viewType || b.viewType === viewType);
  }

  async function removeBookmark(savedViewId: string): Promise<void> {
    await api.deleteSavedView(savedViewId);
  }

  return { bookmarkCurrentUrl, listBookmarks, removeBookmark };
}

function buildAutoLabel(route: ReturnType<typeof useRoute>, viewType: string): string {
  const parts: string[] = [];
  const q = route.query;
  if (q.topic) parts.push(String(Array.isArray(q.topic) ? q.topic[0] : q.topic));
  if (q.trace) parts.push(`trace:${String(q.trace)}`);
  if (q.entity) parts.push(`entity:${String(q.entity)}`);
  if (parts.length === 0) parts.push(viewType);
  parts.push(new Date().toISOString().slice(11, 19));
  return parts.join(' · ');
}
