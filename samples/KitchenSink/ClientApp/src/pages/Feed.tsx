import { Head, InfiniteScroll } from "@inertiajs/react";
import { Code, Intro } from "../Layout";
import type { PropsFor } from "../pathfinder/types/page-map";

export default function Feed({ feed }: PropsFor<"Feed">) {
  return (
    <>
      <Head title="Infinite scroll" />
      <Intro
        eyebrow="05 / KEEP THE GOOD THINGS COMING"
        title="A stream of small updates."
        tag="INFINITE SCROLL"
      >
        Scroll to automatically load the next page. After two pages, a button
        takes over. Inertia merges entries and tracks pagination in the URL.
      </Intro>
      <div className="two-columns">
        <section>
          <InfiniteScroll
            data="feed"
            onlyNext
            manualAfter={2}
            loading={<p role="status">Fetching the next page…</p>}
            next={({ fetch, hasMore, loading }) =>
              hasMore ? (
                <button disabled={loading} onClick={fetch}>
                  {loading ? "Loading…" : "Load more updates"}
                </button>
              ) : (
                <p className="empty">
                  You’re all caught up. Thirty little updates, one continuous
                  feed.
                </p>
              )
            }
          >
            <div className="feed-items" data-testid="feed-items">
              {feed.data.map((entry) => (
                <article className="feed-card" key={entry.id}>
                  <div className="card-top">
                    <span className="team-label">{entry.team}</span>
                    <span className="muted">
                      #{String(entry.id).padStart(2, "0")}
                    </span>
                  </div>
                  <h2>{entry.title}</h2>
                  <p>
                    A server-driven page, folded into the current collection. No
                    separate JSON endpoint required.
                  </p>
                </article>
              ))}
            </div>
          </InfiniteScroll>
        </section>
        <aside className="panel sticky">
          <h2>One prop. A whole feed.</h2>
          <p>
            The server returns a scroll prop with current, previous and next
            page metadata. The client handles intersection observers and merge
            intent.
          </p>
          <Code>
            {
              'feed = inertia.Scroll(data,\n    metadata: new ScrollMetadata\n    {\n        CurrentPage = page,\n        NextPage = nextPage,\n    }).MatchOn("data.id")'
            }
          </Code>
          <p className="muted">
            Five pages of deterministic demo data. The contacts demo uses the
            persistent database.
          </p>
        </aside>
      </div>
    </>
  );
}
