import { Head, router, useForm, usePage } from '@inertiajs/react'
import Todos, { type TodosStorePayload, type TodosLoginPayload } from '../pathfinder/actions/todos'
import type { PropsFor } from '../pathfinder/types/page-map'
import { formFields } from '../pathfinder'

type Props = PropsFor<'React/Todos'>

export default function TodoIndex({ client, todos, total, stats }: Props) {
  const page = usePage<Props>()
  const form = useForm<TodosStorePayload>({ title: '', done: false })
  const login = useForm<TodosLoginPayload>({ name: 'Reader' })
  const editing = useForm<{ id: string | null }>({ id: null })
  const flash = typeof page.flash?.message === 'string' ? page.flash.message : ''
  function submit(event: React.FormEvent) {
    event.preventDefault()
    const action = editing.data.id ? Todos.update({ client, id: editing.data.id }) : Todos.store(client)
    form.submit(action, { errorBag: 'todo', onSuccess: () => { form.reset(); editing.setData('id', null) } })
  }
  return <main>
    <Head title="Tasks · InertiaNet" />
    <header>
      <p className="eyebrow">{page.props.app?.name ?? 'InertiaNet playground'} · React</p>
      <h1>A little room to get things done.</h1>
      <nav><a href="/react/todos">React</a><a href="/vue/todos">Vue</a><a href={Todos.index.url(client, { query: { failSsr: true } })}>Try SSR fallback</a></nav>
    </header>
    <div role="status" data-testid="flash">{flash}</div>
    <section className="panel">
      <h2>Your workspace</h2>
      <p data-testid="auth">{page.props.auth?.user ? `Hello, ${page.props.auth.user.name}` : 'Guest workspace'}</p>
      <form onSubmit={event => { event.preventDefault(); login.submit(Todos.login(client)) }} className="inline">
        <label>Display name<input value={login.data.name} onChange={event => login.setData('name', event.target.value)} /></label>
        <button disabled={login.processing}>Sign in</button>
      </form>
    </section>
    <section className="panel">
      <h2>{editing.data.id ? 'Edit task' : 'Add a task'}</h2>
      <form onSubmit={submit}>
        <label>Task title<input value={form.data.title} onChange={event => form.setData('title', event.target.value)} /></label>
        <p className="error" data-testid="error">{form.errors.title}</p>
        <button disabled={form.processing}>Save task</button>
      </form>
    </section>
    <section className="panel">
      <h2>Tasks <span data-testid="total">{total}</span></h2>
      <p data-testid="stats">{stats ? `${stats.total} tasks, counted in a deferred request` : 'Loading workspace totals…'}</p>
      <ul data-testid="todos">
        {todos.data.map(todo => <li key={todo.id} data-testid={`todo-${todo.id}`}>
          <span>{todo.title}</span>
          <div className="inline">
            <button onClick={() => { editing.setData('id', todo.id); form.setData({ title: todo.title, done: todo.done }) }}>Edit {todo.title}</button>
            <button onClick={() => router.visit(Todos.destroy({ client, id: todo.id }))}>Delete {todo.title}</button>
            <form action={Todos.destroy.form({ client, id: todo.id }).action} method="post">
              {formFields(Todos.destroy.form({ client, id: todo.id })).map(field => <input key={field.name} type="hidden" name={field.name} value={field.value} />)}
              <input type="hidden" name="__RequestVerificationToken" value={page.props.xsrfToken ?? ''} />
              <button>Delete natively {todo.title}</button>
            </form>
          </div>
        </li>)}
      </ul>
      {todos.nextPage && <button onClick={() => router.visit(Todos.index(client, { query: { page: todos.nextPage } }), { only: ['todos'], preserveState: true, preserveScroll: true })}>Load more</button>}
    </section>
  </main>
}
