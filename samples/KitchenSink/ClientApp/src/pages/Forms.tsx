import { Head, useForm, usePage } from "@inertiajs/react";
import { Code, Intro } from "../Layout";
import Demos from "../pathfinder/actions/demos";

export default function Forms() {
  const profile = useForm("post", Demos.saveProfile.url(), {
    name: "",
    email: "",
  });
  const upload = useForm<{ file: File | null }>({ file: null });
  const page = usePage();
  const receipt = page.flash?.upload as
    | { name: string; bytes: number; contentType: string }
    | undefined;
  return (
    <>
      <Head title="Forms & uploads" />
      <Intro
        eyebrow="02 / FORMS THAT FEEL AT HOME"
        title="Good input. Helpful feedback."
        tag="PRECOGNITION"
      >
        The same server rules validate while you type and when you submit.
        Validation-only requests never execute the save action.
      </Intro>
      <div className="two-columns equal">
        <section className="panel">
          <h2>A little introduction</h2>
          <p className="muted">
            Leave a field to validate it against C# DataAnnotations.
          </p>
          <form
            className="stack"
            onSubmit={(event) => {
              event.preventDefault();
              profile.submit({ errorBag: "profile", preserveScroll: true });
            }}
          >
            <label>
              Profile name
              <input
                value={profile.data.name}
                onChange={(event) =>
                  profile.setData("name", event.target.value)
                }
                onBlur={() => profile.validate("name")}
              />
            </label>
            <span className="error" data-testid="profile-name-error">
              {profile.errors.name}
            </span>
            <label>
              Profile email
              <input
                value={profile.data.email}
                onChange={(event) =>
                  profile.setData("email", event.target.value)
                }
                onBlur={() => profile.validate("email")}
              />
            </label>
            <span className="error">{profile.errors.email}</span>
            <div className="inline">
              <button disabled={profile.processing}>Save profile</button>
              <button
                className="secondary"
                type="button"
                onClick={() => profile.resetAndClearErrors()}
              >
                Reset
              </button>
            </div>
            <small className="muted" role="status">
              {profile.validating
                ? "Checking with the server…"
                : profile.isDirty
                  ? "You have unsaved changes."
                  : "Ready when you are."}
            </small>
          </form>
        </section>
        <section className="panel">
          <h2>Something to send</h2>
          <p className="muted">
            Inertia converts a file into FormData and reports upload progress.
            Up to 2 MB; the server reads and discards the file.
          </p>
          <form
            className="stack"
            onSubmit={(event) => {
              event.preventDefault();
              upload.post(Demos.upload.url(), {
                errorBag: "upload",
                forceFormData: true,
                preserveScroll: true,
              });
            }}
          >
            <label className="upload-zone">
              Choose a file
              <input
                type="file"
                onChange={(event) =>
                  upload.setData("file", event.target.files?.[0] ?? null)
                }
              />
            </label>
            <span className="error">{upload.errors.file}</span>
            {upload.progress && (
              <progress
                value={upload.progress.percentage}
                max="100"
                aria-label="Upload progress"
              />
            )}
            <button disabled={upload.processing}>Upload file ↗</button>
            {upload.processing && (
              <button
                type="button"
                className="secondary"
                onClick={() => upload.cancel()}
              >
                Cancel upload
              </button>
            )}
          </form>
          {receipt && (
            <div className="success-note" data-testid="upload-receipt">
              <strong>{receipt.name}</strong>
              <p>
                {receipt.bytes} bytes · {receipt.contentType}
              </p>
            </div>
          )}
        </section>
      </div>
      <section className="panel">
        <h2>Two forms. Two error bags.</h2>
        <p>
          The profile and upload forms send different error-bag names. Submit
          either one empty: only that form displays its errors. Unsafe requests
          include the real ASP.NET antiforgery cookie/header pair.
        </p>
        <Code>
          {
            'const form = useForm("post", "/forms/profile", { name: "", email: "" })\n\n// Runs server validation, without saving:\nform.validate("email")\n\n// Saves and follows the server redirect:\nform.submit({ errorBag: "profile" })'
          }
        </Code>
      </section>
    </>
  );
}
