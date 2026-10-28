import { Head, router } from "@inertiajs/react";
import { Code, Intro } from "../Layout";
import type { Entry } from "../types";

export default function Merging({
  batch,
  entries,
  preferences,
}: {
  batch: number;
  entries: Entry[];
  preferences: Record<string, unknown>;
}) {
  function load(next: number, direction = "append", reset = false) {
    router.get(
      "/merging",
      { batch: next, direction },
      {
        only: ["entries", "batch", "preferences"],
        preserveState: true,
        preserveScroll: true,
        reset: reset ? ["entries", "preferences"] : [],
      },
    );
  }
  return (
    <>
      <Head title="Merging data" />
      <Intro
        eyebrow="04 / MAKE ROOM FOR MORE"
        title="A growing collection."
        tag="MERGE PROPS"
      >
        Partial reloads can append, prepend, and recursively merge incoming
        data. Matching on an ID updates existing entries without duplicating
        them.
      </Intro>
      <div className="two-columns equal">
        <section className="panel">
          <h2>Activity collection</h2>
          <div className="toolbar">
            <button disabled={batch >= 20} onClick={() => load(batch + 1)}>
              Append batch
            </button>
            <button
              className="secondary"
              disabled={batch >= 20}
              onClick={() => load(batch + 1, "prepend")}
            >
              Prepend batch
            </button>
            <button className="secondary" onClick={() => load(batch)}>
              Repeat batch
            </button>
            <button
              className="text-button"
              onClick={() => load(1, "append", true)}
            >
              Reset collection
            </button>
          </div>
          <p className="muted">
            Current batch: {batch}. Repeat it to see ID matching in action.
          </p>
          <ul className="activity-list" data-testid="merged-entries">
            {entries.map((entry) => (
              <li key={entry.id}>
                <span className="activity-dot" />
                <strong>{entry.title}</strong>
                <span>{entry.team}</span>
              </li>
            ))}
          </ul>
        </section>
        <section className="panel">
          <h2>Nested preferences</h2>
          <p>
            Odd batches update appearance. Even batches update notifications.
            Deep merging preserves the other branch.
          </p>
          <pre className="json-preview" data-testid="preferences">
            {JSON.stringify(preferences, null, 2)}
          </pre>
          <Code>
            {
              'entries = inertia.Merge(entries).MatchOn("id"),\npreferences = inertia.DeepMerge(preferences)\n\n// Or insert new entries at the beginning:\ninertia.Merge(entries).Prepend().MatchOn("id")'
            }
          </Code>
        </section>
      </div>
    </>
  );
}
