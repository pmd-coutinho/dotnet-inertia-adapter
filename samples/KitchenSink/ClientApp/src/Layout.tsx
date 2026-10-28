import { Link, router, usePage } from "@inertiajs/react";
import { useEffect, useState, type ReactNode } from "react";
import Demos from "./pathfinder/actions/demos";

export const demos = [
  {
    href: Demos.home.url(),
    name: "Overview",
    icon: "◈",
    detail: "Start exploring",
  },
  {
    href: Demos.contacts.url(),
    name: "Contacts",
    icon: "◉",
    detail: "CRUD · search · pagination",
  },
  {
    href: Demos.forms.url(),
    name: "Forms & uploads",
    icon: "▤",
    detail: "Validation · Precognition",
  },
  {
    href: Demos.props.url(),
    name: "Loading props",
    icon: "◷",
    detail: "Deferred · optional · once",
  },
  {
    href: Demos.merging.url(),
    name: "Merging data",
    icon: "⇄",
    detail: "Append · prepend · deep merge",
  },
  {
    href: Demos.feed.url(),
    name: "Infinite scroll",
    icon: "≋",
    detail: "Scroll-aware pagination",
  },
  {
    href: Demos.polling.url(),
    name: "Live updates",
    icon: "◌",
    detail: "Polling · partial reloads",
  },
  {
    href: Demos.navigation.url(),
    name: "Navigation & state",
    icon: "↗",
    detail: "History · auth · prefetch",
  },
  {
    href: Demos.rendering.url(),
    name: "Rendering",
    icon: "⌘",
    detail: "SSR · fallback · errors",
  },
];

export default function Layout({ children }: { children: ReactNode }) {
  // Pathfinder augments Inertia's PageProps with the generated shared contract.
  const page = usePage();
  const [events, setEvents] = useState<string[]>([]);
  const [inspect, setInspect] = useState(false);
  useEffect(() => {
    const remove = [
      "start",
      "success",
      "error",
      "finish",
      "prefetching",
      "prefetched",
    ].map((name) =>
      router.on(name as "start", () =>
        setEvents((previous) => [name, ...previous].slice(0, 8)),
      ),
    );
    return () => remove.forEach((unsubscribe) => unsubscribe());
  }, []);
  const current = page.url.split(/[?#]/)[0];
  const flash =
    typeof page.flash?.message === "string" ? page.flash.message : "";
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <Link href={Demos.home()} className="brand">
          <span className="brand-mark">i.</span>
          <span>
            InertiaNet<small>THE KITCHEN SINK</small>
          </span>
        </Link>
        <p className="nav-label">
          THE COLLECTION <span>09</span>
        </p>
        <nav aria-label="Demos">
          {demos.map((demo, index) => (
            <Link
              key={demo.href}
              href={demo.href}
              prefetch="hover"
              cacheFor="10s"
              className={current === demo.href ? "nav-link active" : "nav-link"}
            >
              <span className="nav-icon">{demo.icon}</span>
              <span>{demo.name}</span>
              <small>{String(index + 1).padStart(2, "0")}</small>
            </Link>
          ))}
        </nav>
        <div className="sidebar-note">
          <span className="status-dot" /> A working little laboratory.
          <p>
            ASP.NET Core on the server.
            <br />
            React in the browser.
            <br />
            Inertia in between.
          </p>
        </div>
        <a
          className="source-link"
          href="https://inertiajs.com/docs/v3/getting-started"
          target="_blank"
          rel="noreferrer"
        >
          Inertia documentation ↗
        </a>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <span>
            Samples <span className="slash">/</span> Kitchen sink
          </span>
          <div>
            <span className="technology">.NET 10 + React</span>
            <span className="avatar" title={page.props.auth?.name ?? "Guest"}>
              {(page.props.auth?.name ?? "Guest")[0]}
            </span>
          </div>
        </header>
        <main>
          {flash && (
            <div className="flash" role="status" data-testid="flash">
              ✓ {flash}
            </div>
          )}
          {children}
        </main>
        <footer>
          <span>
            <span className="status-dot" /> Request{" "}
            <code data-testid="request-id">{page.props.request?.id}</code>
          </span>
          <button
            className="text-button"
            onClick={() => setInspect(!inspect)}
            aria-expanded={inspect}
          >
            {inspect ? "Close" : "Inspect"} response {inspect ? "↓" : "↑"}
          </button>
        </footer>
        {inspect && (
          <section className="inspector" aria-label="Response inspector">
            <div>
              <h2>Under the hood</h2>
              <p>
                Current Inertia page, including deferred, merge, scroll, once
                and history metadata.
              </p>
              <p>
                Client events:{" "}
                <code data-testid="events">
                  {events.join(" → ") || "Navigate to record events"}
                </code>
              </p>
            </div>
            <pre data-testid="page-json">{JSON.stringify(page, null, 2)}</pre>
          </section>
        )}
      </div>
    </div>
  );
}

export function Intro({
  eyebrow,
  title,
  children,
  tag,
}: {
  eyebrow: string;
  title: string;
  children: ReactNode;
  tag: string;
}) {
  return (
    <div className="intro">
      <div className="eyebrow">{eyebrow}</div>
      <div className="title-line">
        <h1>{title}</h1>
        <span className="pill">{tag}</span>
      </div>
      <p>{children}</p>
    </div>
  );
}
export function Code({ children }: { children: string }) {
  return (
    <pre className="code-example">
      <code>{children}</code>
    </pre>
  );
}
