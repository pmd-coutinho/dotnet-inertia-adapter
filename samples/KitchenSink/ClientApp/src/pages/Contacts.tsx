import { Head, Link, router, useForm, usePage } from "@inertiajs/react";
import { useState, type FormEvent } from "react";
import Demos from "../pathfinder/actions/demos";
import type { PropsFor } from "../pathfinder/types/page-map";
import { formFields } from "../pathfinder";
import { Code, Intro } from "../Layout";
import type { Shared } from "../types";

export default function Contacts({ contacts, search }: PropsFor<"Contacts">) {
  const { props } = usePage<Shared>();
  const form = useForm({ name: "", email: "", team: "Design" });
  const [editing, setEditing] = useState<number | null>(null);
  const [query, setQuery] = useState(search);
  function submit(event: FormEvent) {
    event.preventDefault();
    form.submit(editing ? Demos.updateContact(editing) : Demos.storeContact(), {
      errorBag: "contact",
      preserveScroll: true,
      onSuccess: () => {
        form.reset();
        setEditing(null);
      },
    });
  }
  return (
    <>
      <Head title="Contacts" />
      <Intro
        eyebrow="01 / THE CLASSIC CRUD"
        title="A few familiar faces."
        tag="PERSISTED IN SQLITE"
      >
        Search, create, update, and delete real records. These links and forms
        use Pathfinder routes generated from the C# controller.
      </Intro>
      <div className="two-columns">
        <section className="panel">
          <div className="section-heading">
            <h2>
              Contacts <span className="count">{contacts.total}</span>
            </h2>
          </div>
          <form
            className="search-form"
            onSubmit={(event) => {
              event.preventDefault();
              router.visit(Demos.contacts({ query: { search: query } }), {
                only: ["contacts", "search"],
                preserveState: true,
                replace: true,
              });
            }}
          >
            <label className="sr-only" htmlFor="search">
              Search contacts
            </label>
            <input
              id="search"
              placeholder="Search people or email…"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
            <button className="secondary">Search</button>
          </form>
          <div className="contact-list">
            {contacts.data.map((contact) => (
              <article
                className="contact-row"
                key={contact.id}
                data-testid="contact"
              >
                <span className="contact-avatar">
                  {contact.name
                    .split(" ")
                    .map((part) => part[0])
                    .slice(0, 2)
                    .join("")}
                </span>
                <div>
                  <strong>{contact.name}</strong>
                  <small>{contact.email}</small>
                  <span className="team-label">{contact.team}</span>
                </div>
                <div className="row-actions">
                  <button
                    className="text-button"
                    onClick={() => {
                      setEditing(contact.id);
                      form.setData({
                        name: contact.name,
                        email: contact.email,
                        team: contact.team,
                      });
                    }}
                  >
                    Edit<span className="sr-only"> {contact.name}</span>
                  </button>
                  <button
                    className="text-button danger"
                    onClick={() =>
                      router.visit(Demos.deleteContact(contact.id), {
                        preserveScroll: true,
                      })
                    }
                  >
                    Delete<span className="sr-only"> {contact.name}</span>
                  </button>
                </div>
              </article>
            ))}
            {contacts.data.length === 0 && (
              <p className="empty">No contacts match your search.</p>
            )}
          </div>
          <div className="pagination">
            <span>Page {contacts.currentPage}</span>
            <div>
              {contacts.currentPage > 1 && (
                <Link
                  href={Demos.contacts({
                    query: { search, page: contacts.currentPage - 1 },
                  })}
                  only={["contacts", "search"]}
                  preserveState
                >
                  ← Previous
                </Link>
              )}
              {contacts.nextPage && (
                <Link
                  href={Demos.contacts({
                    query: { search, page: contacts.nextPage },
                  })}
                  only={["contacts", "search"]}
                  preserveState
                >
                  Next →
                </Link>
              )}
            </div>
          </div>
        </section>
        <div>
          <section className="panel">
            <h2>{editing ? "Edit contact" : "Make an introduction"}</h2>
            <p className="muted">
              Server validation, without leaving the page.
            </p>
            <form onSubmit={submit} className="stack">
              <label>
                Name
                <input
                  value={form.data.name}
                  onChange={(event) => form.setData("name", event.target.value)}
                  aria-invalid={!!form.errors.name}
                />
              </label>
              <span className="error">{form.errors.name}</span>
              <label>
                Email
                <input
                  type="text"
                  inputMode="email"
                  value={form.data.email}
                  onChange={(event) =>
                    form.setData("email", event.target.value)
                  }
                  aria-invalid={!!form.errors.email}
                />
              </label>
              <span className="error">{form.errors.email}</span>
              <label>
                Team
                <select
                  value={form.data.team}
                  onChange={(event) => form.setData("team", event.target.value)}
                >
                  {props.reference.teams.map((team) => (
                    <option key={team}>{team}</option>
                  ))}
                </select>
              </label>
              <span className="error">{form.errors.team}</span>
              <button disabled={form.processing}>
                {editing ? "Update contact" : "Create contact"} <span>↗</span>
              </button>
              {editing && (
                <button
                  type="button"
                  className="secondary"
                  onClick={() => {
                    setEditing(null);
                    form.resetAndClearErrors();
                  }}
                >
                  Cancel edit
                </button>
              )}
            </form>
          </section>
          {contacts.data[0] && (
            <details className="panel">
              <summary>Try a native method-override form</summary>
              <p>
                Submits a regular POST with <code>_method=DELETE</code> and an
                antiforgery field.
              </p>
              <form
                action={Demos.deleteContact.form(contacts.data[0].id).action}
                method="post"
              >
                {formFields(Demos.deleteContact.form(contacts.data[0].id)).map(
                  (field) => (
                    <input type="hidden" key={field.name} {...field} />
                  ),
                )}
                <input
                  type="hidden"
                  name="__RequestVerificationToken"
                  value={props.xsrfToken}
                />
                <button className="secondary">
                  Delete first contact natively
                </button>
              </form>
            </details>
          )}
          <Code>
            {
              'form.submit(Demos.storeContact(), {\n  errorBag: "contact",\n  preserveScroll: true,\n})'
            }
          </Code>
        </div>
      </div>
    </>
  );
}
