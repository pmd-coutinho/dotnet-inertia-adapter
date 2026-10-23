import { createInertiaApp } from '@inertiajs/react'
import type { ComponentType } from 'react'
import { createRoot, hydrateRoot } from 'react-dom/client'

createInertiaApp({
  resolve: (name) => {
    const pages = import.meta.glob<{ default: ComponentType }>('./pages/**/*.tsx', { eager: true })
    const page = pages[`./pages/${name}.tsx`]
    if (!page) throw new Error(`Unknown Inertia page: ${name}`)
    return page
  },
  setup({ el, App, props }) {
    const app = <App {...props} />
    if (!el) return app
    if (el.hasAttribute('data-server-rendered')) hydrateRoot(el, app)
    else createRoot(el).render(app)
    return app
  },
})
