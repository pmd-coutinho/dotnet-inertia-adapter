import { Head, usePoll } from "@inertiajs/react";
import { Code, Intro } from "../Layout";

export default function Polling({
  pulse,
  initial,
}: {
  pulse: { at: string; contacts: number; requestId: string };
  initial: string;
}) {
  const { start, stop, polling } = usePoll(2000, { only: ["pulse"] });
  return (
    <>
      <Head title="Live updates" />
      <Intro
        eyebrow="06 / A FINGER ON THE PULSE"
        title="Quietly keeping up."
        tag="POLLING"
      >
        Refresh just the changing prop every two seconds. Inertia throttles
        background-tab polling and stops it when this page unmounts.
      </Intro>
      <section className="panel">
        <div className="section-heading">
          <h2>
            <span
              className={polling ? "status-dot pulse" : "status-dot paused"}
            />{" "}
            {polling ? "Listening for updates" : "Updates paused"}
          </h2>
          <button className="secondary" onClick={polling ? stop : start}>
            {polling ? "Pause updates" : "Resume updates"}
          </button>
        </div>
        <div className="metric-grid">
          <div className="metric">
            <strong data-testid="poll-count">{pulse.contacts}</strong>
            <span>Contacts in the database</span>
          </div>
          <div className="metric">
            <strong className="mono" data-testid="poll-id">
              {pulse.requestId}
            </strong>
            <span>Latest server request</span>
          </div>
        </div>
        <p>
          Last checked: <code data-testid="poll-time">{pulse.at}</code>
        </p>
        <p className="muted">
          The initial prop never refreshes:{" "}
          <code data-testid="poll-initial">{initial}</code>
        </p>
      </section>
      <Code>
        {
          'const { start, stop, polling } = usePoll(2000, {\n  only: ["pulse"],\n})'
        }
      </Code>
    </>
  );
}
