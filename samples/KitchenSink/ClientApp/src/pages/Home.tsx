import { Head, Link } from "@inertiajs/react";
import { Code, demos, Intro } from "../Layout";

export default function Home({ contacts }: { contacts: number }) {
  return (
    <>
      <Head title="Kitchen sink">
        <meta
          name="description"
          content="An interactive collection of Inertia.js features, built with ASP.NET Core and React."
        />
      </Head>
      <Intro
        eyebrow="A HANDS-ON FIELD GUIDE"
        title="Everything, in good company."
        tag="INERTIA v3"
      >
        One application. All the moving parts. Explore how classic server-side
        routing meets a modern React interface, one working example at a time.
      </Intro>
      <section className="hero-panel">
        <div>
          <span className="eyebrow">LESS GLUE. MORE BUILDING.</span>
          <h2>
            Your server routes.
            <br />
            Your React components.
            <br />
            <em>A rather nice connection.</em>
          </h2>
          <p>
            No separate API to maintain. Just controllers, page props, and the
            interface you wanted to build.
          </p>
          <Link className="button" href="/contacts">
            Meet the contacts demo <span>↗</span>
          </Link>
        </div>
        <div className="connection">
          <div className="connection-node">
            <span className="node-icon">⌘</span>
            <div>
              ASP.NET Core<small>Controllers · SQLite · Validation</small>
            </div>
            <span className="node-label">SERVER</span>
          </div>
          <div className="connection-bridge">
            <span /> <b>Inertia</b>
            <small>props down · visits up</small>
            <span />
          </div>
          <div className="connection-node">
            <span className="node-icon react">✳</span>
            <div>
              React<small>Components · Forms · State</small>
            </div>
            <span className="node-label">CLIENT</span>
          </div>
          <div className="connection-caption">
            <span className="status-dot" /> Same routes. No full-page reloads.
          </div>
        </div>
      </section>
      <div className="section-heading">
        <div>
          <span className="eyebrow">LEARN BY DOING</span>
          <h2>Pick something to play with.</h2>
        </div>
        <span className="muted">8 interactive demos</span>
      </div>
      <div className="demo-grid">
        {demos.slice(1).map((demo, index) => (
          <Link className="demo-card" href={demo.href} key={demo.href} prefetch>
            <div className="card-top">
              <span className="card-icon">{demo.icon}</span>
              <span className="card-number">
                {String(index + 1).padStart(2, "0")} ↗
              </span>
            </div>
            <h3>{demo.name}</h3>
            <p>{demo.detail}</p>
            <span className="card-action">
              Explore example <span>→</span>
            </span>
          </Link>
        ))}
      </div>
      <section className="bottom-note">
        <div>
          <span className="eyebrow">REAL REQUESTS, REAL RESPONSES</span>
          <h2>A sample you can take apart.</h2>
          <p>
            {contacts} contacts in SQLite. Generated TypeScript routes. A
            persistent layout. Open the response inspector on any page to see
            what Inertia sends over the wire.
          </p>
        </div>
        <Code>
          {
            'return inertia.Render("Contacts", new\n{\n    contacts = repository.Page(),\n});'
          }
        </Code>
      </section>
    </>
  );
}
