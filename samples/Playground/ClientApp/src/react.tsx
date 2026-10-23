import { createInertiaApp } from '@inertiajs/react'
import { createRoot, hydrateRoot } from 'react-dom/client'
import Todos from './react/Todos'
import './style.css'

createInertiaApp({
  id: 'playground-root',
  resolve: name => {
    if (name !== 'React/Todos') throw new Error(`Unknown React page: ${name}`)
    return Todos
  },
  setup({ el, App, props }) {
    if (el.hasAttribute('data-server-rendered')) hydrateRoot(el, <App {...props} />)
    else createRoot(el).render(<App {...props} />)
  },
})
