import {
  Head,
  Link,
  router,
  useForm,
  usePage,
  useRemember,
} from "@inertiajs/react";
import { Code, Intro } from "../Layout";
import type { PropsFor } from "../pathfinder/types/page-map";
import Demos, {
  type DemosLoginPayload,
  type DemosNavigationQuery,
} from "../pathfinder/actions/demos";
import MinimalApi from "../pathfinder/actions/minimalapi";

export default function Navigation({ encrypted }: PropsFor<"Navigation">) {
  const page = usePage<PropsFor<"Navigation">>();
  const [note, setNote] = useRemember("", "kitchen-sink-note");
  const login = useForm<DemosLoginPayload>({ name: "" });
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
            href={Demos.contacts()}
            className="button secondary"
            prefetch="hover"
            cacheFor="30s"
          >
            Visit contacts · prefetched on hover ↗
          </Link>
          <Code>
            {
              'const [note, setNote] = useRemember("", "note")\n\n<Link href={Demos.contacts()} prefetch="hover" />'
            }
          </Code>
        </section>
        <section className="panel">
          <h2>
            {page.props.auth?.name
              ? `Hello, ${page.props.auth.name}.`
              : "A guest, for now."}
          </h2>
          <p>
            This local demo uses a display-name cookie identity. It demonstrates
            shared authentication props; it has no password or account system.
          </p>
          {page.props.auth?.name ? (
            <>
              <p className="success-note" data-testid="member-message">
                {typeof page.props.memberMessage === "string"
                  ? page.props.memberMessage
                  : null}
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
            href={Demos.navigation({
              query: { encrypted: !encrypted } satisfies DemosNavigationQuery,
            })}
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
            <a href={`${Demos.navigation.url()}#fragment-demo`}>
              Set a URL fragment
            </a>
            <button
              className="secondary"
              onClick={() => {
                const action = Demos.fragment();
                // Inertia preserves the outgoing visit's hash, not the current page's hash.
                router.visit({
                  ...action,
                  url: action.url + window.location.hash,
                });
              }}
            >
              Redirect & preserve fragment
            </button>
            <Link href={Demos.locationDemo()}>
              Try a full-location redirect ↗
            </Link>
            <Link href={MinimalApi.getMinimal()}>
              Visit a Minimal API page ↗
            </Link>
          </div>
          <p className="muted">
            The outgoing visit includes the current URL fragment. The server's
            preserveFragment flag keeps that fragment across its redirect.
          </p>
          <p className="muted">
            The location example returns 409 + X-Inertia-Location, then the
            browser makes a full visit to this sample.
          </p>
        </section>
      </div>
    </>
  );
}
