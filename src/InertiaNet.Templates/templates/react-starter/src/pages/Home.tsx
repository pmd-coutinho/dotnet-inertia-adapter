type HomeProps = {
  title?: string
}

export default function Home({ title = 'InertiaNet React Starter' }: HomeProps) {
  return (
    <main style={{ fontFamily: 'Inter, system-ui, sans-serif', padding: '3rem', maxWidth: 720, margin: '0 auto' }}>
      <h1>{title}</h1>
      <p>Your ASP.NET Core + InertiaNet + React starter is ready.</p>
    </main>
  )
}
