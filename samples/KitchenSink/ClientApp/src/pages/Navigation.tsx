import {
  Head,
  Link,
  router,
  useForm,
  usePage,
  useRemember,
} from "@inertiajs/react";
import { Code, Intro } from "../Layout";
import type { Shared } from "../types";
import Demos from "../pathfinder/actions/demos";

export default function Navigation({ encrypted }: { encrypted: boolean }) {
  const page = usePage<Shared>();
  const [note, setNote] = useRemember("", "kitchen-sink-note");
  const login = useForm({ name: "" });
  return (
    <>
      <Head title="Navigation & state" />
      <Intro
        eyebrow="07 / A SENSE OF PLACE"
        title="Stay right where you belong."
        tag="STATE & HISTORY"
      >
        Persistent layouts, remembered inputs, prefetching and cookie
        authentication. Inspect the response to see shared props and history
        flags.
      </Intro>
      <div className="two-columns equal">
        <section className="panel">
          <h2>A note for your return</h2>
          <p>
            Type something, visit another demo, then use your browser’s Back
            button. Inertia restores this local state from history.
          </p>
          <label>
            Your remembered note
            <textarea
              value={note}
              onChange={(event) => setNote(event.target.value)}
              rows={4}
            />
          </label>
          <Link
            href="/contacts"
            className="button secondary"
            prefetch="hover"
            cacheFor="30s"
          >
            Visit contacts · prefetched on hover ↗
          </Link>
          <Code>
            {
              'const [note, setNote] = useRemember("", "note")\n\n<Link href="/contacts" prefetch="hover" />'
            }
          </Code>
        </section>
        <section className="panel">
          <h2>
            {page.props.auth.name
              ? `Hello, ${page.props.auth.name}.`
              : "A guest, for now."}
          </h2>
          <p>
            This local demo uses a display-name cookie identity. It demonstrates
            shared authentication props; it has no password or account system.
          </p>
          {page.props.auth.name ? (
            <>
              <p className="success-note" data-testid="member-message">
                {page.props.memberMessage}
              </p>
              <button onClick={() => router.visit(Demos.logout())}>
                Sign out & clear history
              </button>
            </>
          ) : (
            <form
              className="stack"
              onSubmit={(event) => {
                event.preventDefault();
                login.submit(Demos.login(), { errorBag: "login" });
              }}
            >
              <label>
                Display name
                <input
                  value={login.data.name}
                  onChange={(event) =>
                    login.setData("name", event.target.value)
                  }
                />
              </label>
              <span className="error">{login.errors.name}</span>
              <button disabled={login.processing}>Sign in locally</button>
            </form>
          )}
          <p className="muted">
            Signing out clears Inertia’s history encryption key. Flash messages
            appear once and are excluded from history state.
          </p>
        </section>
        <section className="panel">
          <h2>History encryption</h2>
          <p data-testid="encryption">
            Encryption is {encrypted ? "enabled" : "disabled"} for this
            response.
          </p>
          <Link
            className="button secondary"
            href={`/navigation?encrypted=${!encrypted}`}
          >
            {encrypted ? "Disable" : "Enable"} history encryption
          </Link>
          <p className="muted">
            Requires a secure browser context (HTTPS or localhost). Check{" "}
            <code>encryptHistory</code> in the inspector.
          </p>
        </section>
        <section className="panel" id="fragment-demo">
          <h2>Visits & redirects</h2>
          <div className="stack">
            <a href="/navigation#fragment-demo">Set a URL fragment</a>
            <button
              className="secondary"
              onClick={() => router.post("/navigation/fragment")}
            >
              Redirect & preserve fragment
            </button>
            <Link href="/navigation/location">
              Try a full-location redirect ↗
            </Link>
            <Link href="/minimal">Visit a Minimal API page ↗</Link>
          </div>
          <p className="muted">
            The location example returns 409 + X-Inertia-Location, then the
            browser makes a full visit to this sample.
          </p>
        </section>
      </div>
    </>
  );
}
