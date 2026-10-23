import { createInertiaApp } from '@inertiajs/vue3'
import { createApp, createSSRApp, h, type DefineComponent } from 'vue'

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob<{ default: DefineComponent }>('./pages/**/*.vue', { eager: true })
    const page = pages[`./pages/${name}.vue`]
    if (!page) throw new Error(`Unknown Inertia page: ${name}`)
    return page
  },
  setup({ el, App, props, plugin }) {
    const create = !el || el.hasAttribute('data-server-rendered') ? createSSRApp : createApp
    const app = create({ render: () => h(App, props) }).use(plugin)
    if (el) app.mount(el)
    return app
  },
})
