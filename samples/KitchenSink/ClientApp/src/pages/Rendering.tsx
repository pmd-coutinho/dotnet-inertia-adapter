import { Head, Link } from "@inertiajs/react";
import { useEffect, useState } from "react";
import { Code, Intro } from "../Layout";

export function Rendering({ mode }: { mode: string }) {
  const [hydrated, setHydrated] = useState(false);
  useEffect(() => setHydrated(true), []);
  return (
    <>
      <Head title="Rendering" />
      <Intro
        eyebrow="08 / THE FIRST IMPRESSION"
        title="Ready before the JavaScript."
        tag="SSR & RESILIENCE"
      >
        The Node renderer produces initial HTML, then React hydrates it. If the
        renderer fails, InertiaNet can fall back to a working client-rendered
        page.
      </Intro>
      <section className="panel">
        <h2 data-testid="render-mode">{mode}</h2>
        <p data-testid="hydration">
          {hydrated
            ? "React is interactive in the browser."
            : "Rendered before hydration."}
        </p>
        <p>
          Use these full-page links to compare initial HTML. Inertia visits
          already render on the client.
        </p>
        <div className="toolbar">
          <a className="button" href="/rendering">
            Request SSR ↗
          </a>
          <a className="button secondary" href="/rendering?csr=true">
            Skip SSR for this request
          </a>
          <a className="button secondary" href="/rendering?failSsr=true">
            Simulate SSR failure
          </a>
        </div>
        <Code>
          {
            "// Per-response opt-out:\ninertia.WithoutSsr();\n\n// Gateway options:\noptions.Ssr.ThrowOnError = false;\noptions.Ssr.Timeout = TimeSpan.FromSeconds(1);"
          }
        </Code>
      </section>
      <section className="panel">
        <h2>Errors belong in the application, too.</h2>
        <p>
          Trigger an intentional exception to see the configured exception
          handler return an Inertia error page with HTTP status 500.
        </p>
        <Link href="/rendering/error" className="button secondary">
          Try the error page →
        </Link>
      </section>
    </>
  );
}
export function ErrorPage({ message }: { message: string }) {
  return (
    <>
      <Head title="Demo error" />
      <Intro
        eyebrow="AN INTENTIONAL DETOUR"
        title="A soft place to land."
        tag="HTTP 500"
      >
        {message}
      </Intro>
      <section className="panel">
        <h2>The exception became an Inertia page.</h2>
        <p>
          A prop-resolution exception goes through HandleExceptionsUsing. The
          sample sets the HTTP error status before headers are sent. The
          persistent layout keeps the application navigable.
        </p>
        <Link href="/rendering" className="button">
          Back to rendering →
        </Link>
      </section>
    </>
  );
}
export function Minimal({ message }: { message: string }) {
  return (
    <>
      <Head title="Minimal API" />
      <Intro
        eyebrow="LESS CEREMONY, SAME PROTOCOL"
        title="A very small endpoint."
        tag="MINIMAL API"
      >
        {message}
      </Intro>
      <section className="panel">
        <h2>Controllers are optional.</h2>
        <Code>
          {
            'app.MapGet("/minimal", (IInertiaService inertia) =>\n    inertia.Render("Minimal", new { message = "Hello!" }));'
          }
        </Code>
        <Link href="/navigation">← Back to navigation</Link>
      </section>
    </>
  );
}
