import createServer from '@inertiajs/core/server'
import { createInertiaApp as createReactInertiaApp } from '@inertiajs/react'
import { createInertiaApp as createVueInertiaApp } from '@inertiajs/vue3'
import { renderToString as renderReactToString } from 'react-dom/server'
import type { DefineComponent } from 'vue'
import { renderToString as renderVueToString } from '@vue/server-renderer'
import ReactTodos from './react/Todos'

// The render-function API carries the same custom root ID as the browser entry points.
const react = await createReactInertiaApp({
  id: 'playground-root', resolve: () => ReactTodos,
})
const vue = await createVueInertiaApp({
  id: 'playground-root',
  resolve: () => import.meta.glob<{ default: DefineComponent }>('./vue/*.vue', { eager: true })['./vue/Todos.vue']!,
})
if (typeof react !== 'function' || typeof vue !== 'function') throw new Error('SSR render functions were not initialized')

createServer(page => {
  if (page.props.forceSsrFailure) throw new Error('Requested SSR fallback demonstration')
  return page.component === 'React/Todos' ? react(page, renderReactToString) : vue(page, renderVueToString)
}, { host: '127.0.0.1', port: Number(process.env.SSR_PORT ?? 13714) })
