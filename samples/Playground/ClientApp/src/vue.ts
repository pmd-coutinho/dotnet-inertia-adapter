import { createInertiaApp } from '@inertiajs/vue3'
import { createApp, createSSRApp, h, type DefineComponent } from 'vue'
import './style.css'

createInertiaApp({
  id: 'playground-root',
  resolve: name => {
    if (name !== 'Vue/Todos') throw new Error(`Unknown Vue page: ${name}`)
    return import.meta.glob<{ default: DefineComponent }>('./vue/*.vue', { eager: true })['./vue/Todos.vue']!
  },
  setup({ el, App, props, plugin }) {
    const create = el.hasAttribute('data-server-rendered') ? createSSRApp : createApp
    create({ render: () => h(App, props) }).use(plugin).mount(el)
  },
})
