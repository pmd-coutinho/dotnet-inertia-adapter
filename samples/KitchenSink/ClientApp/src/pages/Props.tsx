import { Deferred, Head, router, usePage, WhenVisible } from "@inertiajs/react";
import { Code, Intro } from "../Layout";
import type { PropsFor } from "../pathfinder/types/page-map";
import Demos, { type DemosPropsQuery } from "../pathfinder/actions/demos";
export default function Props({
  initial,
  optional,
  metrics,
  summary,
  fragile,
  once,
  visible,
}: PropsFor<"Props">) {
  const page = usePage<PropsFor<"Props">>();
  return (
    <>
      <Head title="Loading props" />
      <Intro
        eyebrow="03 / A LITTLE NOW, A LITTLE LATER"
        title="Everything at its own pace."
        tag="LAZY BY DESIGN"
      >
        Keep the first response light. Fetch expensive data later, ask for
        optional data on demand, and cache reference data across visits.
      </Intro>
      <div className="two-columns equal">
        <section className="panel">
          <span className="mini-label">DEFERRED · GROUPED</span>
          <h2>Let the page arrive first.</h2>
          <Deferred
            data={["metrics", "summary"]}
            fallback={
              <div className="skeleton" role="status">
                Loading the dashboard group…
              </div>
            }
          >
            <div className="metric-grid">
              {metrics?.map((metric) => (
                <div className="metric" key={metric.label}>
                  <strong>{metric.value}</strong>
                  <span>{metric.label}</span>
                </div>
              ))}
            </div>
            <p data-testid="deferred-summary">{summary}</p>
          </Deferred>
          <Code>
            {
              'metrics = inertia.Defer(LoadMetrics, "dashboard"),\nsummary = inertia.Defer(LoadSummary, "dashboard")'
            }
          </Code>
        </section>
        <section className="panel">
          <span className="mini-label">OPTIONAL · ON DEMAND</span>
          <h2>Only when you ask.</h2>
          <p data-testid="optional">
            {optional?.message ??
              "This prop was left out of the initial response."}
          </p>
          {optional && <code>{optional.at}</code>}
          <button onClick={() => router.reload({ only: ["optional"] })}>
            Fetch optional prop ↗
          </button>
          <p className="muted">
            Initial value stays put:{" "}
            <code data-testid="initial">{initial}</code>
          </p>
          <p className="muted">
            The always-included request ID in the footer still changes.
          </p>
        </section>
        <section className="panel">
          <span className="mini-label">DEFERRED · RESCUED</span>
          <h2>A failure with a soft landing.</h2>
          <Deferred
            data="fragile"
            fallback={<p>Trying a deliberately unreliable service…</p>}
            rescue={
              <div>
                <p className="error" data-testid="rescued">
                  The optional service failed. The rest of the page is fine.
                </p>
                <button
                  onClick={() =>
                    router.visit(
                      Demos.props({
                        query: { recover: true } satisfies DemosPropsQuery,
                      }),
                      {
                        only: ["fragile"],
                        preserveState: true,
                        preserveScroll: true,
                      },
                    )
                  }
                >
                  Retry rescued prop
                </button>
              </div>
            }
          >
            <p className="success-note" data-testid="recovered">
              {fragile}
            </p>
          </Deferred>
          <Code>
            {"fragile = inertia.Defer(LoadFragileService)\n    .Rescue()"}
          </Code>
        </section>
        <section className="panel">
          <span className="mini-label">ONCE · CACHED</span>
          <h2>Remember the useful bits.</h2>
          <p>
            Page token: <code data-testid="once-token">{once?.token}</code>
          </p>
          <p className="muted">
            Resolved at {once?.at}. Cached for five minutes.
          </p>
          <button className="secondary" onClick={() => router.reload()}>
            Reload page props
          </button>
          <p>
            Global reference loaded at{" "}
            <code data-testid="reference-time">
              {page.props.reference?.loadedAt}
            </code>
            . Navigate between demos: the shared once prop keeps its timestamp.
          </p>
        </section>
      </div>
      <section className="panel visibility-demo">
        <span className="mini-label">WHEN VISIBLE</span>
        <h2>Just in sight. Just in time.</h2>
        <p>
          Scroll down to fetch this optional prop as it enters the viewport.
        </p>
        <div className="visibility-spacer" />
        <WhenVisible
          data="visible"
          fallback={<p>Waiting to enter the viewport…</p>}
        >
          <p className="success-note" data-testid="visible-prop">
            {visible}
          </p>
        </WhenVisible>
      </section>
    </>
  );
}
