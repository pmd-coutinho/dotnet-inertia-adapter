<script setup lang="ts">
import { Head, router, useForm, usePage } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import Todos, { type TodosStorePayload, type TodosLoginPayload } from '../pathfinder/actions/todos'
import type { PropsFor } from '../pathfinder/types/page-map'
import type { VueTodosProps } from '../pathfinder/types/Vue.Todos'
import { formFields } from '../pathfinder'

type Props = PropsFor<'Vue/Todos'>
defineOptions({ inheritAttrs: false })
const props = defineProps<VueTodosProps>()
const page = usePage<Props>()
const form = useForm<TodosStorePayload>({ title: '', done: false })
const login = useForm<TodosLoginPayload>({ name: 'Reader' })
const editing = ref<string | null>(null)
const flash = computed(() => typeof page.flash?.message === 'string' ? page.flash.message : '')
function submit() {
  const action = editing.value ? Todos.update({ client: props.client, id: editing.value }) : Todos.store(props.client)
  form.submit(action, { errorBag: 'todo', onSuccess: () => { form.reset(); editing.value = null } })
}
</script>

<template>
  <main>
    <Head title="Tasks · InertiaNet" />
    <header>
      <p class="eyebrow">{{ page.props.app?.name ?? 'InertiaNet playground' }} · Vue</p>
      <h1>A little room to get things done.</h1>
      <nav><a href="/react/todos">React</a><a href="/vue/todos">Vue</a><a :href="Todos.index.url(client, { query: { failSsr: true } })">Try SSR fallback</a></nav>
    </header>
    <div role="status" data-testid="flash">{{ flash }}</div>
    <section class="panel">
      <h2>Your workspace</h2>
      <p data-testid="auth">{{ page.props.auth?.user ? `Hello, ${page.props.auth.user.name}` : 'Guest workspace' }}</p>
      <form @submit.prevent="login.submit(Todos.login(client))" class="inline">
        <label>Display name<input v-model="login.name" /></label><button :disabled="login.processing">Sign in</button>
      </form>
    </section>
    <section class="panel">
      <h2>{{ editing ? 'Edit task' : 'Add a task' }}</h2>
      <form @submit.prevent="submit">
        <label>Task title<input v-model="form.title" /></label>
        <p class="error" data-testid="error">{{ form.errors.title }}</p><button :disabled="form.processing">Save task</button>
      </form>
    </section>
    <section class="panel">
      <h2>Tasks <span data-testid="total">{{ total }}</span></h2>
      <p data-testid="stats">{{ stats ? `${stats.total} tasks, counted in a deferred request` : 'Loading workspace totals…' }}</p>
      <ul data-testid="todos">
        <li v-for="todo in todos.data" :key="todo.id" :data-testid="`todo-${todo.id}`">
          <span>{{ todo.title }}</span>
          <div class="inline">
            <button @click="editing = todo.id; form.title = todo.title; form.done = todo.done">Edit {{ todo.title }}</button>
            <button @click="router.visit(Todos.destroy({ client, id: todo.id }))">Delete {{ todo.title }}</button>
            <form :action="Todos.destroy.form({ client, id: todo.id }).action" method="post">
              <input v-for="field in formFields(Todos.destroy.form({ client, id: todo.id }))" :key="field.name" type="hidden" :name="field.name" :value="field.value" />
              <input type="hidden" name="__RequestVerificationToken" :value="page.props.xsrfToken ?? ''" />
              <button>Delete natively {{ todo.title }}</button>
            </form>
          </div>
        </li>
      </ul>
      <button v-if="todos.nextPage" @click="router.visit(Todos.index(client, { query: { page: todos.nextPage } }), { only: ['todos'], preserveState: true, preserveScroll: true })">Load more</button>
    </section>
  </main>
</template>
