<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount, shallowRef } from 'vue';
import { EditorState } from '@codemirror/state';
import { EditorView, lineNumbers, highlightActiveLine, keymap } from '@codemirror/view';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { searchKeymap } from '@codemirror/search';
import { autocompletion, CompletionContext, type Completion } from '@codemirror/autocomplete';
import { sql, SQLite } from '@codemirror/lang-sql';
import { oneDark } from '@codemirror/theme-one-dark';
import type { SqlSchemaDto } from '@/types/sql';

const props = defineProps<{
  modelValue: string;
  schema: SqlSchemaDto | null;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: string];
  run: [];
}>();

const container = ref<HTMLDivElement | null>(null);
const view = shallowRef<EditorView | null>(null);

function customCompletions(context: CompletionContext) {
  const word = context.matchBefore(/\w*/);
  if (!word || (word.from === word.to && !context.explicit)) return null;
  const options: Completion[] = [];
  if (props.schema) {
    for (const table of props.schema.tables) {
      options.push({ label: table.name, type: 'keyword', detail: 'table' });
      for (const col of table.columns) {
        options.push({ label: col.name, type: 'variable', detail: col.duckType });
      }
    }
  }
  return { from: word.from, options };
}

function createEditor(doc: string): EditorView {
  const state = EditorState.create({
    doc,
    extensions: [
      lineNumbers(),
      highlightActiveLine(),
      history(),
      sql({ dialect: SQLite }),
      oneDark,
      autocompletion({ override: [customCompletions] }),
      keymap.of([
        ...defaultKeymap,
        ...historyKeymap,
        ...searchKeymap,
        {
          key: 'Mod-Enter',
          run() {
            emit('run');
            return true;
          },
        },
      ]),
      EditorView.updateListener.of((update) => {
        if (update.docChanged) {
          emit('update:modelValue', update.state.doc.toString());
        }
      }),
    ],
  });
  return new EditorView({ state, parent: container.value! });
}

onMounted(() => {
  view.value = createEditor(props.modelValue);
});

onBeforeUnmount(() => {
  view.value?.destroy();
  view.value = null;
});

watch(
  () => props.modelValue,
  (val) => {
    const v = view.value;
    if (!v) return;
    const current = v.state.doc.toString();
    if (current !== val) {
      v.dispatch({
        changes: { from: 0, to: current.length, insert: val },
      });
    }
  },
);

function focus() {
  view.value?.focus();
}

function getSelection(): string {
  const v = view.value;
  if (!v) return '';
  const { from, to } = v.state.selection.main;
  return v.state.doc.sliceString(from, to);
}

defineExpose({ focus, getSelection });
</script>

<template>
  <div ref="container" class="sql-editor" />
</template>

<style lang="scss">
.sql-editor {
  height: 100%;
  overflow: auto;
  .cm-editor {
    height: 100%;
    font-size: 0.85rem;
  }
}
</style>
