import type {
  DemosContactsQuery,
  DemosLoginPayload,
  DemosRenderingQuery,
  DemosSaveProfilePayload,
  DemosStoreContactPayload,
  DemosUploadForm,
} from "../src/pathfinder/actions/demos";
import type { ComponentName, PropsFor } from "../src/pathfinder/types/page-map";

// Compile-time regression checks: generation must retain the server contracts.
// These directives must produce an error, otherwise `npm run typecheck` fails.
type Contact = PropsFor<"Contacts">["contacts"]["data"][number];
type FileInput = DemosUploadForm["file"];

export function verifyContracts(contact: Contact, file: FileInput) {
  const valid = {
    contact: {
      name: contact.name,
      email: contact.email,
      team: contact.team,
    } satisfies DemosStoreContactPayload,
    profile: {
      name: contact.name,
      email: contact.email,
    } satisfies DemosSaveProfilePayload,
    login: { name: contact.name } satisfies DemosLoginPayload,
    query: { search: contact.name, page: 2 } satisfies DemosContactsQuery,
    rendering: { failSsr: true } satisfies DemosRenderingQuery,
    upload: { file } satisfies DemosUploadForm,
    page: "Contacts" satisfies ComponentName,
  };

  // @ts-expect-error A required server DTO field cannot be omitted.
  const missingEmail: DemosStoreContactPayload = {
    name: "Reader",
    team: "Design",
  };
  // @ts-expect-error Server query names are checked rather than silently ignored.
  const wrongQuery: DemosContactsQuery = { seach: "Reader" };
  // @ts-expect-error Server boolean query parameters are not strings.
  const wrongBoolean: DemosRenderingQuery = { failSsr: "true" };
  // @ts-expect-error An upload must carry a file, not a filename.
  const wrongFile: DemosUploadForm = { file: "photo.png" };
  // @ts-expect-error Component names come from the generated page map.
  const missingPage: ComponentName = "MissingPage";
  // @ts-expect-error SQLite contact IDs are numbers in the server contract.
  const wrongId: Contact["id"] = "42";

  return {
    valid,
    missingEmail,
    wrongQuery,
    wrongBoolean,
    wrongFile,
    missingPage,
    wrongId,
  };
}
